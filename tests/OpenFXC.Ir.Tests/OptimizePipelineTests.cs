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

    [Fact]
    public void Optimize_ConstfoldAndAlgebraic_SimplifyExpressions()
    {
        var values = new List<IrValue>
        {
            new IrValue { Id = 1, Kind = "Constant", Type = "float", Name = "2" },
            new IrValue { Id = 2, Kind = "Constant", Type = "float", Name = "3" }
        };

        var module = new IrModule
        {
            Profile = "vs_2_0",
            Values = values,
            Functions = new[]
            {
                new IrFunction
                {
                    Name = "main",
                    ReturnType = "float",
                    Blocks = new[]
                    {
                        new IrBlock
                        {
                            Id = "entry",
                            Instructions = new[]
                            {
                                new IrInstruction { Op = "Add", Operands = new[] { 1, 2 }, Result = 3, Type = "float" },
                                new IrInstruction { Op = "Return", Operands = new[] { 3 }, Terminator = true }
                            }
                        }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(module, SerializerOptions);
        var pipeline = new OptimizePipeline();
        var result = pipeline.Optimize(new OptimizeRequest(json, "constfold,algebraic", null));

        var foldedConst = result.Values.FirstOrDefault(v => v.Kind == "Constant" && v.Name == "5");
        Assert.NotNull(foldedConst);
        var retBlock = result.Functions.Single().Blocks.Single();
        Assert.Contains(retBlock.Instructions, i => i.Op == "Assign" || i.Op == "Return");
    }

    [Fact]
    public void Optimize_CopyPropAndDce_RemoveDeadAssign()
    {
        var module = new IrModule
        {
            Profile = "vs_2_0",
            Values = new[]
            {
                new IrValue { Id = 1, Kind = "Parameter", Type = "float" }
            },
            Functions = new[]
            {
                new IrFunction
                {
                    Name = "main",
                    ReturnType = "float",
                    Parameters = new[] { 1 },
                    Blocks = new[]
                    {
                        new IrBlock
                        {
                            Id = "entry",
                            Instructions = new[]
                            {
                                new IrInstruction { Op = "Assign", Operands = new[] { 1 }, Result = 2, Type = "float" },
                                new IrInstruction { Op = "Return", Operands = new[] { 2 }, Terminator = true }
                            }
                        }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(module, SerializerOptions);
        var pipeline = new OptimizePipeline();
        var result = pipeline.Optimize(new OptimizeRequest(json, "copyprop,dce", null));

        var block = result.Functions.Single().Blocks.Single();
        Assert.Single(block.Instructions);
        Assert.Equal("Return", block.Instructions.Single().Op);
        Assert.Equal(1, block.Instructions.Single().Operands.Single());
    }

    [Fact]
    public void Optimize_Dce_RemovesUnusedPureOp()
    {
        var module = new IrModule
        {
            Profile = "vs_2_0",
            Values = new[]
            {
                new IrValue { Id = 1, Kind = "Parameter", Type = "float" }
            },
            Functions = new[]
            {
                new IrFunction
                {
                    Name = "main",
                    ReturnType = "float",
                    Parameters = new[] { 1 },
                    Blocks = new[]
                    {
                        new IrBlock
                        {
                            Id = "entry",
                            Instructions = new[]
                            {
                                new IrInstruction { Op = "Add", Operands = new[] { 1, 1 }, Result = 2, Type = "float" },
                                new IrInstruction { Op = "Return", Operands = new[] { 1 }, Terminator = true }
                            }
                        }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(module, SerializerOptions);
        var pipeline = new OptimizePipeline();
        var result = pipeline.Optimize(new OptimizeRequest(json, "dce", null));

        var block = result.Functions.Single().Blocks.Single();
        Assert.Single(block.Instructions);
        Assert.Equal("Return", block.Instructions.Single().Op);
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
