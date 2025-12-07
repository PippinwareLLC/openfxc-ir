using System.Globalization;

namespace OpenFXC.Ir;

internal static class OptimizePasses
{
    public static IrModule ConstantFold(IrModule module, List<IrDiagnostic> diagnostics)
    {
        var (newModule, _) = RewriteFunctions(module, (func, values, instr, allocate) =>
        {
            if (instr.Terminator || instr.Operands.Count == 0 || instr.Result is null)
            {
                return instr;
            }

            if (instr.Operands.Count == 2
                && TryGetConst(values, instr.Operands[0], instr.Type, out var a)
                && TryGetConst(values, instr.Operands[1], instr.Type, out var b))
            {
                if (TryFoldBinary(instr.Op, a, b, instr.Type ?? "unknown", out var folded))
                {
                    var constId = allocate();
                    values.Add(new IrValue { Id = constId, Kind = "Constant", Type = folded.Type, Name = folded.Name });
                    return instr with { Op = "Assign", Operands = new[] { constId }, Type = folded.Type };
                }
            }

            if (instr.Operands.Count == 1 && TryGetConst(values, instr.Operands[0], instr.Type, out var unary) && TryFoldUnary(instr.Op, unary, out var uf))
            {
                var constId = allocate();
                values.Add(new IrValue { Id = constId, Kind = "Constant", Type = uf.Type, Name = uf.Name });
                return instr with { Op = "Assign", Operands = new[] { constId }, Type = uf.Type };
            }

            return instr;
        });

        diagnostics.Add(IrDiagnostic.Info("constfold executed", "optimize"));
        return newModule;
    }

    public static IrModule AlgebraicSimplify(IrModule module, List<IrDiagnostic> diagnostics)
    {
        var (newModule, _) = RewriteFunctions(module, (func, values, instr, allocate) =>
        {
            if (instr.Terminator || instr.Result is null)
            {
                return instr;
            }

            if (instr.Operands.Count == 2)
            {
                var lhs = instr.Operands[0];
                var rhs = instr.Operands[1];
                if (TryGetConst(values, rhs, instr.Type, out var rhsConst))
                {
                    if (rhsConst.IsZero && (instr.Op is "Add" or "Sub"))
                    {
                        return instr with { Op = "Assign", Operands = new[] { lhs }, Type = instr.Type };
                    }

                    if (rhsConst.IsOne && (instr.Op is "Mul" or "Div"))
                    {
                        return instr with { Op = "Assign", Operands = new[] { lhs }, Type = instr.Type };
                    }

                    if (rhsConst.IsZero && instr.Op is "Mul")
                    {
                        var constId = allocate();
                        values.Add(new IrValue { Id = constId, Kind = "Constant", Type = rhsConst.Type, Name = rhsConst.Name });
                        return instr with { Op = "Assign", Operands = new[] { constId }, Type = rhsConst.Type };
                    }
                }
            }

            return instr;
        });

        diagnostics.Add(IrDiagnostic.Info("algebraic executed", "optimize"));
        return newModule;
    }

