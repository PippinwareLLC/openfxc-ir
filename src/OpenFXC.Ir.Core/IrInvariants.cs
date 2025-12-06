namespace OpenFXC.Ir;

public static class IrInvariants
{
    public static IReadOnlyList<IrDiagnostic> Validate(IrModule module)
    {
        if (module is null) throw new ArgumentNullException(nameof(module));

        var diagnostics = new List<IrDiagnostic>();

        if (module.FormatVersion != 1)
        {
            diagnostics.Add(IrDiagnostic.Error($"Unexpected formatVersion: {module.FormatVersion}"));
        }

        var valueIds = new HashSet<int>();
        foreach (var value in module.Values)
        {
            if (!valueIds.Add(value.Id))
            {
                diagnostics.Add(IrDiagnostic.Error($"Duplicate value id: {value.Id}"));
            }

            if (string.IsNullOrWhiteSpace(value.Type))
            {
                diagnostics.Add(IrDiagnostic.Error($"Value {value.Id} missing type."));
            }
        }

        var knownValues = new HashSet<int>(module.Values.Select(v => v.Id));

        foreach (var function in module.Functions)
        {
            if (string.IsNullOrWhiteSpace(function.Name))
            {
                diagnostics.Add(IrDiagnostic.Error("Function missing name."));
            }

            if (string.IsNullOrWhiteSpace(function.ReturnType))
            {
                diagnostics.Add(IrDiagnostic.Error($"Function {function.Name} missing returnType."));
            }

            foreach (var parameter in function.Parameters)
            {
                if (!knownValues.Contains(parameter))
                {
                    diagnostics.Add(IrDiagnostic.Error($"Function {function.Name} references unknown parameter value id {parameter}."));
                }
            }

            foreach (var block in function.Blocks)
            {
                if (block.Instructions.Count == 0)
                {
                    diagnostics.Add(IrDiagnostic.Error($"Block {block.Id} has no instructions/terminator."));
                    continue;
                }

                if (!block.Instructions.Last().Terminator)
                {
                    diagnostics.Add(IrDiagnostic.Error($"Block {block.Id} is not terminated."));
                }

                var terminated = false;
                foreach (var instr in block.Instructions)
                {
                    if (terminated)
                    {
                        diagnostics.Add(IrDiagnostic.Error($"Block {block.Id} has instructions after terminator."));
                        break;
                    }

                    if (instr.Result is { } res && !knownValues.Contains(res))
                    {
                        diagnostics.Add(IrDiagnostic.Error($"Instruction result references unknown value id {res}."));
                    }

                    foreach (var operand in instr.Operands)
                    {
                        if (!knownValues.Contains(operand))
                        {
                            diagnostics.Add(IrDiagnostic.Error($"Instruction operand references unknown value id {operand}."));
                        }
                    }

                    terminated = instr.Terminator;
                }
            }
        }

        return diagnostics;
    }
}
