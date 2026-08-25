namespace NanoByte.CloneGenerator.Specs;

/// <summary>
/// A source location, reduced to values so that it can take part in incremental caching.
/// </summary>
internal readonly record struct LocationSpec(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
{
    public static LocationSpec? From(ISymbol symbol)
        => From(symbol.Locations.FirstOrDefault(x => x.IsInSource));

    public static LocationSpec? From(Location? location)
        => location is {IsInSource: true}
            ? new(location.SourceTree!.FilePath, location.SourceSpan, location.GetLineSpan().Span)
            : null;

    public Location ToLocation()
        => Location.Create(FilePath, TextSpan, LineSpan);
}
