using System.Text.Json;

namespace OpenFXC.Ir;

public sealed class OptimizePipeline
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public IrModule Optimize(OptimizeRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        var module = DeserializeIr(request.IrJson);
        if (!string.IsNullOrWhiteSpace(request.Profile))
        {
            module = module with { Profile = request.Profile };
        }

        var diagnostics = new List<IrDiagnostic>(module.Diagnostics ?? Array.Empty<IrDiagnostic>());
        var passes = ParsePasses(request.Passes);
        module = RunPassPipeline(module, passes, diagnostics);

        var validated = IrInvariants.Validate(module);

        return module with
        {
            Diagnostics = diagnostics.Concat(validated).ToArray()
        };
    }

    private static IrModule DeserializeIr(string irJson)
    {
        var module = JsonSerializer.Deserialize<IrModule>(irJson, SerializerOptions);
        if (module is null)
        {
            throw new InvalidDataException("IR JSON deserialized to null.");
        }

        return module;
    }

    private static IReadOnlyList<string> ParsePasses(string? passes)
    {
        if (string.IsNullOrWhiteSpace(passes))
        {
            return new[] { "constfold", "algebraic", "copyprop", "cse", "dce", "component-dce" };
        }

        return passes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.ToLowerInvariant())
            .ToArray();
    }

    private static IrModule RunPassPipeline(IrModule module, IReadOnlyList<string> passes, List<IrDiagnostic> diagnostics)
    {
        foreach (var pass in passes)
        {
            module = pass switch
            {
                "constfold" => OptimizePasses.ConstantFold(module, diagnostics),
                "algebraic" => OptimizePasses.AlgebraicSimplify(module, diagnostics),
                "copyprop" => OptimizePasses.CopyPropagate(module, diagnostics),
                "dce" => OptimizePasses.DeadCodeEliminate(module, diagnostics),
                "cse" => OptimizePasses.CommonSubexpressionEliminate(module, diagnostics),
                "component-dce" => OptimizePasses.ComponentDce(module, diagnostics),
                _ => WithUnknownPassDiag(module, diagnostics, pass)
            };
        }

        return module;
    }

    private static IrModule WithUnknownPassDiag(IrModule module, List<IrDiagnostic> diagnostics, string pass)
    {
        diagnostics.Add(IrDiagnostic.Error($"Pass '{pass}' not recognized; available passes: {string.Join(", ", ParsePasses(null))}.", "optimize"));
        return module;
    }
}
