using System.Text.Json;
using System.Text.Json.Serialization;
using OpenFXC.Hlsl;
using OpenFXC.Ir;
using OpenFXC.Sem;

namespace OpenFXC.Ir.Tests;

public class OptimizeSnapshotTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    [Fact]
    public void Optimize_LowerThenOptimize_MatchesSnapshot()
    {
        var hlsl = """
        sampler2D S;

        float4 main(float2 uv : TEXCOORD0) : SV_Target
        {
            float2 uv2 = uv;
            return tex2D(S, uv2);
        }
        """;

        var semanticJson = BuildSemanticJson(hlsl, "ps_2_0", "main");
        var lower = new LoweringPipeline().Lower(new LoweringRequest(semanticJson, null, "main"));
        var optimized = new OptimizePipeline().Optimize(new OptimizeRequest(JsonSerializer.Serialize(lower, SerializerOptions), null, null));
        var actualJson = JsonSerializer.Serialize(optimized, SerializerOptions);

        var snapshotPath = SnapshotPath("ps_texture.opt.ir.json");
        MaybeUpdateSnapshot(snapshotPath, actualJson);

        var expectedJson = File.ReadAllText(snapshotPath);
        using var expectedDoc = JsonDocument.Parse(expectedJson);
        using var actualDoc = JsonDocument.Parse(actualJson);

        Assert.True(JsonEqual(expectedDoc.RootElement, actualDoc.RootElement), "Optimized snapshot mismatch.");
        Assert.DoesNotContain(optimized.Diagnostics, d => string.Equals(d.Severity, "Error", StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildSemanticJson(string hlsl, string profile, string entry)
    {
        var (tokens, lexDiagnostics) = HlslLexer.Lex(hlsl);
        var (root, parseDiagnostics) = Parser.Parse(tokens, hlsl.Length);

        var parseResult = new ParseResult(
            FormatVersion: 1,
            Source: new SourceInfo("opt.hlsl", hlsl.Length),
            Root: root,
            Tokens: tokens,
            Diagnostics: lexDiagnostics.Concat(parseDiagnostics).ToArray());

        var astJson = JsonSerializer.Serialize(parseResult, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        var semantic = new SemanticAnalyzer(profile, entry, astJson).Analyze();

        return JsonSerializer.Serialize(semantic, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }

    private static string SnapshotPath(string name) => Path.Combine(GetRepoRoot(), "tests", "OpenFXC.Ir.Tests", "snapshots", name);

    private static string GetRepoRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static void MaybeUpdateSnapshot(string path, string contents)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("UPDATE_IR_SNAPSHOTS"), "1", StringComparison.Ordinal))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }

    private static bool JsonEqual(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind)
        {
            return false;
        }

        return left.ValueKind switch
        {
            JsonValueKind.Object => CompareObjects(left, right),
            JsonValueKind.Array => CompareArrays(left, right),
            JsonValueKind.String => string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal),
            JsonValueKind.Number => left.GetRawText() == right.GetRawText(),
            JsonValueKind.True or JsonValueKind.False => left.GetBoolean() == right.GetBoolean(),
            JsonValueKind.Null or JsonValueKind.Undefined => true,
            _ => left.GetRawText() == right.GetRawText()
        };
    }

    private static bool CompareObjects(JsonElement left, JsonElement right)
    {
        var leftProps = left.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).ToList();
        var rightProps = right.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).ToList();

        if (leftProps.Count != rightProps.Count)
        {
            return false;
        }

        for (var i = 0; i < leftProps.Count; i++)
        {
            if (!string.Equals(leftProps[i].Name, rightProps[i].Name, StringComparison.Ordinal))
            {
                return false;
            }

            if (!JsonEqual(leftProps[i].Value, rightProps[i].Value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool CompareArrays(JsonElement left, JsonElement right)
    {
        var l = left.EnumerateArray().ToList();
        var r = right.EnumerateArray().ToList();
        if (l.Count != r.Count) return false;
        for (var i = 0; i < l.Count; i++)
        {
            if (!JsonEqual(l[i], r[i])) return false;
        }

        return true;
    }
}
