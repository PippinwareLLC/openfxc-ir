using System.Text.Json;
using System.Text.Json.Serialization;
using OpenFXC.Hlsl;
using OpenFXC.Ir;
using OpenFXC.Sem;

namespace OpenFXC.Ir.Tests;

public class CorpusSamplesTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static IEnumerable<object[]> SampleCases => new[]
    {
        new object[]
        {
            "tutorial02_vs",
            "vs_4_0",
            "VS",
            Path.Combine(GetRepoRoot(), "samples", "dxsdk", "DXSDK_Aug08", "DXSDK", "Samples", "C++", "Direct3D10", "Tutorials", "Tutorial02", "Tutorial02.fx")
        },
        new object[]
        {
            "tutorial02_ps",
            "ps_4_0",
            "PS",
            Path.Combine(GetRepoRoot(), "samples", "dxsdk", "DXSDK_Aug08", "DXSDK", "Samples", "C++", "Direct3D10", "Tutorials", "Tutorial02", "Tutorial02.fx")
        },
        new object[]
        {
            "tutorial04_vs",
            "vs_4_0",
            "VS",
            Path.Combine(GetRepoRoot(), "samples", "dxsdk", "DXSDK_Aug08", "DXSDK", "Samples", "C++", "Direct3D10", "Tutorials", "Tutorial04", "Tutorial04.fx")
        },
        new object[]
        {
            "tutorial04_ps",
            "ps_4_0",
            "PS",
            Path.Combine(GetRepoRoot(), "samples", "dxsdk", "DXSDK_Aug08", "DXSDK", "Samples", "C++", "Direct3D10", "Tutorials", "Tutorial04", "Tutorial04.fx")
        },
        new object[]
        {
            "tutorial06_vs",
            "vs_4_0",
            "VS",
            Path.Combine(GetRepoRoot(), "samples", "dxsdk", "DXSDK_Aug08", "DXSDK", "Samples", "C++", "Direct3D10", "Tutorials", "Tutorial06", "Tutorial06.fx")
        },
        new object[]
        {
            "tutorial06_ps",
            "ps_4_0",
            "PS",
            Path.Combine(GetRepoRoot(), "samples", "dxsdk", "DXSDK_Aug08", "DXSDK", "Samples", "C++", "Direct3D10", "Tutorials", "Tutorial06", "Tutorial06.fx")
        }
    };

    [Theory]
    [MemberData(nameof(SampleCases))]
    public void LowerAndOptimize_RunOnSamples(string name, string profile, string entry, string hlslPath)
    {
        var semanticJson = BuildSemanticJsonFromFile(hlslPath, profile, entry);
        var lower = new LoweringPipeline().Lower(new LoweringRequest(semanticJson, null, entry));

        var lowerErrors = lower.Diagnostics.Where(d => string.Equals(d.Severity, "Error", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.True(lowerErrors.Count == 0, $"{name} lowering errors: {string.Join("; ", lowerErrors.Select(e => e.Message))}");

        var invariantDiagnostics = IrInvariants.Validate(lower);
        var invariantErrors = invariantDiagnostics.Where(d => string.Equals(d.Severity, "Error", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.True(invariantErrors.Count == 0, $"{name} invariant errors: {string.Join("; ", invariantErrors.Select(e => e.Message))}");

        var lowerJson = JsonSerializer.Serialize(lower, SerializerOptions);
        var optimized = new OptimizePipeline().Optimize(new OptimizeRequest(lowerJson, "constfold,algebraic,dce,component-dce,copyprop", null));

        var optimizeErrors = optimized.Diagnostics.Where(d => string.Equals(d.Severity, "Error", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.True(optimizeErrors.Count == 0, $"{name} optimize errors: {string.Join("; ", optimizeErrors.Select(e => e.Message))}");

        var optimizedInvariants = IrInvariants.Validate(optimized);
        var optimizedInvariantErrors = optimizedInvariants.Where(d => string.Equals(d.Severity, "Error", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.True(optimizedInvariantErrors.Count == 0, $"{name} optimized invariant errors: {string.Join("; ", optimizedInvariantErrors.Select(e => e.Message))}");
    }

    private static string BuildSemanticJsonFromFile(string hlslPath, string profile, string entry)
    {
        var hlsl = File.ReadAllText(hlslPath);
        var (tokens, lexDiagnostics) = HlslLexer.Lex(hlsl);
        var (root, parseDiagnostics) = Parser.Parse(tokens, hlsl.Length);

        var parseResult = new ParseResult(
            FormatVersion: 1,
            Source: new SourceInfo(Path.GetFileName(hlslPath), hlsl.Length),
            Root: root,
            Tokens: tokens,
            Diagnostics: lexDiagnostics.Concat(parseDiagnostics).ToArray());

        var astJson = JsonSerializer.Serialize(parseResult, SerializerOptions);
        var semantic = new SemanticAnalyzer(profile, entry, astJson).Analyze();

        return JsonSerializer.Serialize(semantic, SerializerOptions);
    }

    private static string GetRepoRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
