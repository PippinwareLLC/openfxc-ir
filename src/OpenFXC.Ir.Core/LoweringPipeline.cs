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
        var stage = ResolveStage(semantic);

        var module = new IrModule
        {
            Profile = profile,
            EntryPoint = new IrEntryPoint
            {
                Function = entry,
                Stage = stage
            },
            Diagnostics = new[]
            {
                IrDiagnostic.Info("IR lowering not implemented (M0 skeleton)")
            }
        };

        return module with { Diagnostics = module.Diagnostics.Concat(IrInvariants.Validate(module)).ToArray() };
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

    private static string ResolveEntry(LoweringRequest request, SemanticOutput semantic)
    {
        if (!string.IsNullOrWhiteSpace(request.Entry))
        {
            return request.Entry!;
        }

        var semanticEntry = semantic.EntryPoints.FirstOrDefault()?.Name;
        if (!string.IsNullOrWhiteSpace(semanticEntry))
        {
            return semanticEntry!;
        }

        return "main";
    }

    private static string ResolveStage(SemanticOutput semantic)
    {
        var stage = semantic.EntryPoints.FirstOrDefault()?.Stage;
        if (!string.IsNullOrWhiteSpace(stage))
        {
            return stage!;
        }

        return "Unknown";
    }
}
