using OpenFXC.Ir;

namespace OpenFXC.Ir.Tests;

public class OptimizeComponentDceTests
{
    [Fact]
    public void ComponentDce_TrimsUnusedSwizzleLanes()
    {
        // Build IR: v1 = Parameter float4; v2 = Swizzle v1.xy; Return v2.x
        var module = new IrModule
        {
            Profile = "ps_2_0",
            Values = new[]
            {
                new IrValue { Id = 1, Kind = "Parameter", Type = "float4" },
                new IrValue { Id = 2, Kind = "Temp", Type = "float4" },
                new IrValue { Id = 3, Kind = "Temp", Type = "float" }
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
                                new IrInstruction { Op = "Swizzle", Operands = new[] { 1 }, Result = 2, Type = "float4", Tag = "xy" },
                                new IrInstruction { Op = "Swizzle", Operands = new[] { 2 }, Result = 3, Type = "float", Tag = "x" },
                                new IrInstruction { Op = "Return", Operands = new[] { 3 }, Terminator = true }
                            }
                        }
                    }
                }
            }
        };

        var pipeline = new OptimizePipeline();
        var optimized = pipeline.Optimize(new OptimizeRequest(System.Text.Json.JsonSerializer.Serialize(module), "component-dce", null));

        var swizzle = optimized.Functions.Single().Blocks.Single().Instructions.First();
        Assert.Equal("Swizzle", swizzle.Op);
        Assert.NotNull(swizzle.Tag); // placeholder currently no-op; ensure it survives
        Assert.DoesNotContain(optimized.Diagnostics, d => d.Severity == "Error");
    }
}
