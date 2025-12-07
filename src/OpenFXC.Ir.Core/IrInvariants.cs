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

        if (HasBackendSpecificTokens(module.Profile) || HasBackendSpecificTokens(module.EntryPoint?.Function) || HasBackendSpecificTokens(module.EntryPoint?.Stage))
        {
            diagnostics.Add(IrDiagnostic.Error("Module metadata carries backend-specific artifacts; IR must remain backend-agnostic."));
        }

        foreach (var resource in module.Resources ?? Array.Empty<IrResource>())
        {
            if (HasBackendSpecificTokens(resource.Kind) || HasBackendSpecificTokens(resource.Type) || HasBackendSpecificTokens(resource.Name))
            {
                diagnostics.Add(IrDiagnostic.Error($"Resource '{resource.Name}' carries backend-specific artifacts; IR must remain backend-agnostic."));
            }
        }

        foreach (var technique in module.Techniques ?? Array.Empty<IrFxTechnique>())
        {
            if (HasBackendSpecificTokens(technique.Name))
            {
                diagnostics.Add(IrDiagnostic.Error($"Technique '{technique.Name}' carries backend-specific artifacts; IR must remain backend-agnostic."));
            }

            foreach (var pass in technique.Passes ?? Array.Empty<IrFxPass>())
            {
                if (HasBackendSpecificTokens(pass.Name))
                {
                    diagnostics.Add(IrDiagnostic.Error($"Technique '{technique.Name}' pass '{pass.Name}' carries backend-specific artifacts; IR must remain backend-agnostic."));
                }

                foreach (var shader in pass.Shaders ?? Array.Empty<IrFxShaderBinding>())
                {
                    if (HasBackendSpecificTokens(shader.Stage) || HasBackendSpecificTokens(shader.Profile) || HasBackendSpecificTokens(shader.Entry))
                    {
                        diagnostics.Add(IrDiagnostic.Error($"Technique '{technique.Name}' pass '{pass.Name}' shader binding carries backend-specific artifacts; IR must remain backend-agnostic."));
                    }
                }

                foreach (var state in pass.States ?? Array.Empty<IrFxStateAssignment>())
                {
                    if (HasBackendSpecificTokens(state.Name) || HasBackendSpecificTokens(state.Value))
                    {
                        diagnostics.Add(IrDiagnostic.Error($"Technique '{technique.Name}' pass '{pass.Name}' state '{state.Name}' carries backend-specific artifacts; IR must remain backend-agnostic."));
                    }
                }
            }
        }

        var valueIds = new HashSet<int>();
        foreach (var value in module.Values ?? Array.Empty<IrValue>())
        {
            if (!valueIds.Add(value.Id))
            {
                diagnostics.Add(IrDiagnostic.Error($"Duplicate value id: {value.Id}"));
            }

            if (string.IsNullOrWhiteSpace(value.Type))
            {
                diagnostics.Add(IrDiagnostic.Error($"Value {value.Id} missing type."));
            }

            if (HasBackendSpecificTokens(value.Type) || HasBackendSpecificTokens(value.Name))
            {
                diagnostics.Add(IrDiagnostic.Error($"Value {value.Id} carries backend-specific artifacts; IR must remain backend-agnostic."));
            }
        }

        var knownValues = new HashSet<int>((module.Values ?? Array.Empty<IrValue>()).Select(v => v.Id));

        var valueMap = new Dictionary<int, IrValue>(EqualityComparer<int>.Default);
        foreach (var value in module.Values ?? Array.Empty<IrValue>())
        {
            if (!valueMap.ContainsKey(value.Id))
            {
                valueMap[value.Id] = value;
            }
        }

        foreach (var function in module.Functions ?? Array.Empty<IrFunction>())
        {
            var definedResults = new HashSet<int>();
            var blockIdCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var block in function.Blocks ?? Array.Empty<IrBlock>())
            {
                var blockId = block.Id ?? string.Empty;
                blockIdCounts[blockId] = blockIdCounts.TryGetValue(blockId, out var count) ? count + 1 : 1;
            }
            var blockIds = new HashSet<string>(blockIdCounts.Keys.Where(k => !string.IsNullOrWhiteSpace(k)), StringComparer.OrdinalIgnoreCase);
            var branchTargetsByBlock = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            var seenBlockIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(function.Name))
            {
                diagnostics.Add(IrDiagnostic.Error("Function missing name."));
            }

            if (string.IsNullOrWhiteSpace(function.ReturnType))
            {
                diagnostics.Add(IrDiagnostic.Error($"Function {function.Name} missing returnType."));
            }

            if (function.Blocks is null || function.Blocks.Count == 0)
            {
                diagnostics.Add(IrDiagnostic.Error($"Function {function.Name} has no blocks.", "invariant"));
                continue;
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
                if (string.IsNullOrWhiteSpace(block.Id))
                {
                    diagnostics.Add(IrDiagnostic.Error($"Function {function.Name} has invalid or duplicate block id '{block.Id}'."));
                }
                else if (!seenBlockIds.Add(block.Id))
                {
                    diagnostics.Add(IrDiagnostic.Error($"Function {function.Name} has invalid or duplicate block id '{block.Id}'."));
                }

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
                    else if (instr.Result is { } defined && !definedResults.Add(defined))
                    {
                        diagnostics.Add(IrDiagnostic.Error($"Value id {defined} defined multiple times in function {function.Name}."));
                    }

                    foreach (var operand in instr.Operands)
                    {
                        if (!knownValues.Contains(operand))
                        {
                            diagnostics.Add(IrDiagnostic.Error($"Instruction operand references unknown value id {operand}."));
                        }
                    }

                    if (instr.Result is int resultId)
                    {
                        if (string.IsNullOrWhiteSpace(instr.Type))
                        {
                            diagnostics.Add(IrDiagnostic.Error($"Instruction producing value {resultId} is missing type."));
                        }
                        else if (valueMap.TryGetValue(resultId, out var value) && !string.Equals(value.Type, instr.Type, StringComparison.OrdinalIgnoreCase))
                        {
                            diagnostics.Add(IrDiagnostic.Error($"Instruction result type '{instr.Type}' does not match value {resultId} type '{value.Type}'."));
                        }
                    }

                    if (string.Equals(instr.Op, "Store", StringComparison.OrdinalIgnoreCase))
                    {
                        if (instr.Operands.Count is not (2 or 3))
                        {
                            diagnostics.Add(IrDiagnostic.Error($"Store requires target/value operands (and optional index); {instr.Operands.Count} provided."));
                        }
                        else if (instr.Operands.Any(o => !knownValues.Contains(o)))
                        {
                            diagnostics.Add(IrDiagnostic.Error("Store references unknown operand value."));
                        }
                        else
                        {
                            var targetType = GetValueType(valueMap, instr.Operands[0]);
                            var valueType = GetValueType(valueMap, instr.Operands[^1]);
                            if (!IsResourceType(targetType) && !IsResourceType(valueType))
                            {
                                var targetScalar = GetScalarType(targetType);
                                var valueScalar = GetScalarType(valueType);
                                if (IsNumericScalar(targetScalar) && IsNumericScalar(valueScalar) && !string.Equals(targetScalar, valueScalar, StringComparison.OrdinalIgnoreCase))
                                {
                                    diagnostics.Add(IrDiagnostic.Error($"Store type mismatch: target type '{targetType}' does not match value type '{valueType}'."));
                                }
                            }
                        }
                    }
                    
                    if (HasBackendSpecificTokens(instr.Op) || HasBackendSpecificTokens(instr.Tag) || HasBackendSpecificTokens(instr.Type))
                    {
                        diagnostics.Add(IrDiagnostic.Error($"Instruction '{instr.Op}' carries backend-specific artifacts; IR must remain backend-agnostic."));
                    }

                    if (instr.Terminator && (string.Equals(instr.Op, "Branch", StringComparison.OrdinalIgnoreCase)
                                              || string.Equals(instr.Op, "BranchCond", StringComparison.OrdinalIgnoreCase)))
                    {
                        if (string.IsNullOrWhiteSpace(instr.Tag))
                        {
                            diagnostics.Add(IrDiagnostic.Error($"Terminator '{instr.Op}' in block {block.Id} is missing target tag."));
                        }
                        else
                        {
                            var targets = GetBranchTargets(instr.Tag).ToArray();
                            var expectedCount = string.Equals(instr.Op, "Branch", StringComparison.OrdinalIgnoreCase) ? 1 : 2;
                            if (targets.Length != expectedCount)
                            {
                                diagnostics.Add(IrDiagnostic.Error($"Terminator '{instr.Op}' in block {block.Id} expects {expectedCount} target(s); {targets.Length} provided."));
                            }

                            foreach (var target in targets)
                            {
                                if (!blockIds.Contains(target))
                                {
                                    diagnostics.Add(IrDiagnostic.Error($"Branch from block {block.Id} targets unknown block '{target}'."));
                                }
                            }

                            branchTargetsByBlock[block.Id] = targets;
                        }

                        if (string.Equals(instr.Op, "BranchCond", StringComparison.OrdinalIgnoreCase) && instr.Operands.Count > 0)
                        {
                            var conditionType = GetValueType(valueMap, instr.Operands[0]);
                            if (!string.IsNullOrWhiteSpace(conditionType) && !string.Equals(conditionType, "bool", StringComparison.OrdinalIgnoreCase))
                            {
                                diagnostics.Add(IrDiagnostic.Error($"BranchCond in block {block.Id} requires boolean condition; found '{conditionType}'."));
                            }
                        }
                    }

                    terminated = instr.Terminator;
                }

                diagnostics.AddRange(ValidateTypeRules(function, block, instr => GetValueType(valueMap, instr)));

                if (!branchTargetsByBlock.ContainsKey(block.Id))
                {
                    branchTargetsByBlock[block.Id] = Array.Empty<string>();
                }
            }

            if (string.IsNullOrWhiteSpace(function.Blocks.FirstOrDefault()?.Id))
            {
                diagnostics.Add(IrDiagnostic.Error($"Function {function.Name} is missing an entry block id."));
                continue;
            }

            foreach (var kvp in blockIdCounts)
            {
                if (kvp.Key.Length == 0)
                {
                    diagnostics.Add(IrDiagnostic.Error($"Function {function.Name} has block(s) with missing id."));
                }
            }

            var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var worklist = new Queue<string>();
            var entryId = function.Blocks.First().Id!;
            reachable.Add(entryId);
            worklist.Enqueue(entryId);

            while (worklist.Count > 0)
            {
                var current = worklist.Dequeue();
                if (!branchTargetsByBlock.TryGetValue(current, out var targets)) continue;

                foreach (var target in targets)
                {
                    if (reachable.Add(target))
                    {
                        worklist.Enqueue(target);
                    }
                }
            }

            foreach (var block in function.Blocks)
            {
                if (!string.IsNullOrWhiteSpace(block.Id) && !reachable.Contains(block.Id))
                {
                    diagnostics.Add(IrDiagnostic.Error($"Block {block.Id} in function {function.Name} is unreachable."));
                }
            }
        }

        return diagnostics;
    }

    private static IEnumerable<IrDiagnostic> ValidateTypeRules(IrFunction function, IrBlock block, Func<int, string?> getValueType)
    {
        var diagnostics = new List<IrDiagnostic>();

        foreach (var instr in block.Instructions)
        {
            if (instr.Result is int resultId && instr.Operands.Count == 1 && string.Equals(instr.Op, "Assign", StringComparison.OrdinalIgnoreCase))
            {
                var sourceType = getValueType(instr.Operands[0]);
                var destType = getValueType(resultId);
                if (!string.IsNullOrWhiteSpace(sourceType) && !string.IsNullOrWhiteSpace(destType) && !string.Equals(sourceType, destType, StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(IrDiagnostic.Error($"Assign in block {block.Id} mismatches source type '{sourceType}' and destination type '{destType}'."));
                }
            }

            if (instr.Operands.Count == 2 && IsBinaryOp(instr.Op))
            {
                var lhsType = getValueType(instr.Operands[0]);
                var rhsType = getValueType(instr.Operands[1]);
                var lhsScalar = GetScalarType(lhsType);
                var rhsScalar = GetScalarType(rhsType);
                if (IsNumericScalar(lhsScalar) && IsNumericScalar(rhsScalar) && !string.Equals(lhsScalar, rhsScalar, StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(IrDiagnostic.Error($"Binary op '{instr.Op}' in block {block.Id} mixes operand types '{lhsType}' and '{rhsType}'."));
                }

                if (instr.Result is int result && !string.IsNullOrWhiteSpace(instr.Type))
                {
                    var resultType = getValueType(result);
                    var resultScalar = GetScalarType(resultType);
                    if (IsNumericScalar(resultScalar) && IsNumericScalar(lhsScalar) && !string.Equals(resultScalar, lhsScalar, StringComparison.OrdinalIgnoreCase))
                    {
                        diagnostics.Add(IrDiagnostic.Error($"Binary op '{instr.Op}' in block {block.Id} result type '{resultType}' does not match operand type '{lhsType}'."));
                    }
                }
            }

            if (string.Equals(instr.Op, "Return", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(function.ReturnType, "void", StringComparison.OrdinalIgnoreCase))
                {
                    if (instr.Operands.Count > 0)
                    {
                        diagnostics.Add(IrDiagnostic.Error($"Void function {function.Name} should not return a value."));
                    }
                }
                else if (instr.Operands.Count == 1)
                {
                    var returnType = getValueType(instr.Operands[0]);
                    var returnScalar = GetScalarType(returnType);
                    var functionScalar = GetScalarType(function.ReturnType);
                    var returnComponents = GetComponentCount(returnType);
                    var functionComponents = GetComponentCount(function.ReturnType);
                    if (IsNumericScalar(returnScalar)
                        && IsNumericScalar(functionScalar)
                        && (!string.Equals(returnScalar, functionScalar, StringComparison.OrdinalIgnoreCase) || returnComponents != functionComponents))
                    {
                        diagnostics.Add(IrDiagnostic.Error($"Return in block {block.Id} uses type '{returnType}' but function {function.Name} declares return type '{function.ReturnType}'."));
                    }
                }
            }

            if (string.Equals(instr.Op, "Swizzle", StringComparison.OrdinalIgnoreCase) && instr.Operands.Count == 1)
            {
                var sourceType = getValueType(instr.Operands[0]);
                var resultType = instr.Result is int res ? getValueType(res) : instr.Type;
                var sourceScalar = GetScalarType(sourceType);
                var resultScalar = GetScalarType(resultType);
                if (!string.IsNullOrWhiteSpace(sourceScalar) && !string.IsNullOrWhiteSpace(resultScalar) && !string.Equals(sourceScalar, resultScalar, StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(IrDiagnostic.Error($"Swizzle in block {block.Id} changes scalar type from '{sourceScalar}' to '{resultScalar}'."));
                }
            }
        }

        return diagnostics;
    }

    private static string? GetValueType(IReadOnlyDictionary<int, IrValue> valueMap, int valueId)
    {
        return valueMap.TryGetValue(valueId, out var value) ? value.Type : null;
    }

    private static bool IsBinaryOp(string? op)
    {
        return op is "Add" or "Sub" or "Mul" or "Div" or "Mod" or "Eq" or "Ne" or "Lt" or "Le" or "Gt" or "Ge" or "LogicalAnd" or "LogicalOr";
    }

    private static bool IsNumericScalar(string? scalar)
    {
        return scalar is "float" or "double" or "half" or "int" or "uint";
    }

    private static string? GetScalarType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return type;

        var trimmed = type.Trim();
        var end = 0;
        while (end < trimmed.Length && char.IsLetter(trimmed[end]))
        {
            end++;
        }

        if (end == 0) return trimmed;
        return trimmed[..end];
    }

    private static int GetComponentCount(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return 0;

        var span = type.Trim();
        var index = 0;
        while (index < span.Length && char.IsLetter(span[index]))
        {
            index++;
        }

        var value = 0;
        while (index < span.Length && char.IsDigit(span[index]))
        {
            value = (value * 10) + (span[index] - '0');
            index++;
        }

        return value == 0 ? 1 : value;
    }

    private static bool IsResourceType(string? type)
    {
        return type is not null
               && (type.Contains("buffer", StringComparison.OrdinalIgnoreCase)
                   || type.Contains("texture", StringComparison.OrdinalIgnoreCase)
                   || type.Contains("sampler", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> GetBranchTargets(string tag)
    {
        foreach (var segment in tag.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = segment.Split(':', 2, StringSplitOptions.TrimEntries);
            yield return parts.Length == 2 ? parts[1] : parts[0];
        }
    }

    private static bool HasBackendSpecificTokens(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        static bool ContainsWhole(string haystack, string needle)
        {
            var span = haystack.AsSpan();
            var idx = span.IndexOf(needle.AsSpan(), StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return false;
            var beforeOk = idx == 0 || !char.IsLetter(span[idx - 1]);
            var afterIndex = idx + needle.Length;
            var afterOk = afterIndex >= span.Length || !char.IsLetter(span[afterIndex]);
            return beforeOk && afterOk;
        }

        return text.Contains("dxbc", StringComparison.OrdinalIgnoreCase)
               || text.Contains("dxil", StringComparison.OrdinalIgnoreCase)
               || text.Contains("spirv", StringComparison.OrdinalIgnoreCase)
               || text.Contains("d3d", StringComparison.OrdinalIgnoreCase)
               || text.Contains("glsl", StringComparison.OrdinalIgnoreCase)
               || ContainsWhole(text, "metal");
    }
}
