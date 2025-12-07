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
    public void Optimize_Constfold_VectorAndMatrix()
    {
        var values = new List<IrValue>
        {
            new IrValue { Id = 1, Kind = "Constant", Type = "float3", Name = "float3(1,2,3)" },
            new IrValue { Id = 2, Kind = "Constant", Type = "float3", Name = "float3(4,5,6)" },
            new IrValue { Id = 3, Kind = "Constant", Type = "float4x4", Name = "float4x4(1,0,0,1,0,1,0,1,0,0,1,1,1,1,1,1)" }
        };

        var module = new IrModule
        {
            Profile = "ps_4_0",
            Values = values,
            Functions = new[]
            {
                new IrFunction
                {
                    Name = "main",
                    ReturnType = "float4x4",
                    Blocks = new[]
                    {
                        new IrBlock
                        {
                            Id = "entry",
                            Instructions = new[]
                            {
                                new IrInstruction { Op = "Add", Operands = new[] { 1, 2 }, Result = 4, Type = "float3" },
                                new IrInstruction { Op = "Negate", Operands = new[] { 4 }, Result = 5, Type = "float3" },
                                new IrInstruction { Op = "Mul", Operands = new[] { 3, 3 }, Result = 6, Type = "float4x4" },
                                new IrInstruction { Op = "Return", Operands = new[] { 6 }, Terminator = true }
                            }
                        }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(module, SerializerOptions);
        var pipeline = new OptimizePipeline();
        var result = pipeline.Optimize(new OptimizeRequest(json, "constfold,algebraic", null));

        var folded = Assert.Single(result.Values, v => v.Kind == "Constant" && v.Type == "float3" && v.Name == "float3(5,7,9)");
        var negated = Assert.Single(result.Values, v => v.Kind == "Constant" && v.Type == "float3" && v.Name == "float3(-5,-7,-9)");

        var matrixConstants = result.Values.Where(v => v.Type == "float4x4" && v.Name == "float4x4(1,0,0,1,0,1,0,1,0,0,1,1,1,1,1,1)").ToList();
        Assert.True(matrixConstants.Count >= 1);

        var retBlock = result.Functions.Single().Blocks.Single();
        Assert.Contains(retBlock.Instructions, i => i.Op == "Assign" && i.Operands.SequenceEqual(new[] { folded.Id }));
        Assert.Contains(retBlock.Instructions, i => i.Op == "Assign" && i.Operands.SequenceEqual(new[] { negated.Id }));
        Assert.Contains(retBlock.Instructions, i => i.Op == "Assign" && matrixConstants.Any(mc => i.Operands.SequenceEqual(new[] { mc.Id })));
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
    public void Optimize_CopyProp_RespectsBranching()
    {
        var module = new IrModule
        {
            Profile = "ps_4_0",
            Values = new[]
            {
                new IrValue { Id = 1, Kind = "Parameter", Type = "bool" },
                new IrValue { Id = 2, Kind = "Constant", Type = "float", Name = "10" },
                new IrValue { Id = 3, Kind = "Constant", Type = "float", Name = "20" },
                new IrValue { Id = 4, Kind = "Temp", Type = "float" }
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
                                new IrInstruction { Op = "BranchCond", Operands = new[] { 1 }, Terminator = true, Tag = "then:then;else:else" }
                            }
                        },
                        new IrBlock
                        {
                            Id = "then",
                            Instructions = new[]
                            {
                                new IrInstruction { Op = "Return", Operands = new[] { 4 }, Terminator = true }
                            }
                        },
                        new IrBlock
                        {
                            Id = "else",
                            Instructions = new[]
                            {
                                new IrInstruction { Op = "Assign", Operands = new[] { 3 }, Result = 4, Type = "float" },
                                new IrInstruction { Op = "Return", Operands = new[] { 4 }, Terminator = true }
                            }
                        }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(module, SerializerOptions);
        var pipeline = new OptimizePipeline();
        var result = pipeline.Optimize(new OptimizeRequest(json, "copyprop", null));

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == "Error");

        var thenReturn = result.Functions.Single().Blocks.Single(b => b.Id == "then").Instructions.Single(i => i.Terminator);
        Assert.Equal(4, thenReturn.Operands.Single());

        var elseReturn = result.Functions.Single().Blocks.Single(b => b.Id == "else").Instructions.Single(i => i.Terminator);
        Assert.Equal(3, elseReturn.Operands.Single());
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

    [Fact]
    public void Optimize_Dce_PreservesSideEffects()
    {
        var module = new IrModule
        {
            Profile = "ps_4_0",
            Values = new[]
            {
                new IrValue { Id = 1, Kind = "Resource", Type = "RWTexture2D<float4>" },
                new IrValue { Id = 2, Kind = "Parameter", Type = "float4" },
                new IrValue { Id = 3, Kind = "Temp", Type = "float4" }
            },
            Functions = new[]
            {
                new IrFunction
                {
                    Name = "main",
                    ReturnType = "void",
                    Parameters = new[] { 1, 2 },
                    Blocks = new[]
                    {
                        new IrBlock
                        {
                            Id = "entry",
                            Instructions = new[]
                            {
                                new IrInstruction { Op = "Add", Operands = new[] { 2, 2 }, Result = 3, Type = "float4" },
                                new IrInstruction { Op = "Store", Operands = new[] { 1, 3 }, Tag = "direct", Type = "float4" },
                                new IrInstruction { Op = "Return", Terminator = true }
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
        Assert.Collection(block.Instructions,
            i => Assert.Equal("Add", i.Op),
            i => Assert.Equal("Store", i.Op),
            i => Assert.Equal("Return", i.Op));
    }

    [Fact]
    public void Optimize_FailsBackendLeakValidation()
    {
        var module = new IrModule
        {
            Profile = "ps_5_0",
            Values = new[]
            {
                new IrValue { Id = 1, Kind = "Resource", Type = "Texture2D" },
                new IrValue { Id = 2, Kind = "Parameter", Type = "float2" },
                new IrValue { Id = 3, Kind = "Sampler", Type = "SamplerState" },
                new IrValue { Id = 4, Kind = "Temp", Type = "float4" }
            },
            Functions = new[]
            {
                new IrFunction
                {
                    Name = "main",
                    ReturnType = "float4",
                    Parameters = new[] { 1, 2, 3 },
                    Blocks = new[]
                    {
                        new IrBlock
                        {
                            Id = "entry",
                            Instructions = new[]
                            {
                                new IrInstruction
                                {
                                    Op = "DxilSample",
                                    Operands = new[] { 1, 3, 2 },
                                    Result = 4,
                                    Type = "float4"
                                },
                                new IrInstruction { Op = "Return", Operands = new[] { 4 }, Terminator = true }
                            }
                        }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(module, SerializerOptions);
        var pipeline = new OptimizePipeline();
        var result = pipeline.Optimize(new OptimizeRequest(json, null, null));

        Assert.Contains(result.Diagnostics, d => d.Severity == "Error" && d.Message.Contains("backend", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Optimize_FailsInvalidBranchTargets()
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
                                new IrInstruction { Op = "Branch", Operands = Array.Empty<int>(), Terminator = true }
                            }
                        }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(module, SerializerOptions);
        var pipeline = new OptimizePipeline();
        var result = pipeline.Optimize(new OptimizeRequest(json, null, null));

        Assert.Contains(result.Diagnostics, d => d.Severity == "Error" && d.Message.Contains("missing target", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Optimize_FlagsUnknownPass()
    {
        var module = BuildMinimalModule("vs_2_0");
        var json = JsonSerializer.Serialize(module, SerializerOptions);
        var pipeline = new OptimizePipeline();

        var result = pipeline.Optimize(new OptimizeRequest(json, "constfold,unknown-pass", null));

        Assert.Contains(result.Diagnostics, d => d.Severity == "Error" && d.Stage == "optimize" && d.Message.Contains("not recognized", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Optimize_Cse_ReusesPureExpressionsWithinBlock()
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
                                new IrInstruction { Op = "Add", Operands = new[] { 1, 1 }, Result = 3, Type = "float" },
                                new IrInstruction { Op = "Return", Operands = new[] { 3 }, Terminator = true, Type = "float" }
                            }
                        }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(module, SerializerOptions);
        var pipeline = new OptimizePipeline();

        var result = pipeline.Optimize(new OptimizeRequest(json, "cse", null));

        var instructions = result.Functions.Single().Blocks.Single().Instructions;
        Assert.Collection(instructions,
            i => Assert.Equal("Add", i.Op),
            i => Assert.Equal("Assign", i.Op),
            i => Assert.Equal("Return", i.Op));
        Assert.Equal(new[] { 2 }, instructions[1].Operands);
    }

    [Fact]
    public void Optimize_Cse_RespectsSideEffectBarriers()
    {
        var module = new IrModule
        {
            Profile = "ps_4_0",
            Values = new[]
            {
                new IrValue { Id = 1, Kind = "Resource", Type = "RWTexture2D<float4>" },
                new IrValue { Id = 2, Kind = "Parameter", Type = "float4" },
                new IrValue { Id = 3, Kind = "Temp", Type = "float4" },
                new IrValue { Id = 4, Kind = "Temp", Type = "float4" }
            },
            Functions = new[]
            {
                new IrFunction
                {
                    Name = "main",
                    ReturnType = "void",
                    Parameters = new[] { 1, 2 },
                    Blocks = new[]
                    {
                        new IrBlock
                        {
                            Id = "entry",
                            Instructions = new[]
                            {
                                new IrInstruction { Op = "Add", Operands = new[] { 2, 2 }, Result = 3, Type = "float4" },
                                new IrInstruction { Op = "Store", Operands = new[] { 1, 3 }, Tag = "direct", Type = "float4" },
                                new IrInstruction { Op = "Add", Operands = new[] { 2, 2 }, Result = 4, Type = "float4" },
                                new IrInstruction { Op = "Return", Terminator = true }
                            }
                        }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(module, SerializerOptions);
        var pipeline = new OptimizePipeline();

        var result = pipeline.Optimize(new OptimizeRequest(json, "cse", null));

        var instructions = result.Functions.Single().Blocks.Single().Instructions;
        Assert.Equal(2, instructions.Count(i => i.Op == "Add"));
        Assert.Equal("Store", instructions[1].Op);
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
