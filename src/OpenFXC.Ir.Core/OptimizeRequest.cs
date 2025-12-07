namespace OpenFXC.Ir;

public sealed record OptimizeRequest(string IrJson, string? Passes, string? Profile);