    public static IrModule CopyPropagate(IrModule module, List<IrDiagnostic> diagnostics)
    {
        var newFunctions = new List<IrFunction>();
        foreach (var func in module.Functions)
        {
            var blockLookup = func.Blocks.ToDictionary(b => b.Id);
            var predecessors = BuildPredecessors(func.Blocks);

            var inMap = func.Blocks.ToDictionary(b => b.Id, _ => new Dictionary<int, int>());
            var outMap = func.Blocks.ToDictionary(b => b.Id, _ => new Dictionary<int, int>());

            var worklist = new Queue<IrBlock>(func.Blocks);
            while (worklist.Count > 0)
            {
                var block = worklist.Dequeue();
                var incoming = MergePredecessors(block, predecessors, outMap);
                var outgoing = ApplyBlockTransfers(block, incoming);

                if (!MapsEqual(inMap[block.Id], incoming) || !MapsEqual(outMap[block.Id], outgoing))
                {
                    inMap[block.Id] = incoming;
                    outMap[block.Id] = outgoing;

                    foreach (var succId in GetSuccessors(block))
                    {
                        if (blockLookup.TryGetValue(succId, out var succ))
                        {
                            worklist.Enqueue(succ);
                        }
                    }
                }
            }

            var rewrittenBlocks = new List<IrBlock>();
            foreach (var block in func.Blocks)
            {
                var current = new Dictionary<int, int>(inMap[block.Id]);
                var rewritten = new List<IrInstruction>();

                foreach (var instr in block.Instructions)
                {
                    var operands = instr.Operands.Select(o => FindRoot(current, o)).ToArray();

                    if (!instr.Terminator && instr.Op == "Assign" && instr.Result is int res && instr.Operands.Count == 1)
                    {
                        current[res] = FindRoot(current, instr.Operands[0]);
                    }
                    else if (instr.Result is int resId)
                    {
                        current.Remove(resId);
                    }

                    rewritten.Add(instr with { Operands = operands });
                }

                rewrittenBlocks.Add(block with { Instructions = rewritten.ToArray() });
            }

            newFunctions.Add(func with { Blocks = rewrittenBlocks.ToArray() });
        }

        diagnostics.Add(IrDiagnostic.Info("copyprop executed", "optimize"));
        return module with { Functions = newFunctions.ToArray() };
    }

    public static IrModule DeadCodeEliminate(IrModule module, List<IrDiagnostic> diagnostics)
    {
        var newFunctions = new List<IrFunction>();
        foreach (var func in module.Functions)
        {
            var usedCounts = BuildUseCounts(func.Blocks);
            var blocks = new List<IrBlock>();
            foreach (var block in func.Blocks)
            {
                var kept = new List<IrInstruction>();
                foreach (var instr in block.Instructions)
                {
                    if (!instr.Terminator && instr.Result is int res && IsPure(instr.Op))
                    {
                        if (!usedCounts.TryGetValue(res, out var count) || count == 0)
                        {
                            // decrement operand uses
                            foreach (var op in instr.Operands)
                            {
                                if (usedCounts.ContainsKey(op) && usedCounts[op] > 0)
                                {
                                    usedCounts[op]--;
                                }
                            }
                            continue;
                        }
                    }

                    kept.Add(instr);
                }

                if (kept.Count > 0)
                {
                    blocks.Add(block with { Instructions = kept.ToArray() });
                }
            }

            newFunctions.Add(func with { Blocks = blocks.ToArray() });
        }

        diagnostics.Add(IrDiagnostic.Info("dce executed", "optimize"));
        return module with { Functions = newFunctions.ToArray() };
    }

    public static IrModule CommonSubexpressionEliminate(IrModule module, List<IrDiagnostic> diagnostics)
    {
        var newFunctions = new List<IrFunction>();
        foreach (var func in module.Functions)
        {
            var newBlocks = new List<IrBlock>();
            foreach (var block in func.Blocks)
            {
                var expressions = new Dictionary<(string Op, string? Type, string? Tag, string Key), int>();
                var rewritten = new List<IrInstruction>();
                foreach (var instr in block.Instructions)
                {
                    if (instr.Terminator)
                    {
                        rewritten.Add(instr);
                        continue;
                    }

                    if (HasSideEffects(instr))
                    {
                        expressions.Clear();
                        rewritten.Add(instr);
                        continue;
                    }

                    if (instr.Result is int resultId && instr.Operands.Count > 0 && IsPure(instr.Op))
                    {
                        var key = (instr.Op, instr.Type, instr.Tag, string.Join(',', instr.Operands));
                        if (expressions.TryGetValue(key, out var existing))
                        {
                            rewritten.Add(instr with { Op = "Assign", Operands = new[] { existing } });
                            continue;
                        }

                        expressions[key] = resultId;
                    }

                    rewritten.Add(instr);
                }

                newBlocks.Add(block with { Instructions = rewritten.ToArray() });
            }

            newFunctions.Add(func with { Blocks = newBlocks.ToArray() });
        }

        diagnostics.Add(IrDiagnostic.Info("cse executed", "optimize"));
        return module with { Functions = newFunctions.ToArray() };
    }

