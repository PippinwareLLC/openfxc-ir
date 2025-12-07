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

        var blocks = LowerFunctionBody(functionNode, entrySymbol, semantic, values, valueBySymbol, typeByNode, usedIds, firstParameterId, diagnostics);

        return new IrFunction
        {
            Name = entrySymbol.Name ?? "main",
            ReturnType = ParseReturnType(entrySymbol.Type),
            Parameters = parameters,
            Blocks = blocks
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

    private static int? LowerExpression(int nodeId, Dictionary<int, SyntaxNodeInfo> nodes, Dictionary<int, string> typeByNode, IReadOnlyList<SymbolInfo> symbols, List<IrValue> values, Dictionary<int, IrValue> valueBySymbol, HashSet<int> usedIds, List<IrInstruction> instructions, List<IrDiagnostic> diagnostics)
    {
        if (!nodes.TryGetValue(nodeId, out var node))
        {
            diagnostics.Add(IrDiagnostic.Error($"Syntax node {nodeId} not found.", "lower"));
            return null;
        }

        if (string.Equals(node.Kind, "Identifier", StringComparison.OrdinalIgnoreCase))
        {
            SymbolInfo? symbol = null;
            if (node.ReferencedSymbolId is int symId)
            {
                symbol = symbols.FirstOrDefault(s => s.Id == symId);
            }
            else
            {
                var type = typeByNode.TryGetValue(nodeId, out var t) ? t : null;
                symbol = TryInferSymbolByType(type, symbols);
            }

            if (symbol is not null)
            {
                if (symbol.Id is null)
                {
                    // Create a synthetic id to track the inferred symbol.
                    var syntheticId = AllocateId(null, usedIds);
                    symbol = new SymbolInfo
                    {
                        Id = syntheticId,
                        Kind = symbol.Kind,
                        Name = symbol.Name,
                        Type = symbol.Type,
                        ParentSymbolId = symbol.ParentSymbolId,
                        Semantic = symbol.Semantic
                    };
                }

                if (ShouldLoad(symbol.Kind))
                {
                    return EmitLoad(symbol, nodeId, typeByNode, values, valueBySymbol, usedIds, instructions);
                }

                return EnsureValue(symbol, values, valueBySymbol)?.Id;
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
                    if (ShouldLoad(symbol.Kind))
                    {
                        return EmitLoad(symbol, nodeId, typeByNode, values, valueBySymbol, usedIds, instructions, node.Swizzle);
                    }

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
            if (calleeChild?.NodeId is int cId && nodes.TryGetValue(cId, out var calleeNode) && calleeNode.ReferencedSymbolId is int calleeSymId)
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
            if (string.Equals(calleeKind, "Intrinsic", StringComparison.OrdinalIgnoreCase) && string.Equals(opName, "Call", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(IrDiagnostic.Error($"Intrinsic '{calleeTag ?? "unknown"}' not supported by IR lowering.", "lower"));
            }

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
            var leftId = leftChild?.NodeId is int lId
                ? LowerExpression(lId, nodes, typeByNode, symbols, values, valueBySymbol, usedIds, instructions, diagnostics)
                : null;
            var rightId = rightChild?.NodeId is int rId
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
            var operandId = operandChild?.NodeId is int opId
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

        if (string.Equals(node.Kind, "LiteralExpression", StringComparison.OrdinalIgnoreCase) || string.Equals(node.Kind, "Literal", StringComparison.OrdinalIgnoreCase))
        {
            var resultType = typeByNode.TryGetValue(nodeId, out var litType) ? litType : "unknown";
            var constId = AllocateId(null, usedIds);
            values.Add(new IrValue
            {
                Id = constId,
                Kind = "Constant",
                Type = resultType,
                Name = node.Operator ?? node.Swizzle ?? "literal"
            });

            return constId;
        }

        if (string.Equals(node.Kind, "IndexExpression", StringComparison.OrdinalIgnoreCase))
        {
            var targetId = GetChildNodeId(node, "expression");
            var indexId = GetChildNodeId(node, "index");
            var baseVal = targetId is int tId
                ? LowerExpression(tId, nodes, typeByNode, symbols, values, valueBySymbol, usedIds, instructions, diagnostics)
                : null;
            var idxVal = indexId is int iId
                ? LowerExpression(iId, nodes, typeByNode, symbols, values, valueBySymbol, usedIds, instructions, diagnostics)
                : null;

            if (baseVal is null || idxVal is null)
            {
                diagnostics.Add(IrDiagnostic.Error($"Failed to lower index expression {node.Id}.", "lower"));
                return null;
            }

            var resultType = typeByNode.TryGetValue(nodeId, out var it) ? it : "unknown";
            var resultId = AllocateId(null, usedIds);
            values.Add(new IrValue
            {
                Id = resultId,
                Kind = "Temp",
                Type = resultType
            });

            instructions.Add(new IrInstruction
            {
                Op = "Index",
                Result = resultId,
                Operands = new[] { baseVal.Value, idxVal.Value },
                Type = resultType
            });

            return resultId;
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
        var op = ResolveIntrinsicByName(calleeName);
        if (string.Equals(calleeKind, "Intrinsic", StringComparison.OrdinalIgnoreCase) && op is not null)
        {
            return op;
        }

        if (op is not null)
        {
            return op;
        }

        return "Call";
    }

    private static string? ResolveIntrinsicByName(string? calleeName)
    {
        return calleeName?.ToLowerInvariant() switch
        {
            "mul" => "Mul",
            "dot" => "Dot",
            "normalize" => "Normalize",
            "saturate" => "Saturate",
            "sin" => "Sin",
            "cos" => "Cos",
            "abs" => "Abs",
            "min" => "Min",
            "max" => "Max",
            "clamp" => "Clamp",
            "lerp" => "Lerp",
            "pow" => "Pow",
            "exp" => "Exp",
            "log" => "Log",
            "step" => "Step",
            "smoothstep" => "SmoothStep",
            "reflect" => "Reflect",
            "refract" => "Refract",
            "atan2" => "Atan2",
            "fma" => "Fma",
            "ddx" => "Ddx",
            "ddy" => "Ddy",
            "length" => "Length",
            "rsqrt" => "Rsqrt",
            "rcp" => "Rcp",
            var name when name is not null && name.StartsWith("tex") => "Sample",
            "sample" => "Sample",
            _ => null
        };
    }

    private static SymbolInfo? TryInferSymbolByType(string? type, IReadOnlyList<SymbolInfo> symbols)
    {
        if (string.IsNullOrWhiteSpace(type)) return null;

        var candidates = symbols.Where(s =>
            string.Equals(s.Type, type, StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(s.Kind, "CBufferMember", StringComparison.OrdinalIgnoreCase)
             || string.Equals(s.Kind, "StructMember", StringComparison.OrdinalIgnoreCase)
             || string.Equals(s.Kind, "GlobalVariable", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        return null;
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
        return child?.NodeId;
    }

    private static bool ShouldLoad(string? symbolKind)
    {
        if (symbolKind is null) return false;
        return symbolKind.Equals("GlobalVariable", StringComparison.OrdinalIgnoreCase)
               || symbolKind.Equals("CBuffer", StringComparison.OrdinalIgnoreCase)
               || symbolKind.Equals("Buffer", StringComparison.OrdinalIgnoreCase)
               || symbolKind.Equals("StructMember", StringComparison.OrdinalIgnoreCase);
    }

    private static int EmitLoad(SymbolInfo symbol, int nodeId, Dictionary<int, string> typeByNode, List<IrValue> values, Dictionary<int, IrValue> valueBySymbol, HashSet<int> usedIds, List<IrInstruction> instructions, string? tag = null)
    {
        var baseVal = EnsureValue(symbol, values, valueBySymbol, defaultKind: symbol.Kind ?? "Resource");
        var resultId = AllocateId(null, usedIds);
        var resultType = typeByNode.TryGetValue(nodeId, out var t) ? t : symbol.Type ?? "unknown";
        values.Add(new IrValue
        {
            Id = resultId,
            Kind = "Temp",
            Type = resultType
        });

        instructions.Add(new IrInstruction
        {
            Op = "Load",
            Result = resultId,
            Operands = baseVal is null ? Array.Empty<int>() : new[] { baseVal.Id },
            Type = resultType,
            Tag = tag
        });

        return resultId;
    }

    private static IrBlock[] LowerFunctionBody(SyntaxNodeInfo? functionNode, SymbolInfo entrySymbol, SemanticOutput semantic, List<IrValue> values, Dictionary<int, IrValue> valueBySymbol, Dictionary<int, string> typeByNode, HashSet<int> usedIds, int? firstParameterId, List<IrDiagnostic> diagnostics)
    {
        var nodeById = BuildSyntaxLookup(semantic.Syntax);
        var blocks = new List<IrBlock>();
        var currentInstructions = new List<IrInstruction>();
        var currentLabel = "entry";
        var blockCounter = 0;
        string NewLabel(string prefix) => $"{prefix}{++blockCounter}";

        void FinishBlock()
        {
            blocks.Add(new IrBlock
            {
                Id = currentLabel,
                Instructions = currentInstructions.ToArray()
            });
        }

        void StartBlock(string label)
        {
            currentLabel = label;
            currentInstructions = new List<IrInstruction>();
        }

        var bodyNodeId = GetChildNodeId(functionNode ?? new SyntaxNodeInfo { Children = Array.Empty<SyntaxNodeChild>() }, "body");
        var bodyNode = bodyNodeId is int bId && nodeById.TryGetValue(bId, out var bn) ? bn : null;
        var statements = bodyNode?.Children.Where(c => string.Equals(c.Role, "statement", StringComparison.OrdinalIgnoreCase)).ToList() ?? new List<SyntaxNodeChild>();

        foreach (var stmt in statements)
        {
            if (stmt.NodeId is not int stmtId || !nodeById.TryGetValue(stmtId, out var stmtNode))
            {
                continue;
            }

            if (string.Equals(stmtNode.Kind, "ReturnStatement", StringComparison.OrdinalIgnoreCase))
            {
                var exprId = GetChildNodeId(stmtNode, "expression");
                int? valueId = null;
                if (exprId is int eId)
                {
                    valueId = LowerExpression(eId, nodeById, typeByNode, semantic.Symbols, values, valueBySymbol, usedIds, currentInstructions, diagnostics);
                }

                var returnType = ParseReturnType(entrySymbol.Type);
                if (!IsVoid(returnType) && valueId is null)
                {
                    var undefId = AllocateId(null, usedIds);
                    values.Add(new IrValue { Id = undefId, Kind = "Undef", Type = returnType, Name = "undef_return" });
                    valueId = undefId;
                }

                currentInstructions.Add(new IrInstruction
                {
                    Op = "Return",
                    Operands = valueId is null ? Array.Empty<int>() : new[] { valueId.Value },
                    Type = ParseReturnType(entrySymbol.Type),
                    Terminator = true
                });

                FinishBlock();
                return blocks.ToArray();
            }

            if (string.Equals(stmtNode.Kind, "IfStatement", StringComparison.OrdinalIgnoreCase))
            {
                var condId = GetChildNodeId(stmtNode, "condition");
                var thenId = GetChildNodeId(stmtNode, "then");
                var elseId = GetChildNodeId(stmtNode, "else");

                var condValue = condId is int cId
                    ? LowerExpression(cId, nodeById, typeByNode, semantic.Symbols, values, valueBySymbol, usedIds, currentInstructions, diagnostics)
                    : null;

                var thenLabel = NewLabel("then");
                var elseLabel = elseId is not null ? NewLabel("else") : null;
                var mergeLabel = NewLabel("merge");

                currentInstructions.Add(new IrInstruction
                {
                    Op = "BranchCond",
                    Operands = condValue is null ? Array.Empty<int>() : new[] { condValue.Value },
                    Terminator = true,
                    Tag = elseLabel is null ? $"then:{thenLabel}" : $"then:{thenLabel};else:{elseLabel}"
                });
                FinishBlock();

                StartBlock(thenLabel);
                if (thenId is int tId)
                {
                    LowerEmbeddedStatement(tId, nodeById, typeByNode, semantic.Symbols, values, valueBySymbol, usedIds, currentInstructions, diagnostics, entrySymbol);
                }
                if (!currentInstructions.Any(i => i.Terminator))
                {
                    currentInstructions.Add(new IrInstruction { Op = "Branch", Terminator = true, Tag = mergeLabel });
                }
                FinishBlock();

                if (elseLabel is not null)
                {
                    StartBlock(elseLabel);
                    if (elseId is int eStmtId)
                    {
                        LowerEmbeddedStatement(eStmtId, nodeById, typeByNode, semantic.Symbols, values, valueBySymbol, usedIds, currentInstructions, diagnostics, entrySymbol);
                    }
                    if (!currentInstructions.Any(i => i.Terminator))
                    {
                        currentInstructions.Add(new IrInstruction { Op = "Branch", Terminator = true, Tag = mergeLabel });
                    }
                    FinishBlock();
                }

                StartBlock(mergeLabel);
                continue;
            }

            if (string.Equals(stmtNode.Kind, "WhileStatement", StringComparison.OrdinalIgnoreCase))
            {
                var condLabel = NewLabel("while.cond");
                var bodyLabel = NewLabel("while.body");
                var exitLabel = NewLabel("while.exit");

                currentInstructions.Add(new IrInstruction { Op = "Branch", Terminator = true, Tag = condLabel });
                FinishBlock();

                StartBlock(condLabel);
                var condId = GetChildNodeId(stmtNode, "condition");
                var condValue = condId is int cId
                    ? LowerExpression(cId, nodeById, typeByNode, semantic.Symbols, values, valueBySymbol, usedIds, currentInstructions, diagnostics)
                    : null;
                currentInstructions.Add(new IrInstruction
                {
                    Op = "BranchCond",
                    Operands = condValue is null ? Array.Empty<int>() : new[] { condValue.Value },
                    Terminator = true,
                    Tag = $"then:{bodyLabel};else:{exitLabel}"
                });
                FinishBlock();

                StartBlock(bodyLabel);
                var bodyId = GetChildNodeId(stmtNode, "body");
                if (bodyId is int bStmtId)
                {
                    LowerEmbeddedStatement(bStmtId, nodeById, typeByNode, semantic.Symbols, values, valueBySymbol, usedIds, currentInstructions, diagnostics, entrySymbol);
                }
                if (!currentInstructions.Any(i => i.Terminator))
                {
                    currentInstructions.Add(new IrInstruction { Op = "Branch", Terminator = true, Tag = condLabel });
                }
                FinishBlock();

                StartBlock(exitLabel);
                continue;
            }

            if (string.Equals(stmtNode.Kind, "DoWhileStatement", StringComparison.OrdinalIgnoreCase))
            {
                var bodyLabel = NewLabel("do.body");
                var condLabel = NewLabel("do.cond");
                var exitLabel = NewLabel("do.exit");

                currentInstructions.Add(new IrInstruction { Op = "Branch", Terminator = true, Tag = bodyLabel });
                FinishBlock();

                StartBlock(bodyLabel);
                var bodyId = GetChildNodeId(stmtNode, "body");
                if (bodyId is int bStmtId)
                {
                    LowerEmbeddedStatement(bStmtId, nodeById, typeByNode, semantic.Symbols, values, valueBySymbol, usedIds, currentInstructions, diagnostics, entrySymbol);
                }
                if (!currentInstructions.Any(i => i.Terminator))
                {
                    currentInstructions.Add(new IrInstruction { Op = "Branch", Terminator = true, Tag = condLabel });
                }
                FinishBlock();

                StartBlock(condLabel);
                var condId = GetChildNodeId(stmtNode, "condition");
                var condValue = condId is int cId
                    ? LowerExpression(cId, nodeById, typeByNode, semantic.Symbols, values, valueBySymbol, usedIds, currentInstructions, diagnostics)
                    : null;
                currentInstructions.Add(new IrInstruction
                {
                    Op = "BranchCond",
                    Operands = condValue is null ? Array.Empty<int>() : new[] { condValue.Value },
                    Terminator = true,
                    Tag = $"then:{bodyLabel};else:{exitLabel}"
                });
                FinishBlock();

                StartBlock(exitLabel);
                continue;
            }

            if (string.Equals(stmtNode.Kind, "ForStatement", StringComparison.OrdinalIgnoreCase))
            {
                var initId = GetChildNodeId(stmtNode, "initializer");
                if (initId is int initStmtId)
                {
                    LowerEmbeddedStatement(initStmtId, nodeById, typeByNode, semantic.Symbols, values, valueBySymbol, usedIds, currentInstructions, diagnostics, entrySymbol);
                }

                var condLabel = NewLabel("for.cond");
                var bodyLabel = NewLabel("for.body");
                var incrLabel = NewLabel("for.incr");
                var exitLabel = NewLabel("for.exit");

                currentInstructions.Add(new IrInstruction { Op = "Branch", Terminator = true, Tag = condLabel });
                FinishBlock();

                StartBlock(condLabel);
                var condId = GetChildNodeId(stmtNode, "condition");
                var condValue = condId is int cId
                    ? LowerExpression(cId, nodeById, typeByNode, semantic.Symbols, values, valueBySymbol, usedIds, currentInstructions, diagnostics)
                    : null;
                currentInstructions.Add(new IrInstruction
                {
                    Op = "BranchCond",
                    Operands = condValue is null ? Array.Empty<int>() : new[] { condValue.Value },
                    Terminator = true,
                    Tag = $"then:{bodyLabel};else:{exitLabel}"
                });
                FinishBlock();

                StartBlock(bodyLabel);
                var bodyId = GetChildNodeId(stmtNode, "body");
                if (bodyId is int bodyStmtId)
                {
                    LowerEmbeddedStatement(bodyStmtId, nodeById, typeByNode, semantic.Symbols, values, valueBySymbol, usedIds, currentInstructions, diagnostics, entrySymbol);
                }
                if (!currentInstructions.Any(i => i.Terminator))
                {
                    currentInstructions.Add(new IrInstruction { Op = "Branch", Terminator = true, Tag = incrLabel });
                }
                FinishBlock();

                StartBlock(incrLabel);
                var incrId = GetChildNodeId(stmtNode, "increment");
                if (incrId is int incStmtId)
                {
                    LowerEmbeddedStatement(incStmtId, nodeById, typeByNode, semantic.Symbols, values, valueBySymbol, usedIds, currentInstructions, diagnostics, entrySymbol);
                }
                if (!currentInstructions.Any(i => i.Terminator))
                {
                    currentInstructions.Add(new IrInstruction { Op = "Branch", Terminator = true, Tag = condLabel });
                }
                FinishBlock();

                StartBlock(exitLabel);
                continue;
            }
        }

        if (!currentInstructions.Any(i => i.Terminator))
        {
            var returnType = ParseReturnType(entrySymbol.Type);
            int? returnValue = null;
            if (!IsVoid(returnType))
            {
                var existing = values.FirstOrDefault(v => v.Kind == "Parameter");
                if (existing is not null)
                {
                    returnValue = existing.Id;
                }
                else
                {
                    var undefId = AllocateId(null, usedIds);
                    values.Add(new IrValue { Id = undefId, Kind = "Undef", Type = returnType, Name = "undef_return" });
                    returnValue = undefId;
                }
            }

            currentInstructions.Add(new IrInstruction
            {
                Op = "Return",
                Operands = returnValue is null ? Array.Empty<int>() : new[] { returnValue.Value },
                Type = returnType,
                Terminator = true
            });
        }

        FinishBlock();

        return blocks.ToArray();
    }

    private static void LowerEmbeddedStatement(int stmtId, Dictionary<int, SyntaxNodeInfo> nodeById, Dictionary<int, string> typeByNode, IReadOnlyList<SymbolInfo> symbols, List<IrValue> values, Dictionary<int, IrValue> valueBySymbol, HashSet<int> usedIds, List<IrInstruction> instructions, List<IrDiagnostic> diagnostics, SymbolInfo entrySymbol)
    {
        if (!nodeById.TryGetValue(stmtId, out var node))
        {
            return;
        }

        if (string.Equals(node.Kind, "ReturnStatement", StringComparison.OrdinalIgnoreCase))
        {
            var exprId = GetChildNodeId(node, "expression");
            int? valueId = null;
            if (exprId is int eId)
            {
                valueId = LowerExpression(eId, nodeById, typeByNode, symbols, values, valueBySymbol, usedIds, instructions, diagnostics);
            }

            var returnType = ParseReturnType(entrySymbol.Type);
            if (!IsVoid(returnType) && valueId is null)
            {
                var undefId = AllocateId(null, usedIds);
                values.Add(new IrValue { Id = undefId, Kind = "Undef", Type = returnType, Name = "undef_return" });
                valueId = undefId;
            }

            instructions.Add(new IrInstruction
            {
                Op = "Return",
                Operands = valueId is null ? Array.Empty<int>() : new[] { valueId.Value },
                Type = returnType,
                Terminator = true
            });
            return;
        }

        if (string.Equals(node.Kind, "ExpressionStatement", StringComparison.OrdinalIgnoreCase))
        {
            var exprId = GetChildNodeId(node, "expression");
            if (exprId is int eId)
            {
                LowerExpression(eId, nodeById, typeByNode, symbols, values, valueBySymbol, usedIds, instructions, diagnostics);
            }
        }
    }
}
