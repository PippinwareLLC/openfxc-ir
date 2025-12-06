using System.Text.Json;
using OpenFXC.Ir;
using OpenFXC.Sem;

namespace OpenFXC.Ir.Tests;

public class LoweringPipelineTests
{
    [Fact]
    public void Lower_UsesSemanticProfile_WhenNoOverrideProvided()
    {
        var pipeline = new LoweringPipeline();
        var semanticJson = BuildSemanticJsonFromFixture();

        var result = pipeline.Lower(new LoweringRequest(semanticJson, null, null));

        Assert.Equal(1, result.FormatVersion);
        Assert.Equal("ps_2_0", result.Profile);
        Assert.Equal("main", result.EntryPoint?.Function);
        Assert.Equal("Unknown", result.EntryPoint?.Stage);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("M0 skeleton", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Lower_PrefersProfileOverride_WhenProvided()
    {
        var pipeline = new LoweringPipeline();
        var semanticJson = BuildSemanticJsonFromFixture();

        var result = pipeline.Lower(new LoweringRequest(semanticJson, "vs_4_0", null));

        Assert.Equal("vs_4_0", result.Profile);
    }

    [Fact]
    public void Lower_UsesEntryOverride_WhenProvided()
    {
        var pipeline = new LoweringPipeline();
        var semanticJson = BuildSemanticJsonFromFixture();

        var result = pipeline.Lower(new LoweringRequest(semanticJson, null, "customEntry"));

        Assert.Equal("customEntry", result.EntryPoint?.Function);
    }

    [Fact]
    public void Lower_Throws_OnInvalidJson()
    {
        var pipeline = new LoweringPipeline();

        Assert.ThrowsAny<Exception>(() => pipeline.Lower(new LoweringRequest("{not json}", null, null)));
    }

    private static string BuildSemanticJsonFromFixture()
    {
        var fixturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "openfxc-sem", "tests", "fixtures", "sm1-smoke.ast.json"));
        var astJson = File.ReadAllText(fixturePath);
        var analyzer = new SemanticAnalyzer("ps_2_0", "main", astJson);
        var semantic = analyzer.Analyze();

        return JsonSerializer.Serialize(semantic, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
    }
}
