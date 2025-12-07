using OpenFXC.Ir;

namespace OpenFXC.Ir.Tests;

public class IrInvariantsTests
{
    [Fact]
    public void Validate_AllowsWellFormedModule()
    {
        var module = new IrModule
        {
            Profile = "ps_2_0",
            EntryPoint = new IrEntryPoint { Function = "main", Stage = "Pixel" },
            Values = new[]
            {
                new IrValue { Id = 1, Kind = "Parameter", Type = "float4", Name = "pos" }
            },
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

        var diagnostics = IrInvariants.Validate(module);

        Assert.DoesNotContain(diagnostics, d => d.Severity == "Error");
    }

    [Fact]
    public void Validate_FlagsDuplicateValueIds()
    {
        var module = new IrModule
        {
            Values = new[]
            {
                new IrValue { Id = 1, Kind = "Parameter", Type = "float4" },
                new IrValue { Id = 1, Kind = "Temp", Type = "float4" }
            }
        };

        var diagnostics = IrInvariants.Validate(module);

        Assert.Contains(diagnostics, d => d.Message.Contains("Duplicate value id", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsMissingTerminator()
    {
        var module = new IrModule
        {
            Values = Array.Empty<IrValue>(),
            Functions = new[]
            {
                new IrFunction
                {
                    Name = "main",
                    ReturnType = "void",
                    Blocks = new[]
                    {
                        new IrBlock
                        {
                            Id = "b0",
                            Instructions = new[]
                            {
                                new IrInstruction { Op = "Nop" }
                            }
                        }
                    }
                }
            }
        };

        var diagnostics = IrInvariants.Validate(module);

        Assert.Contains(diagnostics, d => d.Message.Contains("not terminated", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsUnknownOperandValue()
    {
        var module = new IrModule
        {
            Values = new[] { new IrValue { Id = 1, Kind = "Parameter", Type = "float4" } },
            Functions = new[]
            {
                new IrFunction
                {
                    Name = "main",
                    ReturnType = "void",
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
                                    Operands = new[] { 2 },
                                    Terminator = true
                                }
                            }
                        }
                    }
                }
            }
        };

        var diagnostics = IrInvariants.Validate(module);

        Assert.Contains(diagnostics, d => d.Message.Contains("unknown value id 2", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsInvalidBranchTargets()
    {
        var module = new IrModule
        {
            Values = new[] { new IrValue { Id = 1, Kind = "Parameter", Type = "float" } },
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
                                new IrInstruction { Op = "Branch", Terminator = true, Tag = "missing" }
                            }
                        },
                        new IrBlock
                        {
                            Id = "entry",
                            Instructions = new[]
                            {
                                new IrInstruction { Op = "Return", Operands = new[] { 1 }, Terminator = true }
                            }
                        }
                    }
                }
            }
        };

        var diagnostics = IrInvariants.Validate(module);

        Assert.Contains(diagnostics, d => d.Message.Contains("duplicate block", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(diagnostics, d => d.Message.Contains("unknown block", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsBranchTargetArity()
    {
        var module = new IrModule
        {
            Values = new[] { new IrValue { Id = 1, Kind = "Parameter", Type = "float" } },
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
                                new IrInstruction { Op = "BranchCond", Terminator = true, Tag = "then:exit" }
                            }
                        },
                        new IrBlock
                        {
                            Id = "exit",
                            Instructions = new[]
                            {
                                new IrInstruction { Op = "Return", Operands = new[] { 1 }, Terminator = true }
                            }
                        }
                    }
                }
            }
        };

        var diagnostics = IrInvariants.Validate(module);

        Assert.Contains(diagnostics, d => d.Message.Contains("expects 2 target", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsUnreachableBlocks()
    {
        var module = new IrModule
        {
            Values = new[] { new IrValue { Id = 1, Kind = "Parameter", Type = "float" } },
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
                                new IrInstruction { Op = "Branch", Terminator = true, Tag = "exit" }
                            }
                        },
                        new IrBlock
                        {
                            Id = "unused",
                            Instructions = new[]
                            {
                                new IrInstruction { Op = "Return", Operands = new[] { 1 }, Terminator = true }
                            }
                        },
                        new IrBlock
                        {
                            Id = "exit",
                            Instructions = new[]
                            {
                                new IrInstruction { Op = "Return", Operands = new[] { 1 }, Terminator = true }
                            }
                        }
                    }
                }
            }
        };

        var diagnostics = IrInvariants.Validate(module);

