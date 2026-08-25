# Clone interfaces

The generator adapts to whichever cloning contract your type already declares. You do not have to declare one at all.

| Your type declares | Generated |
|---|---|
| nothing | `public virtual T Clone()` |
| `ICloneable<T>` | `public virtual T Clone()`, implicitly implementing it |
| `System.ICloneable` | plus `object System.ICloneable.Clone()` as an explicit implementation |
| both | both, unambiguously |

## `ICloneable<T>`

`ICloneable<T>` is matched *structurally*: any interface named `ICloneable` with one type parameter and a `T Clone()` method, regardless of namespace. That means this package works with your own definition, or one from a library such as `NanoByte.Common`, without referencing it:

```csharp
namespace NanoByte.Common;

public interface ICloneable<out T>
{
    T Clone();
}
```

When the type is the [clone root](inheritance.md#which-type-owns-the-clone-name), the public `Clone()` satisfies the interface implicitly. When it is not, the generated method has a different name, so an explicit implementation is added:

```csharp
MobileNumber ICloneable<MobileNumber>.Clone() => CloneMobileNumber();
```

## Contracts inherited from an interface

An interface can carry the contract on behalf of its implementers:

```csharp
public interface IContactContainer : ICloneable<IContactContainer>
{
    List<Contact> Contacts { get; }
}

[Cloneable]
public partial class Group : IContactContainer
{
    public string? Name { get; set; }
    public List<Contact> Contacts { get; } = [];
}
```

`Group.Clone()` returns `Group`, which does not satisfy `ICloneable<IContactContainer>` on its own, so a bridge is generated:

```csharp
IContactContainer ICloneable<IContactContainer>.Clone() => Clone();
```

The bridge is emitted on the type that introduces the interface; derived types inherit it.

## `System.ICloneable`

Matched by name, and bridged with an **explicit** implementation:

```csharp
object System.ICloneable.Clone() => Clone();
```

Because it is explicit, an `object`-returning `Clone` stays off your public surface, even when a type carries both interfaces at once.

A member is only treated as cloneable via `System.ICloneable` if **its type comes from your own assembly**. Members typed as BCL types that implement the interface (e.g., `string`, `Array`, `CultureInfo`) are not deep-copied.

`ICloneable<T>` is honoured across assembly boundaries.
