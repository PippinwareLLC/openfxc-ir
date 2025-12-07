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
}
