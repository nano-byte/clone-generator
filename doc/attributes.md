# Attributes

All attributes live in the `NanoByte.CloneGenerator` namespace and are available as soon as you reference the package. There is no runtime package to reference.

## `[Cloneable]`

Opts a class in. The class must be `partial`, otherwise you get a compiler error.

```csharp
[Cloneable]
public partial class Address
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
}
```

### `MethodName`

Only relevant when a base type already declares a `Clone()` method with a different return type. The generated method returning the concrete type is called `Clone` + the type name by default; set `MethodName` to override that.

```csharp
// Would be called CloneMobileNumber() by default
[Cloneable(MethodName = "CloneNumber")]
public sealed partial class MobileNumber : PhoneNumber
{}
```

Use this to keep an existing public API intact when replacing hand-written clone methods. See [Inheritance](inheritance.md) for how the default name is derived.

## `[IgnoreClone]`

Excludes a property or field. The clone keeps whatever value the member's initializer or constructor gives it.

```csharp
[Cloneable]
public partial class AddressBook
{
    public List<Contact> Contacts { get; } = [];

    /// <summary>Where this address book will be saved. Deliberately not inherited by clones.</summary>
    [IgnoreClone]
    internal string FilePath = GetDefaultPath();
}
```

## `[ShallowClone]`

Copies a member by reference even when its type is cloneable, so the clone shares the instance.

Use it where sharing is the point — heavyweight state, delegates, or a back-reference whose identity must be preserved:

```csharp
[Cloneable]
public partial class Contact
{
    public string? LastName { get; set; }

    /// <summary>The group this contact belongs to. Cloning it would detach the copy from the address book.</summary>
    [ShallowClone]
    public Group? Owner { get; set; }
}
```

## `[assembly: CloneShallow(typeof(T))]`

Registers a type as safe to copy by reference across the whole assembly, typically because it is immutable. Use this instead of putting `[ShallowClone]` on every member of that type.

```csharp
[assembly: CloneShallow(typeof(CountryCode))]
[assembly: CloneShallow(typeof(PostalCode))]
```

A good workflow is to convert your types first and let the compiler errors tell you which types to register, rather than trying to list them up front.
