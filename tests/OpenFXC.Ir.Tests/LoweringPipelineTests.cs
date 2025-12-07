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
        Assert.True(block.Instructions.Count >= 2);
        var call = block.Instructions.First(i => i.Op == "Mul");
        Assert.Equal("Mul", call.Op);
        Assert.False(call.Terminator);
        Assert.NotNull(call.Result);
        var ret = block.Instructions.Last();
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

    [Fact]
    public void Lower_MapsTex2D_ToSampleOp()
    {
        var pipeline = new LoweringPipeline();
        var semanticJson = BuildSemanticJsonForTexture();

        var result = pipeline.Lower(new LoweringRequest(semanticJson, null, null));

        var func = Assert.Single(result.Functions);
        var block = Assert.Single(func.Blocks);
        Assert.Equal(2, block.Instructions.Count);
        var sample = block.Instructions[0];
        Assert.Equal("Sample", sample.Op);
        Assert.Equal(2, sample.Operands.Count);
        Assert.Equal("Return", block.Instructions[1].Op);
    }

    [Fact]
    public void Lower_UnsupportedIntrinsic_EmitsDiagnostic()
    {
        var pipeline = new LoweringPipeline();
        var semanticJson = BuildSemanticJsonForUnsupportedIntrinsic();

        var result = pipeline.Lower(new LoweringRequest(semanticJson, null, null));

        Assert.Contains(result.Diagnostics, d => d.Severity == "Error" && d.Message.Contains("Intrinsic", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Lower_NormalizeIntrinsic_IsRecognized()
    {
        var pipeline = new LoweringPipeline();
        var semanticJson = BuildSemanticJsonForNormalize();

        var result = pipeline.Lower(new LoweringRequest(semanticJson, null, null));

        var func = Assert.Single(result.Functions);
        var block = Assert.Single(func.Blocks);
        Assert.Contains(block.Instructions, i => i.Op == "Normalize");
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == "Error");
    }

    [Fact]
    public void Lower_StructuredBufferIndex_LoadsElement()
    {
        var pipeline = new LoweringPipeline();
        var semanticJson = BuildSemanticJsonForStructuredBuffer();

        var result = pipeline.Lower(new LoweringRequest(semanticJson, null, null));

        var func = Assert.Single(result.Functions);
        var block = Assert.Single(func.Blocks);
        Assert.Contains(block.Instructions, i => i.Op == "Index");
        Assert.True(block.Instructions.Last().Terminator);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == "Error");
    }

    [Fact]
    public void Lower_LoadsCbufferField()
    {
        var pipeline = new LoweringPipeline();
        var semanticJson = BuildSemanticJsonForCbuffer();

        var result = pipeline.Lower(new LoweringRequest(semanticJson, null, null));

        var func = Assert.Single(result.Functions);
        var block = Assert.Single(func.Blocks);
        Assert.True(block.Instructions.Count >= 1);
        Assert.Equal("Return", block.Instructions.Last().Op);
        Assert.Contains(result.Diagnostics, d => d.Severity == "Error");
    }

    [Fact]
    public void Lower_LowersIfAndWhile_ToCfgBlocks()
    {
        var pipeline = new LoweringPipeline();
        var semanticJson = BuildSemanticJsonForControlFlow();

        var result = pipeline.Lower(new LoweringRequest(semanticJson, null, null));

        var func = Assert.Single(result.Functions);
        Assert.True(func.Blocks.Count >= 4);
        Assert.Contains(func.Blocks, b => b.Id.Contains("while", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(func.Blocks, b => b.Id.Contains("if", StringComparison.OrdinalIgnoreCase) || b.Instructions.Any(i => i.Op == "BranchCond" && (i.Tag?.Contains("then:") ?? false)));
        Assert.True(func.Blocks.Any(b => b.Instructions.Any(i => i.Op == "BranchCond")), "Expected at least one conditional branch.");
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

    private static string BuildSemanticJsonForTexture()
    {
        var hlsl = """
        sampler2D S;

        float4 main(float2 uv : TEXCOORD0) : SV_Target
        {
            return tex2D(S, uv);
        }
        """;

        var (tokens, lexDiagnostics) = HlslLexer.Lex(hlsl);
        var (root, parseDiagnostics) = Parser.Parse(tokens, hlsl.Length);

        var parseResult = new ParseResult(
            FormatVersion: 1,
            Source: new SourceInfo("tex.hlsl", hlsl.Length),
            Root: root,
            Tokens: tokens,
            Diagnostics: lexDiagnostics.Concat(parseDiagnostics).ToArray());

        var astJson = JsonSerializer.Serialize(parseResult, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        var semantic = new SemanticAnalyzer("ps_2_0", "main", astJson).Analyze();

        return JsonSerializer.Serialize(semantic, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }

    private static string BuildSemanticJsonForUnsupportedIntrinsic()
    {
        var hlsl = """
        float4 main(float4 pos : POSITION0) : POSITION
        {
            return foo_intrinsic(pos);
        }
        """;

        var (tokens, lexDiagnostics) = HlslLexer.Lex(hlsl);
        var (root, parseDiagnostics) = Parser.Parse(tokens, hlsl.Length);

        var parseResult = new ParseResult(
            FormatVersion: 1,
            Source: new SourceInfo("intrinsic.hlsl", hlsl.Length),
            Root: root,
            Tokens: tokens,
            Diagnostics: lexDiagnostics.Concat(parseDiagnostics).ToArray());

        var astJson = JsonSerializer.Serialize(parseResult, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        var semantic = new SemanticAnalyzer("vs_2_0", "main", astJson).Analyze();
        if (semantic.Syntax?.Nodes is { } nodes)
        {
            var callNode = nodes.FirstOrDefault(n => string.Equals(n.Kind, "CallExpression", StringComparison.OrdinalIgnoreCase));
            if (callNode is not null)
            {
                var updatedNodes = nodes.Select(n =>
                    n.Id == callNode.Id
                        ? n with { CalleeKind = "Intrinsic", CalleeName = "foo_intrinsic" }
                        : n).ToArray();

                semantic = semantic with
                {
                    Syntax = semantic.Syntax with { Nodes = updatedNodes }
                };
            }
        }

        return JsonSerializer.Serialize(semantic, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }

    private static string BuildSemanticJsonForCbuffer()
    {
        var hlsl = """
        cbuffer C : register(b0)
        {
            float4 v;
        };

        float4 main() : SV_Position
        {
            return v;
        }
        """;

        var (tokens, lexDiagnostics) = HlslLexer.Lex(hlsl);
        var (root, parseDiagnostics) = Parser.Parse(tokens, hlsl.Length);

        var parseResult = new ParseResult(
            FormatVersion: 1,
            Source: new SourceInfo("cbuffer.hlsl", hlsl.Length),
            Root: root,
            Tokens: tokens,
            Diagnostics: lexDiagnostics.Concat(parseDiagnostics).ToArray());

        var astJson = JsonSerializer.Serialize(parseResult, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        var semantic = new SemanticAnalyzer("vs_4_0", "main", astJson).Analyze();

        return JsonSerializer.Serialize(semantic, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }

    private static string BuildSemanticJsonForControlFlow()
    {
        var hlsl = """
        float4 main(float x : TEXCOORD0) : SV_Target
        {
            float y = x;
            while (y > 0)
            {
                y = y - 1;
            }

            if (y > 0.5)
                return float4(1, 0, 0, 1);
            else
                return float4(0, 1, 0, 1);
        }
        """;

        var (tokens, lexDiagnostics) = HlslLexer.Lex(hlsl);
        var (root, parseDiagnostics) = Parser.Parse(tokens, hlsl.Length);

        var parseResult = new ParseResult(
            FormatVersion: 1,
            Source: new SourceInfo("controlflow.hlsl", hlsl.Length),
            Root: root,
            Tokens: tokens,
            Diagnostics: lexDiagnostics.Concat(parseDiagnostics).ToArray());

        var astJson = JsonSerializer.Serialize(parseResult, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        var semantic = new SemanticAnalyzer("ps_3_0", "main", astJson).Analyze();

        return JsonSerializer.Serialize(semantic, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }

    private static string BuildSemanticJsonForNormalize()
    {
        var hlsl = """
        float4 main(float4 pos : POSITION0) : POSITION
        {
            return normalize(pos);
        }
        """;

        var (tokens, lexDiagnostics) = HlslLexer.Lex(hlsl);
        var (root, parseDiagnostics) = Parser.Parse(tokens, hlsl.Length);

        var parseResult = new ParseResult(
            FormatVersion: 1,
            Source: new SourceInfo("normalize.hlsl", hlsl.Length),
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

    private static string BuildSemanticJsonForStructuredBuffer()
    {
        var hlsl = """
        StructuredBuffer<float4> Input : register(t0);

        float4 main(uint idx : SV_DispatchThreadID) : SV_Target
        {
            return Input[idx];
        }
        """;

        var (tokens, lexDiagnostics) = HlslLexer.Lex(hlsl);
        var (root, parseDiagnostics) = Parser.Parse(tokens, hlsl.Length);

        var parseResult = new ParseResult(
            FormatVersion: 1,
            Source: new SourceInfo("structured.hlsl", hlsl.Length),
            Root: root,
            Tokens: tokens,
            Diagnostics: lexDiagnostics.Concat(parseDiagnostics).ToArray());

        var astJson = JsonSerializer.Serialize(parseResult, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        var semantic = new SemanticAnalyzer("cs_5_0", "main", astJson).Analyze();

        return JsonSerializer.Serialize(semantic, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }
}
