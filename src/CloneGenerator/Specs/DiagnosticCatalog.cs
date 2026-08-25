namespace NanoByte.CloneGenerator.Specs;

/// <summary>
/// Maps diagnostic IDs back to their descriptors after the caching boundary.
/// </summary>
internal static class DiagnosticCatalog
{
    public static readonly Dictionary<string, DiagnosticDescriptor> ById = new()
    {
        [Diagnostics.NotPartial.Id] = Diagnostics.NotPartial,
        [Diagnostics.NotConstructible.Id] = Diagnostics.NotConstructible,
        [Diagnostics.ShallowCopy.Id] = Diagnostics.ShallowCopy,
        [Diagnostics.BaseNotCloneable.Id] = Diagnostics.BaseNotCloneable,
        [Diagnostics.HandWritten.Id] = Diagnostics.HandWritten,
        [Diagnostics.Unsupported.Id] = Diagnostics.Unsupported
    };
}