    public static IrModule ComponentDce(IrModule module, List<IrDiagnostic> diagnostics)
    {
        var values = module.Values.ToDictionary(v => v.Id, v => v with { });
        var updatedFunctions = new List<IrFunction>();

        foreach (var func in module.Functions)
        {
            var liveMask = new Dictionary<int, int>();
            int GetComponentCount(int valueId)
            {
                return values.TryGetValue(valueId, out var val) ? ParseComponentCount(val.Type) : 1;
            }

            void AddMask(int valueId, int mask)
            {
                if (mask == 0) return;
                if (liveMask.TryGetValue(valueId, out var existing))
                {
                    liveMask[valueId] = existing | mask;
                }
                else
                {
                    liveMask[valueId] = mask;
                }
            }

            // Seed liveness from terminators (returns/branches use their operands).
            foreach (var block in func.Blocks)
            {
                foreach (var instr in block.Instructions.Where(i => i.Terminator))
                {
                    foreach (var op in instr.Operands)
                    {
                        AddMask(op, FullMask(GetComponentCount(op)));
                    }
                }
            }

            var newBlocks = new List<IrBlock>();
            foreach (var block in func.Blocks)
            {
                var newInstr = new List<IrInstruction>();
                for (var i = block.Instructions.Count - 1; i >= 0; i--)
                {
                    var instr = block.Instructions[i];
                    var resultMask = instr.Result is int rid && liveMask.TryGetValue(rid, out var mask)
                        ? mask
                        : 0;

                    if (!instr.Terminator && instr.Result is int deadResult && resultMask == 0 && IsPure(instr.Op))
                    {
                        // Instruction result is unused; skip it.
                        continue;
                    }

                    if (string.Equals(instr.Op, "Swizzle", StringComparison.OrdinalIgnoreCase) && instr.Operands.Count == 1)
                    {
                        var sourceId = instr.Operands[0];
                        var originalTag = instr.Tag ?? string.Empty;
                        var sourceMask = ComputeSwizzleRequirement(originalTag, resultMask, GetComponentCount(sourceId));

                        if (instr.Result is int swizzleResult && resultMask != 0)
                        {
                            var destCount = string.IsNullOrEmpty(originalTag)
                                ? ParseComponentCount(values[swizzleResult].Type)
                                : originalTag.Length;
                            var destMask = FullMask(destCount);
                            var trimmedTag = resultMask == destMask ? originalTag : TrimSwizzle(originalTag, resultMask);
                            var baseType = InferScalarType(values[swizzleResult].Type);
                            var newType = ComposeType(baseType, string.IsNullOrEmpty(trimmedTag) ? destCount : trimmedTag.Length);
                            values[swizzleResult] = values[swizzleResult] with { Type = newType };
                            instr = instr with { Tag = string.IsNullOrEmpty(trimmedTag) ? instr.Tag : trimmedTag, Type = newType };
                        }

                        AddMask(sourceId, sourceMask);
                    }
                    else
                    {
                        foreach (var op in instr.Operands)
                        {
                            AddMask(op, FullMask(GetComponentCount(op)));
                        }
                    }

                    newInstr.Add(instr);
                }

                newInstr.Reverse();
                newBlocks.Add(block with { Instructions = newInstr.ToArray() });
            }

            updatedFunctions.Add(func with { Blocks = newBlocks.ToArray() });
        }

        diagnostics.Add(IrDiagnostic.Info("component-dce executed", "optimize"));
        return module with { Values = values.Values.ToArray(), Functions = updatedFunctions.ToArray() };
    }

    private static (IrModule module, int nextId) RewriteFunctions(
        IrModule module,
        Func<IrFunction, List<IrValue>, IrInstruction, Func<int> , IrInstruction> transform)
    {
        var values = module.Values.ToList();
        var nextId = values.Any() ? values.Max(v => v.Id) + 1 : 1;
        int Allocate() => nextId++;

        var newFunctions = new List<IrFunction>();
        foreach (var func in module.Functions)
        {
            var newBlocks = new List<IrBlock>();
            foreach (var block in func.Blocks)
            {
                var newInstr = block.Instructions.Select(instr => transform(func, values, instr, Allocate)).ToArray();
                newBlocks.Add(block with { Instructions = newInstr });
            }
            newFunctions.Add(func with { Blocks = newBlocks.ToArray() });
        }

        return (module with { Functions = newFunctions.ToArray(), Values = values.ToArray() }, nextId);
    }

