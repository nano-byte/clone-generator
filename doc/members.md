# What gets copied

The generator looks at every property **and** field declared by the type — public or private — and applies these rules in order.

| #   | Condition                                                                 | Result                                                                       |
| --- | ------------------------------------------------------------------------- | ---------------------------------------------------------------------------- |
| 0   | `static`, `const`, indexer, write-only, `readonly` field, `[IgnoreClone]` | skipped                                                                      |
| 1   | `required` or `init`-only                                                 | set in the leaf object initializer                                           |
| 2   | get-only collection                                                       | contents copied into the existing instance, elements cloned if cloneable     |
| 3   | type is cloneable                                                         | `from.X?.Clone()`                                                            |
| 4   | settable collection                                                       | new instance, elements cloned if cloneable; falls back to a copy constructor |
| 5   | primitive, `string`, `enum`, value type, delegate, or registered shallow  | copied directly                                                              |
| 5a  | type parameter                                                            | classified by its constraints, see [below](#type-parameters)                 |
| 6   | `record`                                                                  | `from.X with {}`                                                             |
| 7   | anything else                                                             | copied by reference, with a warning                                          |

## Computed properties are skipped

A settable property is only copied if it holds state of its own, i.e. if it is auto-implemented. A property computed from other members is skipped.

```csharp
[Cloneable]
public partial class Contact
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }

    // Skipped
    public string? FullName
    {
        get => $"{FirstName} {LastName}";
        set => (FirstName, LastName) = Split(value);
    }
}
```

Manually implemented properties are skipped as well, but the private field behind them is copied directly, so no state is lost:

```csharp
private string? _city;
public string? City { get => _city; set => _city = value; }   // to._city = from._city;
```

## Collections

A member counts as a collection if its type implements `ICollection<T>`.

A get-only collection property is copied into the existing instance rather than replaced:

```csharp
public List<PhoneNumber> PhoneNumbers { get; } = [];
// foreach (var item in from.PhoneNumbers) to.PhoneNumbers.Add(item?.Clone()!);
```

A settable collection is tried three ways, in this order:

1. **Public parameterless constructor**: a fresh instance, then the same add-loop as above.
2. **Constructor taking `IEnumerable<T>`**: `to.Tags = new TagSet(from.Tags);`
3. **Neither**: copied by reference, with a warning.

Note that only the first of these deep-copies the *elements*; a copy constructor produces a new collection over the same items.

A settable type that is merely `IEnumerable<T>` rather than `ICollection<T>` — `Queue<T>` and `Stack<T>`, which have no `Add` to loop over — also takes the copy-constructor route:

```csharp
public Queue<Vector2> PathNodes { get; private set; } = new();
// to.PathNodes = new Queue<Vector2>(from.PathNodes);
```

A nullable one is guarded, because passing `null` to a copy constructor throws. A **get-only** member of such a type cannot be filled at all, so it is skipped and the clone keeps whatever its initializer produced; make it settable to have it copied.

Elements are only cloned when their type is cloneable. Strings and other [immutable types](#immutable-types) are copied as-is.

A null check is only emitted when the member's type is annotated nullable; a get-only `List<T>` is assumed to be non-null, while `List<T>?` is guarded.

Note that the collection is copied, but the object graph is not deduplicated. Two members referencing the same list produce two independent copies, and an element appearing twice in a collection is cloned twice.

## Type parameters

A member typed as a type parameter is classified by what its constraints promise, because the type parameter itself says nothing:

| Constraint                            | Result                                                     |
| ------------------------------------- | ---------------------------------------------------------- |
| `where T : struct`                    | copied directly                                            |
| a cloneable type                      | `from.X?.Clone()`                                          |
| anything else, or no constraint       | copied by reference, with a warning                        |

Where the constraint is the root of a [curiously recurring hierarchy](inheritance.md), its `Clone()` already returns `T`, so the value is used as-is:

```csharp
[Cloneable]
public abstract partial class EntityBase<TTemplate>
    where TTemplate : EntityTemplateBase<TTemplate>
{
    private TTemplate? _template;   // to._template = from._template?.Clone();
}
```

Otherwise `Clone()` returns the constraint rather than `T`, so the result is cast back down:

```csharp
[Cloneable]
public partial class Holder<T> where T : Item
{
    public T? Value { get; set; }   // to.Value = (T)from.Value?.Clone()!;
}
```

That cast assumes the hierarchy overrides `Clone()` to return the concrete type, which generated ones always do. It is only emitted where `T` is known to be a reference type.

## Immutable types

These are always copied by reference, without a warning:

- value types and `enum`s
- `string`
- delegates
- `System.Type`, `System.Uri`, `System.Version`
- anything registered with `[assembly: CloneShallow(typeof(T))]`

Everything else that is not cloneable falls through to rule 7. The warning there is limited to types from your own assembly.
