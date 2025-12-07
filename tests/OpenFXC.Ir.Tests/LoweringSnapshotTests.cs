using System.Text.Json;
using System.Text.Json.Serialization;
using OpenFXC.Hlsl;
using OpenFXC.Ir;
using OpenFXC.Sem;

namespace OpenFXC.Ir.Tests;

public class LoweringSnapshotTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions ModuleSerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public static IEnumerable<object[]> SnapshotCases => new[]
    {
        new object[] { "ps_texture", "ps_2_0", "main", SemSnapshotPath("ps_texture.hlsl"), SnapshotPath("ps_texture.ir.json"), true },
        new object[] { "ps_sm3_texproj", "ps_3_0", "main", SemSnapshotPath("ps_sm3_texproj.hlsl"), SnapshotPath("ps_sm3_texproj.ir.json"), true },
        new object[] { "sm4_cbuffer", "vs_4_0", "main", SemSnapshotPath("sm4_cbuffer.hlsl"), SnapshotPath("sm4_cbuffer.ir.json"), false },
        new object[] { "sm5_structured", "cs_5_0", "main", SemSnapshotPath("sm5_structured.hlsl"), SnapshotPath("sm5_structured.ir.json"), true },
        new object[] { "fx_basic", "vs_2_0", "main", SemSnapshotPath("fx_basic.hlsl"), SnapshotPath("fx_basic.ir.json"), true }
    };

    [Theory]
    [MemberData(nameof(SnapshotCases))]
    public void Lower_MatchesSnapshot(string name, string profile, string entry, string hlslPath, string snapshotPath, bool expectSuccess)
    {
        var semanticJson = BuildSemanticJsonFromFile(hlslPath, profile, entry);
        var pipeline = new LoweringPipeline();

        var module = pipeline.Lower(new LoweringRequest(semanticJson, null, entry));
        var actualJson = JsonSerializer.Serialize(module, ModuleSerializerOptions);

        MaybeUpdateSnapshot(snapshotPath, actualJson);

        var expectedJson = File.ReadAllText(snapshotPath);

        using var expectedDoc = JsonDocument.Parse(expectedJson);
        using var actualDoc = JsonDocument.Parse(actualJson);

        Assert.True(JsonEqual(expectedDoc.RootElement, actualDoc.RootElement), $"Snapshot '{name}' did not match.");

        if (expectSuccess)
        {
            Assert.DoesNotContain(module.Diagnostics, d => string.Equals(d.Severity, "Error", StringComparison.OrdinalIgnoreCase));
        }
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

    private static void MaybeUpdateSnapshot(string snapshotPath, string contents)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("UPDATE_IR_SNAPSHOTS"), "1", StringComparison.Ordinal))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
        File.WriteAllText(snapshotPath, contents);
    }

    private static string SemSnapshotPath(string name) => Path.Combine(GetRepoRoot(), "openfxc-sem", "tests", "snapshots", name);

    private static string SnapshotPath(string name) => Path.Combine(GetRepoRoot(), "tests", "OpenFXC.Ir.Tests", "snapshots", name);

    private static string GetRepoRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

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
        var leftItems = left.EnumerateArray().ToList();
        var rightItems = right.EnumerateArray().ToList();

        if (leftItems.Count != rightItems.Count)
        {
            return false;
        }

        for (var i = 0; i < leftItems.Count; i++)
        {
            if (!JsonEqual(leftItems[i], rightItems[i]))
            {
                return false;
            }
        }

        return true;
    }
}
