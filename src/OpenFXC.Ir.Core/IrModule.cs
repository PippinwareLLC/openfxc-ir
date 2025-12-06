using System.Text.Json.Serialization;

namespace OpenFXC.Ir;

public sealed record IrModule
{
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; init; } = 1;

    [JsonPropertyName("profile")]
    public string? Profile { get; init; }

    [JsonPropertyName("entry")]
    public string? Entry { get; init; }

    [JsonPropertyName("functions")]
    public IReadOnlyList<IrFunction> Functions { get; init; } = Array.Empty<IrFunction>();

    [JsonPropertyName("resources")]
    public IReadOnlyList<IrResource> Resources { get; init; } = Array.Empty<IrResource>();

    [JsonPropertyName("diagnostics")]
    public IReadOnlyList<IrDiagnostic> Diagnostics { get; init; } = Array.Empty<IrDiagnostic>();
}

public sealed record IrFunction
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("blocks")]
    public IReadOnlyList<IrBlock> Blocks { get; init; } = Array.Empty<IrBlock>();
}

public sealed record IrBlock
{
    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    [JsonPropertyName("instructions")]
    public IReadOnlyList<IrInstruction> Instructions { get; init; } = Array.Empty<IrInstruction>();
}

public sealed record IrInstruction
{
    [JsonPropertyName("op")]
    public string Op { get; init; } = "Nop";
}

public sealed record IrResource
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;
}

public sealed record IrDiagnostic
{
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("severity")]
    public string Severity { get; init; } = "Info";

    [JsonPropertyName("stage")]
    public string Stage { get; init; } = "lower";

    public static IrDiagnostic Info(string message, string? stage = "lower")
    {
        return new IrDiagnostic
        {
            Message = message,
            Severity = "Info",
            Stage = stage ?? "lower"
        };
    }
}
