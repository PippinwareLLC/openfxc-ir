namespace OpenFXC.Ir;

public sealed record LoweringRequest(string SemanticJson, string? Profile, string? Entry)
{
    public string EntryOrDefault => string.IsNullOrWhiteSpace(Entry) ? "main" : Entry!;
}
