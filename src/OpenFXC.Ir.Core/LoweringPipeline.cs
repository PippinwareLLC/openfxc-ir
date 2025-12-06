using System.Text.Json;
using OpenFXC.Sem;

namespace OpenFXC.Ir;

public sealed class LoweringPipeline
{
    private static readonly JsonSerializerOptions SemanticSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly IReadOnlyList<IrDiagnostic> SkeletonDiagnostics = new[]
    {
        IrDiagnostic.Info("IR lowering not implemented (M0 skeleton)")
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

        return new IrModule
        {
            Profile = profile,
            Entry = entry,
            Diagnostics = SkeletonDiagnostics
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
}