    private static Dictionary<string, HashSet<string>> BuildPredecessors(IReadOnlyList<IrBlock> blocks)
    {
        var predecessors = blocks.ToDictionary(b => b.Id, _ => new HashSet<string>());

        foreach (var block in blocks)
        {
            foreach (var target in GetSuccessors(block))
            {
                if (predecessors.TryGetValue(target, out var preds))
                {
                    preds.Add(block.Id);
                }
            }
        }

        return predecessors;
    }

    private static IEnumerable<string> GetSuccessors(IrBlock block)
    {
        var terminator = block.Instructions.LastOrDefault(i => i.Terminator);
        if (terminator is null)
        {
            yield break;
        }

        if (terminator.Op.Equals("Branch", StringComparison.OrdinalIgnoreCase)
            || terminator.Op.Equals("BranchCond", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var target in GetBranchTargets(terminator.Tag ?? string.Empty))
            {
                yield return target;
            }
        }
    }

    private static Dictionary<int, int> MergePredecessors(
        IrBlock block,
        IReadOnlyDictionary<string, HashSet<string>> predecessors,
        IReadOnlyDictionary<string, Dictionary<int, int>> outMap)
    {
        if (!predecessors.TryGetValue(block.Id, out var preds) || preds.Count == 0)
        {
            return new Dictionary<int, int>();
        }

        var merged = new Dictionary<int, int>(outMap[preds.First()]);
        foreach (var pred in preds.Skip(1))
        {
            var current = outMap[pred];
            foreach (var kvp in merged.ToArray())
            {
                if (!current.TryGetValue(kvp.Key, out var value) || value != kvp.Value)
                {
                    merged.Remove(kvp.Key);
                }
            }
        }

        return merged;
    }

    private static Dictionary<int, int> ApplyBlockTransfers(IrBlock block, IReadOnlyDictionary<int, int> incoming)
    {
        var state = new Dictionary<int, int>(incoming);
        foreach (var instr in block.Instructions)
        {
            if (!instr.Terminator && instr.Op == "Assign" && instr.Result is int res && instr.Operands.Count == 1)
            {
                state[res] = FindRoot(state, instr.Operands[0]);
            }
            else if (instr.Result is int resId)
            {
                state.Remove(resId);
            }
        }

        return state;
    }

