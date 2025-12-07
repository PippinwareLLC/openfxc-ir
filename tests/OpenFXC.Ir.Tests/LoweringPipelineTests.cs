using System.Text.Json;
using System.Text.Json.Serialization;
using OpenFXC.Hlsl;
using OpenFXC.Ir;
using OpenFXC.Sem;

namespace OpenFXC.Ir.Tests;

public class LoweringPipelineTests
{
    [Fact]
    public void Lower_UsesSemanticProfile_WhenNoOverrideProvided()
    {
        var pipeline = new LoweringPipeline();
        var semanticJson = BuildSemanticJsonFromHlsl();

        var result = pipeline.Lower(new LoweringRequest(semanticJson, null, null));

        Assert.Equal(1, result.FormatVersion);
        Assert.Equal("vs_2_0", result.Profile);
        Assert.Equal("main", result.EntryPoint?.Function);
        Assert.Equal("Vertex", result.EntryPoint?.Stage);
        Assert.Contains(result.Values, v => v.Kind == "Parameter" && v.Type == "float4");
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == "Error");
    }

    [Fact]
    public void Lower_EmitsResourcesAndReturnPlaceholder()
    {
        var pipeline = new LoweringPipeline();
        var semanticJson = BuildSemanticJsonFromHlsl();

        var result = pipeline.Lower(new LoweringRequest(semanticJson, null, null));

        Assert.Contains(result.Resources, r => r.Name == "WorldViewProj" && r.Kind == "GlobalVariable");
        Assert.Contains(result.Resources, r => r.Name == "DiffuseSampler" && r.Kind == "Sampler");
        Assert.DoesNotContain(result.Values, v => v.Kind == "Undef");
        var func = Assert.Single(result.Functions);
        var block = Assert.Single(func.Blocks);
        Assert.Equal(2, block.Instructions.Count);
        var call = block.Instructions[0];
        Assert.Equal("Call", call.Op);
        Assert.False(call.Terminator);
        Assert.NotNull(call.Result);
        var ret = block.Instructions[1];
        Assert.True(ret.Terminator);
        Assert.Equal("Return", ret.Op);
        Assert.Equal(call.Result, ret.Operands.Single());
    }

    [Fact]
    public void Lower_PrefersProfileOverride_WhenProvided()
    {
        var pipeline = new LoweringPipeline();
        var semanticJson = BuildSemanticJsonFromHlsl();

        var result = pipeline.Lower(new LoweringRequest(semanticJson, "vs_4_0", null));

        Assert.Equal("vs_4_0", result.Profile);
    }

    [Fact]
    public void Lower_UsesEntryOverride_WhenProvided()
    {
        var pipeline = new LoweringPipeline();
        var semanticJson = BuildSemanticJsonFromHlsl();

        var result = pipeline.Lower(new LoweringRequest(semanticJson, null, "customEntry"));

        Assert.Null(result.EntryPoint);
        Assert.Contains(result.Diagnostics, d => d.Severity == "Error");
    }

    [Fact]
    public void Lower_Throws_OnInvalidJson()
    {
        var pipeline = new LoweringPipeline();

        Assert.ThrowsAny<Exception>(() => pipeline.Lower(new LoweringRequest("{not json}", null, null)));
    }

    private static string BuildSemanticJsonFromHlsl()
    {
        var hlsl = """
        float4x4 WorldViewProj;
        sampler2D DiffuseSampler;

        float4 main(float4 pos : POSITION0) : POSITION
        {
            return mul(pos, WorldViewProj);
        }
        """;

        var (tokens, lexDiagnostics) = HlslLexer.Lex(hlsl);
        var (root, parseDiagnostics) = Parser.Parse(tokens, hlsl.Length);

        var parseResult = new ParseResult(
            FormatVersion: 1,
            Source: new SourceInfo("test.hlsl", hlsl.Length),
            Root: root,
            Tokens: tokens,
            Diagnostics: lexDiagnostics.Concat(parseDiagnostics).ToArray());

        var astJson = JsonSerializer.Serialize(parseResult, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        var semantic = new SemanticAnalyzer("vs_2_0", "main", astJson).Analyze();

        return JsonSerializer.Serialize(semantic, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }
}
