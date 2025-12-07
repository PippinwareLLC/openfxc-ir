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
        foreach (var pass in passes)
        {
            diagnostics.Add(IrDiagnostic.Info($"Pass '{pass}' not implemented; skipping.", "optimize"));
        }

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
            return Array.Empty<string>();
        }

        return passes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
