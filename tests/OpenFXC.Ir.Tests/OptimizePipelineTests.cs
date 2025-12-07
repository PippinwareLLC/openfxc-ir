using System.Text.Json;
using System.Text.Json.Serialization;
using OpenFXC.Ir;

namespace OpenFXC.Ir.Tests;

public class OptimizePipelineTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [Fact]
    public void Optimize_PassthroughWithProfileOverride()
    {
        var module = BuildMinimalModule("vs_2_0");
        var json = JsonSerializer.Serialize(module, SerializerOptions);
        var pipeline = new OptimizePipeline();

        var result = pipeline.Optimize(new OptimizeRequest(json, "constfold,dce", "ps_2_0"));

        Assert.Equal("ps_2_0", result.Profile);
        Assert.Contains(result.Diagnostics, d => d.Severity == "Info" && d.Stage == "optimize");
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == "Error");
    }

    [Fact]
    public void Optimize_AppendsInvariantDiagnostics()
    {
        var module = new IrModule
        {
            Profile = "vs_2_0",
            Values = new[] { new IrValue { Id = 1, Kind = "Parameter", Type = "float4" } },
            Functions = new[]
            {
                new IrFunction
                {
                    Name = "main",
                    ReturnType = "float4",
                    Parameters = new[] { 1 },
                    Blocks = new[]
                    {
                        new IrBlock
                        {
                            Id = "entry",
                            Instructions = Array.Empty<IrInstruction>() // missing terminator
                        }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(module, SerializerOptions);
        var pipeline = new OptimizePipeline();

        var result = pipeline.Optimize(new OptimizeRequest(json, null, null));

        Assert.Contains(result.Diagnostics, d => d.Severity == "Error");
    }

    private static IrModule BuildMinimalModule(string profile)
    {
        return new IrModule
        {
            Profile = profile,
            EntryPoint = new IrEntryPoint { Function = "main", Stage = "Vertex" },
            Values = new[] { new IrValue { Id = 1, Kind = "Parameter", Type = "float4" } },
            Functions = new[]
            {
                new IrFunction
                {
                    Name = "main",
                    ReturnType = "float4",
                    Parameters = new[] { 1 },
                    Blocks = new[]
                    {
                        new IrBlock
                        {
                            Id = "entry",
                            Instructions = new[]
                            {
                                new IrInstruction
                                {
                                    Op = "Return",
                                    Operands = new[] { 1 },
                                    Terminator = true
                                }
                            }
                        }
                    }
                }
            }
        };
    }
}
