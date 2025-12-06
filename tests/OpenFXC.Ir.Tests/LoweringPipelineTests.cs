using OpenFXC.Ir;

namespace OpenFXC.Ir.Tests;

public class LoweringPipelineTests
{
    private const string SemanticJson = """
    {
      "formatVersion": 1,
      "profile": "ps_2_0",
      "entryPoints": [
        { "name": "main" }
      ]
    }
    """;

    [Fact]
    public void Lower_UsesSemanticProfile_WhenNoOverrideProvided()
    {
        var pipeline = new LoweringPipeline();

        var result = pipeline.Lower(new LoweringRequest(SemanticJson, null, null));

        Assert.Equal(1, result.FormatVersion);
        Assert.Equal("ps_2_0", result.Profile);
        Assert.Equal("main", result.Entry);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("M0 skeleton", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Lower_PrefersProfileOverride_WhenProvided()
    {
        var pipeline = new LoweringPipeline();

        var result = pipeline.Lower(new LoweringRequest(SemanticJson, "vs_4_0", null));

        Assert.Equal("vs_4_0", result.Profile);
    }

    [Fact]
    public void Lower_UsesEntryOverride_WhenProvided()
    {
        var pipeline = new LoweringPipeline();

        var result = pipeline.Lower(new LoweringRequest(SemanticJson, null, "customEntry"));

        Assert.Equal("customEntry", result.Entry);
    }

    [Fact]
    public void Lower_Throws_OnInvalidJson()
    {
        var pipeline = new LoweringPipeline();

        Assert.ThrowsAny<Exception>(() => pipeline.Lower(new LoweringRequest("{not json}", null, null)));
    }
}
