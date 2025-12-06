using System.Text.Json;
using OpenFXC.Sem;

namespace OpenFXC.Ir;

public sealed class LoweringPipeline
{
    private static readonly JsonSerializerOptions SemanticSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public IrModule Lower(LoweringRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var semantic = DeserializeSemantic(request.SemanticJson);
        var profile = ResolveProfile(request, semantic);
        var entry = ResolveEntry(request, semantic);
        var entrySymbol = ResolveEntrySymbol(entry, semantic);
        var stage = entry?.Stage ?? "Unknown";

        var diagnostics = new List<IrDiagnostic>();
        if (entry is null)
        {
            diagnostics.Add(IrDiagnostic.Error($"Entry point '{request.EntryOrDefault}' not found in semantic model.", "lower"));
        }

        var values = new List<IrValue>();
        var resources = new List<IrResource>();
        var functions = new List<IrFunction>();

        if (entrySymbol is not null)
        {
            resources.AddRange(LowerResources(semantic));
            var function = LowerFunction(entrySymbol, semantic, values);
            functions.Add(function);
        }
        else if (entry is not null && entry.SymbolId is null)
        {
            diagnostics.Add(IrDiagnostic.Error("Entry point missing symbolId.", "lower"));
        }

        var module = new IrModule
        {
            Profile = profile,
            EntryPoint = entry is null
                ? null
                : new IrEntryPoint
                {
                    Function = entry.Name ?? request.EntryOrDefault,
                    Stage = stage
                },
            Values = values,
            Resources = resources,
            Functions = functions,
            Diagnostics = diagnostics
        };

        return module with
        {
            Diagnostics = module.Diagnostics.Concat(IrInvariants.Validate(module)).ToArray()
        };
    }

    private static SemanticOutput DeserializeSemantic(string semanticJson)
    {
        var semantic = JsonSerializer.Deserialize<SemanticOutput>(semanticJson, SemanticSerializerOptions);
        if (semantic is null)
        {
            throw new InvalidDataException("Semantic JSON deserialized to null.");
        }

        return semantic;
    }

    private static string ResolveProfile(LoweringRequest request, SemanticOutput semantic)
    {
        if (!string.IsNullOrWhiteSpace(request.Profile))
        {
            return request.Profile!;
        }

        if (!string.IsNullOrWhiteSpace(semantic.Profile))
        {
            return semantic.Profile!;
        }

        return "unknown";
    }

    private static EntryPointInfo? ResolveEntry(LoweringRequest request, SemanticOutput semantic)
    {
        if (!string.IsNullOrWhiteSpace(request.Entry))
        {
            var match = semantic.EntryPoints.FirstOrDefault(e => string.Equals(e.Name, request.Entry, StringComparison.OrdinalIgnoreCase));
            return match;
        }

        return semantic.EntryPoints.FirstOrDefault();
    }

    private static SymbolInfo? ResolveEntrySymbol(EntryPointInfo? entry, SemanticOutput semantic)
    {
        if (entry?.SymbolId is null)
        {
            return null;
        }

        return semantic.Symbols.FirstOrDefault(s => s.Id == entry.SymbolId);
    }

    private static IrFunction LowerFunction(SymbolInfo entrySymbol, SemanticOutput semantic, List<IrValue> values)
    {
        var usedIds = new HashSet<int>();
        var parameters = new List<int>();
        int? firstParameterId = null;

        foreach (var parameter in semantic.Symbols.Where(s =>
                     string.Equals(s.Kind, "Parameter", StringComparison.OrdinalIgnoreCase) &&
                     s.ParentSymbolId == entrySymbol.Id))
        {
            var id = AllocateId(parameter.Id, usedIds);
            var value = new IrValue
            {
                Id = id,
                Kind = "Parameter",
                Type = parameter.Type ?? "unknown",
                Name = parameter.Name,
                Semantic = FormatSemantic(parameter.Semantic)
            };
            values.Add(value);
            parameters.Add(id);
            firstParameterId ??= id;
        }

        var returnType = ParseReturnType(entrySymbol.Type);
        int? returnUndefId = null;
        var returnOperand = firstParameterId;
        if (!IsVoid(returnType) && returnOperand is null)
        {
            returnUndefId = AllocateId(null, usedIds);
            values.Add(new IrValue
            {
                Id = returnUndefId.Value,
                Kind = "Undef",
                Type = returnType,
                Name = "undef_return"
            });
            returnOperand = returnUndefId;
        }

        var returnInstruction = new IrInstruction
        {
            Op = "Return",
            Operands = returnOperand is null ? Array.Empty<int>() : new[] { returnOperand.Value },
            Type = returnType,
            Terminator = true
        };

        var block = new IrBlock
        {
            Id = "entry",
            Instructions = new[] { returnInstruction }
        };

        return new IrFunction
        {
            Name = entrySymbol.Name ?? "main",
            ReturnType = returnType,
            Parameters = parameters,
            Blocks = new[] { block }
        };
    }

    private static string ParseReturnType(string? functionType)
    {
        if (string.IsNullOrWhiteSpace(functionType))
            return "void";

        var idx = functionType.IndexOf('(');
        if (idx < 0)
            return functionType;

        return functionType[..idx];
    }

    private static bool IsVoid(string type) => string.Equals(type, "void", StringComparison.OrdinalIgnoreCase);

    private static int AllocateId(int? preferred, HashSet<int> used)
    {
        if (preferred is int pref && pref > 0 && used.Add(pref))
        {
            return pref;
        }

        var id = 1;
        while (!used.Add(id))
        {
            id++;
        }
        return id;
    }

    private static IReadOnlyList<IrResource> LowerResources(SemanticOutput semantic)
    {
        var resources = new List<IrResource>();
        foreach (var symbol in semantic.Symbols)
        {
            if (!IsResourceKind(symbol.Kind))
            {
                continue;
            }

            resources.Add(new IrResource
            {
                Name = symbol.Name ?? string.Empty,
                Kind = symbol.Kind ?? string.Empty,
                Type = symbol.Type ?? "unknown"
            });
        }

        return resources;
    }

    private static bool IsResourceKind(string? kind)
    {
        if (kind is null) return false;
        return kind.Equals("Sampler", StringComparison.OrdinalIgnoreCase)
               || kind.Equals("Texture", StringComparison.OrdinalIgnoreCase)
               || kind.Equals("Texture1D", StringComparison.OrdinalIgnoreCase)
               || kind.Equals("Texture2D", StringComparison.OrdinalIgnoreCase)
               || kind.Equals("Texture3D", StringComparison.OrdinalIgnoreCase)
               || kind.Equals("TextureCube", StringComparison.OrdinalIgnoreCase)
               || kind.Equals("GlobalVariable", StringComparison.OrdinalIgnoreCase)
               || kind.Equals("CBuffer", StringComparison.OrdinalIgnoreCase)
               || kind.Equals("Buffer", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FormatSemantic(SemanticInfo? semantic)
    {
        if (semantic is null) return null;
        return $"{semantic.Name}{semantic.Index ?? 0}";
    }
}
