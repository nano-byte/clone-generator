using System.Collections;

namespace NanoByte.CloneGenerator;

/// <summary>
/// An immutable array with structural equality, so that it can safely take part in the incremental generator caching pipeline.
/// </summary>
internal readonly struct EquatableArray<T>(T[] items) : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
    where T : IEquatable<T>
{
    private readonly T[]? _items = items;

    public static readonly EquatableArray<T> Empty = new([]);

    private T[] Items => _items ?? [];

    public int Count => Items.Length;

    public T this[int index] => Items[index];

    public bool Equals(EquatableArray<T> other)
    {
        var items = Items;
        var otherItems = other.Items;
        if (items.Length != otherItems.Length) return false;

        for (int i = 0; i < items.Length; i++)
        {
            if (!items[i].Equals(otherItems[i])) return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        int hash = 17;
        foreach (var item in Items)
            unchecked { hash = hash * 31 + item.GetHashCode(); }
        return hash;
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => Items.GetEnumerator();
}

internal static class EquatableArrayExtensions
{
    public static EquatableArray<T> ToEquatableArray<T>(this IEnumerable<T> source)
        where T : IEquatable<T>
        => new([..source]);
}
