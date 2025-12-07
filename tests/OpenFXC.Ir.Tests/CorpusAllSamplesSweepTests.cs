using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;
using OpenFXC.Hlsl;
using OpenFXC.Ir;
using OpenFXC.Sem;

namespace OpenFXC.Ir.Tests;

/// <summary>
/// Optional long-running sweep over the entire samples corpus (.fx and .hlsl).
/// Enable by setting RUN_SAMPLE_CORPUS=1 to exercise parse->sem->lower->optimize across all discovered entries.
/// </summary>
public class CorpusAllSamplesSweepTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = 0 // allow deep DXSDK trees without cycle errors
    };

    public static IEnumerable<object[]> CorpusFiles
    {
        get
        {
            if (!Enabled)
            {
                return new[] { new object[] { "SKIP" } };
            }

            var files = EnumerateSampleFiles().ToList();
            if (files.Count == 0)
            {
                return new[] { new object[] { "SKIP_NO_FILES" } };
            }

            return files.Select(f => new object[] { f });
        }
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void Sweep_Sample_File(string file)
    {
        if (!Enabled)
        {
            return;
        }

        if (string.Equals(file, "SKIP", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(file, "SKIP_NO_FILES", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var failures = SweepFile(file);
        Assert.True(failures.Count == 0, $"{file} failed: {string.Join(" | ", failures)}");
    }

    private static List<string> SweepFile(string file)
    {
        var fileStopwatch = Stopwatch.StartNew();
        var failures = new List<string>();
        var hlsl = File.ReadAllText(file);
        var (tokens, lexDiagnostics) = HlslLexer.Lex(hlsl);
        var (root, parseDiagnostics) = Parser.Parse(tokens, hlsl.Length);

        var parseResult = new ParseResult(
            FormatVersion: 1,
            Source: new SourceInfo(Path.GetFileName(file), hlsl.Length),
            Root: root,
            Tokens: tokens,
            Diagnostics: lexDiagnostics.Concat(parseDiagnostics).ToArray());

        var astJson = JsonSerializer.Serialize(parseResult, SerializerOptions);

        // First pass: discover techniques/entries if present.
        var initialSem = new SemanticAnalyzer("ps_4_0", "main", astJson).Analyze();
        var candidates = GatherEntries(initialSem);
        if (candidates.Count == 0)
        {
            candidates.Add(new EntryCandidate("vs_4_0", "main"));
        }

        foreach (var candidate in candidates)
        {
            try
            {
                var semantic = new SemanticAnalyzer(candidate.Profile, candidate.Entry, astJson).Analyze();
                var semanticJson = JsonSerializer.Serialize(semantic, SerializerOptions);

                var entryStopwatch = Stopwatch.StartNew();
                var lowered = new LoweringPipeline().Lower(new LoweringRequest(semanticJson, candidate.Profile, candidate.Entry));
                // Keep going even if diagnostics exist; only invariant errors or thrown exceptions will surface.
                var lowerInvariantErrors = IrInvariants.Validate(lowered).Where(d => string.Equals(d.Severity, "Error", StringComparison.OrdinalIgnoreCase)).ToList();
                if (lowerInvariantErrors.Count > 0)
                {
                    failures.Add($"{file} [{candidate.Profile}:{candidate.Entry}] lowering invariants: {string.Join("; ", lowerInvariantErrors.Select(e => e.Message))}");
                    continue;
                }

                var loweredJson = JsonSerializer.Serialize(lowered, SerializerOptions);
                var optimized = new OptimizePipeline().Optimize(new OptimizeRequest(loweredJson, "constfold,algebraic,dce,component-dce,copyprop", candidate.Profile));
                var optInvariantErrors = IrInvariants.Validate(optimized).Where(d => string.Equals(d.Severity, "Error", StringComparison.OrdinalIgnoreCase)).ToList();
                if (optInvariantErrors.Count > 0)
                {
                    failures.Add($"{file} [{candidate.Profile}:{candidate.Entry}] optimized invariants: {string.Join("; ", optInvariantErrors.Select(e => e.Message))}");
                    continue;
                }

                Console.WriteLine($"[CORPUS] {Path.GetFileName(file)} [{candidate.Profile}:{candidate.Entry}] {entryStopwatch.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                failures.Add($"{file} [{candidate.Profile}:{candidate.Entry}] error: {ex.Message}");
            }
        }

        Console.WriteLine($"[CORPUS] {file} total {fileStopwatch.ElapsedMilliseconds} ms");
        return failures;
    }

    private static List<EntryCandidate> GatherEntries(SemanticOutput semantic)
    {
        var list = new List<EntryCandidate>();
        if (semantic.Techniques is not null)
        {
            foreach (var tech in semantic.Techniques)
            {
                foreach (var pass in tech.Passes ?? Array.Empty<FxPassInfo>())
                {
                    foreach (var shader in pass.Shaders ?? Array.Empty<FxShaderBinding>())
                    {
                        if (string.IsNullOrWhiteSpace(shader.Entry)) continue;
                        var profile = shader.Profile ?? ProfileFromStage(shader.Stage);
                        list.Add(new EntryCandidate(profile, shader.Entry));
                    }
                }
            }
        }

        // Deduplicate by profile+entry.
        return list
            .GroupBy(e => $"{e.Profile}:{e.Entry}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private static string ProfileFromStage(string? stage)
    {
        return stage?.ToLowerInvariant() switch
        {
            "vertex" => "vs_4_0",
            "pixel" => "ps_4_0",
            "geometry" => "gs_4_0",
            "hull" => "hs_5_0",
            "domain" => "ds_5_0",
            "compute" => "cs_5_0",
            _ => "ps_4_0"
        };
    }

    private static IEnumerable<string> EnumerateSampleFiles()
    {
        var repoRoot = GetRepoRoot();
        var sampleRoot = Path.Combine(repoRoot, "samples");
        return Directory.EnumerateFiles(sampleRoot, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".fx", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".hlsl", StringComparison.OrdinalIgnoreCase));
    }

    private static bool Enabled => string.Equals(Environment.GetEnvironmentVariable("RUN_SAMPLE_CORPUS"), "1", StringComparison.Ordinal);

    private static string GetRepoRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private sealed record EntryCandidate(string Profile, string Entry);
}
