namespace NanoByte.CloneGenerator.Specs;

/// <summary>
/// A diagnostic, reduced to values so that it can take part in incremental caching.
/// </summary>
internal readonly record struct DiagnosticSpec(string Id, LocationSpec? Location, EquatableArray<string> MessageArgs)
{
    public static DiagnosticSpec Create(DiagnosticDescriptor descriptor, ISymbol? symbol, params string[] messageArgs)
        => new(descriptor.Id, symbol == null ? null : LocationSpec.From(symbol), new(messageArgs));

    public Diagnostic ToDiagnostic()
        => Diagnostic.Create(
            DiagnosticCatalog.ById[Id],
            Location?.ToLocation(),
            [..MessageArgs]);
}
