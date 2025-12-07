using System.Text.Json.Serialization;

namespace OpenFXC.Ir;

public sealed record IrModule
{
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; init; } = 1;

    [JsonPropertyName("profile")]
    public string? Profile { get; init; }

    [JsonPropertyName("entryPoint")]
    public IrEntryPoint? EntryPoint { get; init; }

    [JsonPropertyName("functions")]
    public IReadOnlyList<IrFunction> Functions { get; init; } = Array.Empty<IrFunction>();

    [JsonPropertyName("values")]
    public IReadOnlyList<IrValue> Values { get; init; } = Array.Empty<IrValue>();

    [JsonPropertyName("resources")]
    public IReadOnlyList<IrResource> Resources { get; init; } = Array.Empty<IrResource>();

    [JsonPropertyName("techniques")]
    public IReadOnlyList<IrFxTechnique> Techniques { get; init; } = Array.Empty<IrFxTechnique>();

    [JsonPropertyName("diagnostics")]
    public IReadOnlyList<IrDiagnostic> Diagnostics { get; init; } = Array.Empty<IrDiagnostic>();
}

public sealed record IrEntryPoint
{
    [JsonPropertyName("function")]
    public string Function { get; init; } = string.Empty;

    [JsonPropertyName("stage")]
    public string Stage { get; init; } = "Unknown";
}

public sealed record IrFunction
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("returnType")]
    public string ReturnType { get; init; } = "void";

    [JsonPropertyName("parameters")]
    public IReadOnlyList<int> Parameters { get; init; } = Array.Empty<int>();

    [JsonPropertyName("blocks")]
    public IReadOnlyList<IrBlock> Blocks { get; init; } = Array.Empty<IrBlock>();
}

public sealed record IrBlock
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("instructions")]
    public IReadOnlyList<IrInstruction> Instructions { get; init; } = Array.Empty<IrInstruction>();
}

public sealed record IrInstruction
{
    [JsonPropertyName("op")]
    public string Op { get; init; } = "Nop";

    [JsonPropertyName("terminator")]
    public bool Terminator { get; init; }

    [JsonPropertyName("result")]
    public int? Result { get; init; }

    [JsonPropertyName("operands")]
    public IReadOnlyList<int> Operands { get; init; } = Array.Empty<int>();

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("tag")]
    public string? Tag { get; init; }
}

public sealed record IrResource
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("writable")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Writable { get; init; }
}

public sealed record IrValue
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "Temp";

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("semantic")]
    public string? Semantic { get; init; }
}

public sealed record IrFxTechnique
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("passes")]
    public IReadOnlyList<IrFxPass> Passes { get; init; } = Array.Empty<IrFxPass>();
}

public sealed record IrFxPass
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("shaders")]
    public IReadOnlyList<IrFxShaderBinding> Shaders { get; init; } = Array.Empty<IrFxShaderBinding>();

    [JsonPropertyName("states")]
    public IReadOnlyList<IrFxStateAssignment> States { get; init; } = Array.Empty<IrFxStateAssignment>();
}

public sealed record IrFxShaderBinding
{
    [JsonPropertyName("stage")]
    public string Stage { get; init; } = string.Empty;

    [JsonPropertyName("profile")]
    public string? Profile { get; init; }

    [JsonPropertyName("entry")]
    public string? Entry { get; init; }

    [JsonPropertyName("entrySymbolId")]
    public int? EntrySymbolId { get; init; }
}

public sealed record IrFxStateAssignment
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("value")]
    public string? Value { get; init; }
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

    public static IrDiagnostic Error(string message, string? stage = "invariant")
    {
        return new IrDiagnostic
        {
            Message = message,
            Severity = "Error",
            Stage = stage ?? "invariant"
        };
    }
}