    private static bool MapsEqual(IReadOnlyDictionary<int, int> lhs, IReadOnlyDictionary<int, int> rhs)
    {
        if (ReferenceEquals(lhs, rhs)) return true;
        if (lhs.Count != rhs.Count) return false;

        foreach (var kvp in lhs)
        {
            if (!rhs.TryGetValue(kvp.Key, out var value) || kvp.Value != value)
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<string> GetBranchTargets(string tag)
    {
        foreach (var segment in tag.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = segment.Split(':', 2, StringSplitOptions.TrimEntries);
            yield return parts.Length == 2 ? parts[1] : parts[0];
        }
    }

    private static bool TryFoldBinary(string op, ConstantValue a, ConstantValue b, string requestedType, out ConstantValue result)
    {
        result = default;
        if (!a.ShapeEquals(b))
        {
            return false;
        }

        var elements = new double[a.Elements.Length];
        for (var i = 0; i < elements.Length; i++)
        {
            var left = a.Elements[i];
            var right = b.Elements[i];
            switch (op)
            {
                case "Add":
                    elements[i] = left + right;
                    break;
                case "Sub":
                    elements[i] = left - right;
                    break;
                case "Mul":
                    elements[i] = left * right;
                    break;
                case "Div":
                    if (right == 0)
                    {
                        return false;
                    }

                    elements[i] = left / right;
                    break;
                default:
                    return false;
            }
        }

        result = new ConstantValue(a.Rows, a.Columns, elements, requestedType ?? a.Type);
        return true;
    }

    private static bool TryFoldUnary(string op, ConstantValue a, out ConstantValue result)
    {
        result = default;
        switch (op)
        {
            case "Negate":
                var elements = a.Elements.Select(v => -v).ToArray();
                result = new ConstantValue(a.Rows, a.Columns, elements, a.Type);
                return true;
            default:
                return false;
        }
    }

    private static bool TryGetConst(IReadOnlyList<IrValue> values, int valueId, string? requestedType, out ConstantValue constant)
    {
        var v = values.FirstOrDefault(x => x.Id == valueId);
        if (v is null || !string.Equals(v.Kind, "Constant", StringComparison.OrdinalIgnoreCase))
        {
            constant = default;
            return false;
        }

        return ConstantValue.TryParse(v, requestedType, out constant);
    }

    private static Dictionary<int, int> BuildUseCounts(IReadOnlyList<IrBlock> blocks)
    {
        var counts = new Dictionary<int, int>();
        foreach (var block in blocks)
        {
            foreach (var instr in block.Instructions)
            {
                foreach (var op in instr.Operands)
                {
                    counts.TryGetValue(op, out var count);
                    counts[op] = count + 1;
                }
            }
        }

        return counts;
    }

    private static bool IsPure(string op)
    {
        if (HasSideEffects(op, null))
        {
            return false;
        }

        return op is "Add" or "Sub" or "Mul" or "Div" or "Mod" or "Eq" or "Ne" or "Lt" or "Le" or "Gt" or "Ge" or "LogicalAnd" or "LogicalOr" or "Swizzle" or "Cast" or "Assign" or "Index";
    }

    private static bool HasSideEffects(IrInstruction instr)
    {
        return HasSideEffects(instr.Op, instr.Tag);
    }

    private static bool HasSideEffects(string op, string? tag)
    {
        if (string.IsNullOrWhiteSpace(op))
        {
            return false;
        }

        if (op.Equals("Store", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (op.Contains("Sample", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(tag) && tag.Contains("discard", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private readonly record struct ConstantValue(int Rows, int Columns, double[] Elements, string Type)
    {
        public int Rows { get; } = Rows;

        public int Columns { get; } = Columns;

        public double[] Elements { get; } = Elements;

        public string Type { get; } = Type;

        public bool IsScalar => Rows == 1 && Columns == 1;

        public bool IsZero => Elements.All(e => e == 0);

        public bool IsOne => Elements.All(e => e == 1);

        public bool ShapeEquals(ConstantValue other) => Rows == other.Rows && Columns == other.Columns && Elements.Length == other.Elements.Length;

        public string Name => FormatLiteral(Type, Elements);

        public static bool TryParse(IrValue value, string? requestedType, out ConstantValue constant)
        {
            constant = default;
            if (value is null)
            {
                return false;
            }

            var type = requestedType ?? value.Type ?? "unknown";
            if (!TryParseTypeShape(type, out var rows, out var cols))
            {
                // default to scalar if the type is unknown
                rows = 1;
                cols = 1;
            }

            if (!TryParseElements(value.Name, rows * cols, out var elements))
            {
                return false;
            }

            constant = new ConstantValue(rows, cols, elements, type);
            return true;
        }

        private static bool TryParseTypeShape(string type, out int rows, out int cols)
        {
            rows = 1;
            cols = 1;
            if (string.IsNullOrWhiteSpace(type))
            {
                return false;
            }

            var trimmed = type.Trim().ToLowerInvariant();
            if (trimmed.Length < 5)
            {
                return false;
            }

            // float3 or float4x4
            var numeric = new string(trimmed.SkipWhile(c => !char.IsDigit(c)).ToArray());
            if (string.IsNullOrEmpty(numeric))
            {
                rows = 1;
                cols = 1;
                return true;
            }

            var parts = numeric.Split('x');
            if (int.TryParse(parts[0], out var first))
            {
                if (parts.Length == 1)
                {
                    rows = 1;
                    cols = first;
                    return true;
                }

                if (parts.Length == 2 && int.TryParse(parts[1], out var second))
                {
                    rows = first;
                    cols = second;
                    return true;
                }
            }

            return false;
        }

        private static bool TryParseElements(string literal, int expected, out double[] elements)
        {
            elements = Array.Empty<double>();
            if (string.IsNullOrWhiteSpace(literal))
            {
                return false;
            }

            var trimmed = literal.Trim();
            if (trimmed.EndsWith(")") && trimmed.Contains('('))
            {
                var open = trimmed.IndexOf('(');
                trimmed = trimmed.Substring(open + 1, trimmed.Length - open - 2);
            }

            var parts = trimmed.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1 && expected > 1)
            {
                // splat a scalar across the expected size (e.g., float3(1))
                if (!TryParseDouble(parts[0], out var scalar))
                {
                    return false;
                }

                elements = Enumerable.Repeat(scalar, expected).ToArray();
                return true;
            }

            if (parts.Length != expected)

            {
                return false;
            }

            var parsed = new List<double>();
            foreach (var part in parts)
            {
                if (!TryParseDouble(part, out var val))
                {
                    return false;
                }

                parsed.Add(val);
            }

            elements = parsed.ToArray();
            return true;
        }

        private static bool TryParseDouble(string literal, out double value)
        {
            literal = literal.Trim();
            if (literal.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                value = 1;
                return true;
            }

            if (literal.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                value = 0;
                return true;
            }

            return double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static string FormatLiteral(string type, IReadOnlyList<double> elements)
        {
            var formatted = elements.Select(v => v.ToString(CultureInfo.InvariantCulture));
            if (elements.Count == 1)
            {
                return formatted.First();
            }

            return $"{type}({string.Join(",", formatted)})";
        }
    }

    private static int FindRoot(Dictionary<int, int> map, int id)
    {
        while (map.TryGetValue(id, out var next) && next != id)
        {
            id = next;
        }
        return id;
    }

    private static int ParseComponentCount(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return 1;
        var digits = new string(type.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        if (int.TryParse(digits, out var count) && count > 0)
        {
            return count;
        }

        return 1;
    }

    private static int FullMask(int componentCount)
    {
        if (componentCount <= 0) return 0;
        return (1 << componentCount) - 1;
    }

    private static string InferScalarType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return "float";
        var trimmed = type.Trim();
        var suffix = new string(trimmed.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        return string.IsNullOrEmpty(suffix) ? trimmed : trimmed[..^suffix.Length];
    }

    private static string ComposeType(string baseType, int componentCount)
    {
        if (componentCount <= 1)
        {
            return baseType;
        }

        return $"{baseType}{componentCount}";
    }

    private static int ComputeSwizzleRequirement(string tag, int resultMask, int sourceComponents)
    {
        if (string.IsNullOrEmpty(tag))
        {
            return FullMask(sourceComponents);
        }

        var mapping = new List<int>();
        foreach (var ch in tag)
        {
            mapping.Add(ch switch
            {
                'x' or 'r' or 'u' => 0,
                'y' or 'g' or 'v' => 1,
                'z' or 'b' => 2,
                'w' or 'a' => 3,
                _ => -1
            });
        }

        if (resultMask == 0)
        {
            resultMask = FullMask(mapping.Count == 0 ? sourceComponents : mapping.Count);
        }

        var required = 0;
        for (var i = 0; i < mapping.Count; i++)
        {
            if (((resultMask >> i) & 1) == 1 && mapping[i] >= 0)
            {
                required |= 1 << mapping[i];
            }
        }

        return required == 0 ? FullMask(sourceComponents) : required;
    }

    private static string TrimSwizzle(string tag, int liveMask)
    {
        if (string.IsNullOrEmpty(tag)) return tag;
        var kept = new List<char>();
        for (var i = 0; i < tag.Length; i++)
        {
            if (((liveMask >> i) & 1) == 1)
            {
                kept.Add(tag[i]);
            }
        }

        return kept.Count == 0 ? tag : new string(kept.ToArray());
    }
}