        Assert.Contains(diagnostics, d => d.Message.Contains("unreachable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsBackendSpecificArtifacts()
    {
        var module = new IrModule
        {
            Values = new[]
            {
                new IrValue { Id = 1, Kind = "Temp", Type = "float4" }
            },
            Functions = new[]
            {
                new IrFunction
                {
                    Name = "main",
                    ReturnType = "float4",
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
                                    Result = 1,
                                    Terminator = true
                                }
                            }
                        }
                    }
                }
            }
        };

        var diagnostics = IrInvariants.Validate(module);

        Assert.Contains(diagnostics, d => d.Message.Contains("backend", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsBackendSpecificMetadata()
    {
        var module = new IrModule
        {
            Profile = "dxil-sm_6_0",
            EntryPoint = new IrEntryPoint { Function = "dxil_main", Stage = "Pixel" },
            Resources = new[] { new IrResource { Name = "tex0", Kind = "d3d-srv", Type = "Texture2D" } },
            Values = new[] { new IrValue { Id = 1, Kind = "Parameter", Type = "spirv.float4" } },
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
                                new IrInstruction { Op = "Return", Operands = new[] { 1 }, Terminator = true }
                            }
                        }
                    }
                }
            }
        };

        var diagnostics = IrInvariants.Validate(module);

        Assert.Contains(diagnostics, d => d.Message.Contains("backend-specific", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsMultipleDefinitions()
    {
        var module = new IrModule
        {
            Values = new[]
            {
                new IrValue { Id = 1, Kind = "Temp", Type = "float4" }
            },
            Functions = new[]
            {
                new IrFunction
                {
                    Name = "main",
                    ReturnType = "float4",
                    Blocks = new[]
                    {
                        new IrBlock
                        {
                            Id = "entry",
                            Instructions = new[]
                            {
                                new IrInstruction { Op = "Add", Result = 1, Terminator = false },
                                new IrInstruction { Op = "Mul", Result = 1, Terminator = true }
                            }
                        }
                    }
                }
            }
        };

        var diagnostics = IrInvariants.Validate(module);

        Assert.Contains(diagnostics, d => d.Message.Contains("defined multiple times", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsMalformedStore()
    {
        var module = new IrModule
        {
            Values = new[]
            {
                new IrValue { Id = 1, Kind = "Temp", Type = "float4" }
            },
            Functions = new[]
            {
                new IrFunction
                {
                    Name = "main",
                    ReturnType = "void",
                    Blocks = new[]
                    {
                        new IrBlock
                        {
                            Id = "entry",
                            Instructions = new[]
                            {
                                new IrInstruction { Op = "Store", Operands = new[] { 1 }, Terminator = false },
                                new IrInstruction { Op = "Return", Terminator = true }
                            }
                        }
                    }
                }
            }
        };

        var diagnostics = IrInvariants.Validate(module);

        Assert.Contains(diagnostics, d => d.Message.Contains("Store requires", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsResultTypeMismatch()
    {
        var module = new IrModule
        {
            Values = new[]
            {
                new IrValue { Id = 1, Kind = "Temp", Type = "float4" },
                new IrValue { Id = 2, Kind = "Temp", Type = "float4" }
            },
            Functions = new[]
            {
                new IrFunction
                {
                    Name = "main",
                    ReturnType = "float4",
                    Blocks = new[]
                    {
                        new IrBlock
                        {
                            Id = "entry",
                            Instructions = new[]
                            {
                                new IrInstruction
                                {
                                    Op = "Assign",
                                    Result = 1,
                                    Operands = new[] { 2 },
                                    Type = "float"
                                },
                                new IrInstruction { Op = "Return", Operands = new[] { 1 }, Terminator = true }
                            }
                        }
                    }
                }
            }
        };

        var diagnostics = IrInvariants.Validate(module);

        Assert.Contains(diagnostics, d => d.Message.Contains("does not match value", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsAssignTypeMismatch()
    {
        var module = new IrModule
        {
            Values = new[]
            {
                new IrValue { Id = 1, Kind = "Temp", Type = "float4" },
                new IrValue { Id = 2, Kind = "Temp", Type = "int" }
            },
            Functions = new[]
            {
                new IrFunction
                {
                    Name = "main",
                    ReturnType = "float4",
                    Blocks = new[]
                    {
                        new IrBlock
                        {
                            Id = "entry",
                            Instructions = new[]
                            {
                                new IrInstruction
                                {
                                    Op = "Assign",
                                    Result = 1,
                                    Operands = new[] { 2 },
                                    Type = "float4"
                                },
                                new IrInstruction { Op = "Return", Operands = new[] { 1 }, Terminator = true }
                            }
                        }
                    }
                }
            }
        };

        var diagnostics = IrInvariants.Validate(module);

        Assert.Contains(diagnostics, d => d.Message.Contains("Assign", StringComparison.OrdinalIgnoreCase) && d.Message.Contains("mismatches", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsStoreTypeMismatch()
    {
        var module = new IrModule
        {
            Values = new[]
            {
                new IrValue { Id = 1, Kind = "Temp", Type = "float4" },
                new IrValue { Id = 2, Kind = "Temp", Type = "int" }
            },
            Functions = new[]
            {
                new IrFunction
                {
                    Name = "main",
                    ReturnType = "void",
                    Blocks = new[]
                    {
                        new IrBlock
                        {
                            Id = "entry",
                            Instructions = new[]
                            {
                                new IrInstruction
                                {
                                    Op = "Store",
                                    Operands = new[] { 1, 2 },
                                    Terminator = false
                                },
                                new IrInstruction { Op = "Return", Terminator = true }
                            }
                        }
                    }
                }
            }
        };

        var diagnostics = IrInvariants.Validate(module);

        Assert.Contains(diagnostics, d => d.Message.Contains("Store type mismatch", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsReturnTypeMismatch()
    {
        var module = new IrModule
        {
            Values = new[] { new IrValue { Id = 1, Kind = "Temp", Type = "float" } },
            Functions = new[]
            {
                new IrFunction
                {
                    Name = "main",
                    ReturnType = "float4",
                    Blocks = new[]
                    {
                        new IrBlock
                        {
                            Id = "entry",
                            Instructions = new[]
                            {
                                new IrInstruction { Op = "Return", Operands = new[] { 1 }, Terminator = true }
                            }
                        }
                    }
                }
            }
        };

        var diagnostics = IrInvariants.Validate(module);

        Assert.Contains(diagnostics, d => d.Message.Contains("Return", StringComparison.OrdinalIgnoreCase) && d.Message.Contains("declares return type", StringComparison.OrdinalIgnoreCase));
    }
}
