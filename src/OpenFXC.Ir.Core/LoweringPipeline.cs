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
        var valueBySymbol = new Dictionary<int, IrValue>();

        if (entrySymbol is not null)
        {
            LowerResources(semantic, resources, values, valueBySymbol);
            LowerParameters(entrySymbol, semantic, values, valueBySymbol);
            var function = LowerFunction(entrySymbol, semantic, values, valueBySymbol, diagnostics);
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

    private static void LowerParameters(SymbolInfo entrySymbol, SemanticOutput semantic, List<IrValue> values, Dictionary<int, IrValue> valueBySymbol)
    {
        foreach (var parameter in semantic.Symbols.Where(s =>
                     string.Equals(s.Kind, "Parameter", StringComparison.OrdinalIgnoreCase) &&
                     s.ParentSymbolId == entrySymbol.Id))
        {
            EnsureValue(parameter, values, valueBySymbol, defaultKind: "Parameter");
        }
    }

    private static IrFunction LowerFunction(SymbolInfo entrySymbol, SemanticOutput semantic, List<IrValue> values, Dictionary<int, IrValue> valueBySymbol, List<IrDiagnostic> diagnostics)
    {
        var usedIds = new HashSet<int>(values.Select(v => v.Id));
        var parameters = values.Where(v => string.Equals(v.Kind, "Parameter", StringComparison.OrdinalIgnoreCase)).Select(v => v.Id).ToList();
        int? firstParameterId = parameters.FirstOrDefault();

        var typeByNode = semantic.Types
            .Where(t => t.NodeId is not null && !string.IsNullOrWhiteSpace(t.Type))
            .ToDictionary(t => t.NodeId!.Value, t => t.Type!);

        var nodeById = BuildSyntaxLookup(semantic.Syntax);
        var functionNode = entrySymbol.DeclNodeId is int declId && nodeById.TryGetValue(declId, out var fnNode) ? fnNode : null;

        var instructions = new List<IrInstruction>();
        int? returnValueId = null;
        string returnType = ParseReturnType(entrySymbol.Type);

        if (functionNode is not null)
        {
            var returnExprId = FindReturnExpression(nodeById, functionNode.Id ?? -1);
            if (returnExprId is not null)
            {
                returnValueId = LowerExpression(returnExprId.Value, nodeById, typeByNode, semantic.Symbols, values, valueBySymbol, usedIds, instructions, diagnostics);
                if (returnValueId is null && !IsVoid(returnType))
                {
                    diagnostics.Add(IrDiagnostic.Error("Failed to lower return expression.", "lower"));
                }
            }
        }

        int? returnUndefId = null;
        var returnOperand = returnValueId ?? firstParameterId;
        if (!IsVoid(returnType) && returnOperand is null)
        {
            returnUndefId = AllocateId(null, usedIds);
            var undef = new IrValue
            {
                Id = returnUndefId.Value,
                Kind = "Undef",
                Type = returnType,
                Name = "undef_return"
            };
            values.Add(undef);
            returnOperand = returnUndefId;
        }

        var returnInstruction = new IrInstruction
        {
            Op = "Return",
            Operands = returnOperand is null ? Array.Empty<int>() : new[] { returnOperand.Value },
            Type = returnType,
            Terminator = true
        };

        instructions.Add(returnInstruction);

        var block = new IrBlock
        {
            Id = "entry",
            Instructions = instructions
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

    private static void LowerResources(SemanticOutput semantic, List<IrResource> resources, List<IrValue> values, Dictionary<int, IrValue> valueBySymbol)
    {
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

            EnsureValue(symbol, values, valueBySymbol, defaultKind: symbol.Kind ?? "Resource");
        }
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

    private static Dictionary<int, SyntaxNodeInfo> BuildSyntaxLookup(SyntaxInfo? syntax)
    {
        var map = new Dictionary<int, SyntaxNodeInfo>();
        if (syntax?.Nodes is null) return map;
        foreach (var node in syntax.Nodes)
        {
            if (node.Id is int id)
            {
                map[id] = node;
            }
        }
        return map;
    }

    private static int? FindReturnExpression(Dictionary<int, SyntaxNodeInfo> nodes, int rootId)
    {
        if (!nodes.TryGetValue(rootId, out var node)) return null;
        if (string.Equals(node.Kind, "ReturnStatement", StringComparison.OrdinalIgnoreCase))
        {
            var exprChild = node.Children.FirstOrDefault(c => string.Equals(c.Role, "expression", StringComparison.OrdinalIgnoreCase));
            if (exprChild.NodeId is int exprId)
            {
                return exprId;
            }
            return null;
        }

        foreach (var child in node.Children)
        {
            if (child.NodeId is int childId)
            {
                var found = FindReturnExpression(nodes, childId);
                if (found is not null) return found;
            }
        }

        return null;
    }

    private static int? LowerExpression(int nodeId, Dictionary<int, SyntaxNodeInfo> nodes, Dictionary<int, string> typeByNode, IReadOnlyList<SymbolInfo> symbols, List<IrValue> values, Dictionary<int, IrValue> valueBySymbol, HashSet<int> usedIds, List<IrInstruction> instructions, List<IrDiagnostic> diagnostics)
    {
        if (!nodes.TryGetValue(nodeId, out var node))
        {
            diagnostics.Add(IrDiagnostic.Error($"Syntax node {nodeId} not found.", "lower"));
            return null;
        }

        if (string.Equals(node.Kind, "Identifier", StringComparison.OrdinalIgnoreCase))
        {
            if (node.ReferencedSymbolId is int symId)
            {
                var symbol = symbols.FirstOrDefault(s => s.Id == symId);
                if (symbol is not null)
                {
                    return EnsureValue(symbol, values, valueBySymbol)?.Id;
                }
            }

            diagnostics.Add(IrDiagnostic.Error($"Identifier node {node.Id} missing referenced symbol.", "lower"));
            return null;
        }

        if (string.Equals(node.Kind, "MemberAccessExpression", StringComparison.OrdinalIgnoreCase))
        {
            if (node.ReferencedSymbolId is int memberSymId)
            {
                var symbol = symbols.FirstOrDefault(s => s.Id == memberSymId);
                if (symbol is not null)
                {
                    return EnsureValue(symbol, values, valueBySymbol)?.Id;
                }
            }

            var exprChildId = GetChildNodeId(node, "expression");
            if (exprChildId is int swizzleSourceId)
            {
                var sourceId = LowerExpression(swizzleSourceId, nodes, typeByNode, symbols, values, valueBySymbol, usedIds, instructions, diagnostics);
                if (sourceId is null)
                {
                    diagnostics.Add(IrDiagnostic.Error($"Failed to lower swizzle source for node {node.Id}.", "lower"));
                    return null;
                }

                var resultType = typeByNode.TryGetValue(nodeId, out var st) ? st : "unknown";
                var resultId = AllocateId(null, usedIds);
                values.Add(new IrValue
                {
                    Id = resultId,
                    Kind = "Temp",
                    Type = resultType
                });

                instructions.Add(new IrInstruction
                {
                    Op = "Swizzle",
                    Result = resultId,
                    Operands = new[] { sourceId.Value },
                    Type = resultType,
                    Tag = node.Swizzle
                });

                return resultId;
            }

            diagnostics.Add(IrDiagnostic.Error($"Member access node {node.Id} missing referenced symbol.", "lower"));
            return null;
        }

        if (string.Equals(node.Kind, "CallExpression", StringComparison.OrdinalIgnoreCase))
        {
            var argIds = new List<int>();
            string? calleeTag = null;
            string? calleeKind = null;
            var calleeChild = node.Children.FirstOrDefault(c => string.Equals(c.Role, "callee", StringComparison.OrdinalIgnoreCase));
            if (calleeChild.NodeId is int cId && nodes.TryGetValue(cId, out var calleeNode) && calleeNode.ReferencedSymbolId is int calleeSymId)
            {
                var calleeSym = symbols.FirstOrDefault(s => s.Id == calleeSymId);
                calleeTag = calleeSym?.Name;
            }
            calleeTag ??= node.CalleeName;
            calleeKind = node.CalleeKind;

            foreach (var child in node.Children.Where(c => string.Equals(c.Role, "argument", StringComparison.OrdinalIgnoreCase)))
            {
                if (child.NodeId is not int argNodeId) continue;
                var lowered = LowerExpression(argNodeId, nodes, typeByNode, symbols, values, valueBySymbol, usedIds, instructions, diagnostics);
                if (lowered is not null)
                {
                    argIds.Add(lowered.Value);
                }
            }

            var resultType = typeByNode.TryGetValue(nodeId, out var t) ? t : "unknown";
            var resultId = AllocateId(null, usedIds);
            var resultValue = new IrValue
            {
                Id = resultId,
                Kind = "Temp",
                Type = resultType
            };
            values.Add(resultValue);

            var opName = ResolveCallOp(calleeKind, calleeTag);

            instructions.Add(new IrInstruction
            {
                Op = opName,
                Result = resultId,
                Operands = argIds,
                Type = resultType,
                Tag = calleeTag
            });

            return resultId;
        }

        if (string.Equals(node.Kind, "BinaryExpression", StringComparison.OrdinalIgnoreCase))
        {
            var leftChild = node.Children.FirstOrDefault(c => string.Equals(c.Role, "left", StringComparison.OrdinalIgnoreCase));
            var rightChild = node.Children.FirstOrDefault(c => string.Equals(c.Role, "right", StringComparison.OrdinalIgnoreCase));
            var leftId = leftChild.NodeId is int lId
                ? LowerExpression(lId, nodes, typeByNode, symbols, values, valueBySymbol, usedIds, instructions, diagnostics)
                : null;
            var rightId = rightChild.NodeId is int rId
                ? LowerExpression(rId, nodes, typeByNode, symbols, values, valueBySymbol, usedIds, instructions, diagnostics)
                : null;

            if (leftId is null || rightId is null)
            {
                diagnostics.Add(IrDiagnostic.Error($"Failed to lower binary expression {node.Id}.", "lower"));
                return null;
            }

            var resultType = typeByNode.TryGetValue(nodeId, out var t) ? t : "unknown";
            var resultId = AllocateId(null, usedIds);
            values.Add(new IrValue
            {
                Id = resultId,
                Kind = "Temp",
                Type = resultType
            });

            instructions.Add(new IrInstruction
            {
                Op = ResolveBinaryOp(node.Operator),
                Result = resultId,
                Operands = new[] { leftId.Value, rightId.Value },
                Type = resultType
            });

            return resultId;
        }

        if (string.Equals(node.Kind, "UnaryExpression", StringComparison.OrdinalIgnoreCase))
        {
            var operandChild = node.Children.FirstOrDefault(c => string.Equals(c.Role, "operand", StringComparison.OrdinalIgnoreCase));
            var operandId = operandChild.NodeId is int opId
                ? LowerExpression(opId, nodes, typeByNode, symbols, values, valueBySymbol, usedIds, instructions, diagnostics)
                : null;
            if (operandId is null)
            {
                diagnostics.Add(IrDiagnostic.Error($"Failed to lower unary expression {node.Id}.", "lower"));
                return null;
            }

            var resultType = typeByNode.TryGetValue(nodeId, out var t) ? t : "unknown";
            var resultId = AllocateId(null, usedIds);
            values.Add(new IrValue
            {
                Id = resultId,
                Kind = "Temp",
                Type = resultType
            });

            var opName = ResolveUnaryOp(node.Operator);
            if (opName is null)
            {
                return operandId.Value;
            }

            instructions.Add(new IrInstruction
            {
                Op = opName,
                Result = resultId,
                Operands = new[] { operandId.Value },
                Type = resultType
            });

            return resultId;
        }

        if (string.Equals(node.Kind, "CastExpression", StringComparison.OrdinalIgnoreCase))
        {
            var exprChildId = GetChildNodeId(node, "expression");
            if (exprChildId is int castExprId)
            {
                var operandId = LowerExpression(castExprId, nodes, typeByNode, symbols, values, valueBySymbol, usedIds, instructions, diagnostics);
                if (operandId is null)
                {
                    diagnostics.Add(IrDiagnostic.Error($"Failed to lower cast expression {node.Id}.", "lower"));
                    return null;
                }

                var resultType = typeByNode.TryGetValue(nodeId, out var t) ? t : "unknown";
                var resultId = AllocateId(null, usedIds);
                values.Add(new IrValue
                {
                    Id = resultId,
                    Kind = "Temp",
                    Type = resultType
                });

                instructions.Add(new IrInstruction
                {
                    Op = "Cast",
                    Result = resultId,
                    Operands = new[] { operandId.Value },
                    Type = resultType
                });

                return resultId;
            }
        }

        diagnostics.Add(IrDiagnostic.Error($"Unsupported expression kind '{node.Kind}'.", "lower"));
        return null;
    }

    private static IrValue? EnsureValue(SymbolInfo symbol, List<IrValue>? values, Dictionary<int, IrValue> valueBySymbol, string? defaultKind = null)
    {
        if (symbol.Id is not int id)
        {
            return null;
        }

        if (valueBySymbol.TryGetValue(id, out var existing))
        {
            return existing;
        }

        var value = new IrValue
        {
            Id = id,
            Kind = defaultKind ?? symbol.Kind ?? "Temp",
            Type = symbol.Type ?? "unknown",
            Name = symbol.Name,
            Semantic = FormatSemantic(symbol.Semantic)
        };

        valueBySymbol[id] = value;
        values?.Add(value);
        return value;
    }

    private static string ResolveCallOp(string? calleeKind, string? calleeName)
    {
        if (string.Equals(calleeKind, "Intrinsic", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(calleeName, "mul", StringComparison.OrdinalIgnoreCase))
            {
                return "Mul";
            }
            if (calleeName is not null && calleeName.StartsWith("tex", StringComparison.OrdinalIgnoreCase))
            {
                return "Sample";
            }
            if (string.Equals(calleeName, "sample", StringComparison.OrdinalIgnoreCase))
            {
                return "Sample";
            }
        }

        return "Call";
    }

    private static string ResolveBinaryOp(string? op)
    {
        return op switch
        {
            "=" => "Assign",
            "+" => "Add",
            "-" => "Sub",
            "*" => "Mul",
            "/" => "Div",
            "%" => "Mod",
            "==" => "Eq",
            "!=" => "Ne",
            "<" => "Lt",
            "<=" => "Le",
            ">" => "Gt",
            ">=" => "Ge",
            "&&" => "LogicalAnd",
            "||" => "LogicalOr",
            _ => "Binary"
        };
    }

    private static string? ResolveUnaryOp(string? op)
    {
        return op switch
        {
            "-" => "Negate",
            "!" => "Not",
            "~" => "BitNot",
            "+" => null,
            _ => "Unary"
        };
    }

    private static int? GetChildNodeId(SyntaxNodeInfo node, string role)
    {
        var child = node.Children.FirstOrDefault(c => string.Equals(c.Role, role, StringComparison.OrdinalIgnoreCase));
        return child.NodeId;
    }
}
