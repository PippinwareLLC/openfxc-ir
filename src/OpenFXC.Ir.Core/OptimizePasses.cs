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

            if (instr.Operands.Count == 2 && TryGetConst(values, instr.Operands[0], out var a) && TryGetConst(values, instr.Operands[1], out var b))
            {
                if (TryFoldBinary(instr.Op, a, b, out var folded))
                {
                    var constId = allocate();
                    values.Add(new IrValue { Id = constId, Kind = "Constant", Type = instr.Type ?? "unknown", Name = folded });
                    return instr with { Op = "Assign", Operands = new[] { constId } };
                }
            }

            if (instr.Operands.Count == 1 && TryGetConst(values, instr.Operands[0], out var unary) && TryFoldUnary(instr.Op, unary, out var uf))
            {
                var constId = allocate();
                values.Add(new IrValue { Id = constId, Kind = "Constant", Type = instr.Type ?? "unknown", Name = uf });
                return instr with { Op = "Assign", Operands = new[] { constId } };
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
                if (IsConst(values, rhs, "0") && (instr.Op is "Add" or "Sub"))
                {
                    return instr with { Op = "Assign", Operands = new[] { lhs } };
                }

                if (IsConst(values, rhs, "1") && (instr.Op is "Mul" or "Div"))
                {
                    return instr with { Op = "Assign", Operands = new[] { lhs } };
                }

                if (IsConst(values, rhs, "0") && instr.Op is "Mul")
                {
                    var constId = allocate();
                    values.Add(new IrValue { Id = constId, Kind = "Constant", Type = instr.Type ?? "unknown", Name = "0" });
                    return instr with { Op = "Assign", Operands = new[] { constId } };
                }
            }

            return instr;
        });

        diagnostics.Add(IrDiagnostic.Info("algebraic executed", "optimize"));
        return newModule;
    }

    public static IrModule CopyPropagate(IrModule module, List<IrDiagnostic> diagnostics)
    {
        var (newModule, _) = RewriteFunctions(module, (func, values, instr, allocate) =>
        {
            var map = new Dictionary<int, int>();
            // build map of prior assigns in function scope
            foreach (var block in func.Blocks)
            {
                foreach (var inst in block.Instructions)
                {
                    if (!inst.Terminator && inst.Op == "Assign" && inst.Result is int res && inst.Operands.Count == 1)
                    {
                        map[res] = FindRoot(map, inst.Operands[0]);
                    }
                }
            }

            var newOperands = instr.Operands.Select(o => FindRoot(map, o)).ToArray();
            return instr with { Operands = newOperands };
        });

        diagnostics.Add(IrDiagnostic.Info("copyprop executed", "optimize"));
        return newModule;
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

    public static IrModule ComponentDce(IrModule module, List<IrDiagnostic> diagnostics)
    {
        diagnostics.Add(IrDiagnostic.Info("component-dce executed (no-op placeholder)", "optimize"));
        return module;
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

    private static bool TryFoldBinary(string op, double a, double b, out string result)
    {
        result = string.Empty;
        switch (op)
        {
            case "Add":
                result = (a + b).ToString();
                return true;
            case "Sub":
                result = (a - b).ToString();
                return true;
            case "Mul":
                result = (a * b).ToString();
                return true;
            case "Div":
                if (b == 0) return false;
                result = (a / b).ToString();
                return true;
            default:
                return false;
        }
    }

    private static bool TryFoldUnary(string op, double a, out string result)
    {
        result = string.Empty;
        switch (op)
        {
            case "Negate":
                result = (-a).ToString();
                return true;
            default:
                return false;
        }
    }

    private static bool TryGetConst(IReadOnlyList<IrValue> values, int valueId, out double number)
    {
        var v = values.FirstOrDefault(x => x.Id == valueId);
        if (v is null || !string.Equals(v.Kind, "Constant", StringComparison.OrdinalIgnoreCase))
        {
            number = 0;
            return false;
        }

        return double.TryParse(v.Name, out number);
    }

    private static bool IsConst(IReadOnlyList<IrValue> values, int valueId, string literal)
    {
        var v = values.FirstOrDefault(x => x.Id == valueId);
        return v is not null
               && string.Equals(v.Kind, "Constant", StringComparison.OrdinalIgnoreCase)
               && string.Equals(v.Name, literal, StringComparison.OrdinalIgnoreCase);
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
        return op is "Add" or "Sub" or "Mul" or "Div" or "Mod" or "Eq" or "Ne" or "Lt" or "Le" or "Gt" or "Ge" or "LogicalAnd" or "LogicalOr" or "Swizzle" or "Cast" or "Assign";
    }

    private static int FindRoot(Dictionary<int, int> map, int id)
    {
        while (map.TryGetValue(id, out var next) && next != id)
        {
            id = next;
        }
        return id;
    }
}
