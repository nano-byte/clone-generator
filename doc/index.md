---
title: Home
---

# NanoByte Clone Generator

A [Roslyn source generator](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview) for deep `Clone()` methods.

Annotate a `partial` class with `[Cloneable]` and the member-by-member copy is generated at compile time. No reflection, no runtime dependency, trim- and AOT-safe.

## Usage

Add a reference to the [NanoByte.CloneGenerator](https://www.nuget.org/packages/NanoByte.CloneGenerator/) NuGet package to your project. It is an analyzer, so mark it as a build-time-only dependency:

```xml
<PackageReference Include="NanoByte.CloneGenerator" PrivateAssets="All" />
```

You can then make a class cloneable like this:

```csharp
using NanoByte.CloneGenerator;

[Cloneable]
public partial class Contact
{
    public required string LastName { get; set; }
    public string? FirstName { get; set; }
    public Address? WorkAddress { get; set; }
    public List<PhoneNumber> PhoneNumbers { get; } = [];
}
```

which generates:

```csharp
public virtual Contact Clone()
{
    var from = this;
    var to = new Contact {LastName = from.LastName};
    CloneFromTo(from, to);
    return to;
}

protected static void CloneFromTo(Contact from, Contact to)
{
    to.FirstName = from.FirstName;
    to.WorkAddress = from.WorkAddress?.Clone();
    foreach (var item in from.PhoneNumbers) to.PhoneNumbers.Add(item?.Clone()!);
}
```

## No runtime dependency

The [marker attributes](attributes.md) are `internal` to each compilation that uses this package. An assembly granted `InternalsVisibleTo` access to several such projects will therefore see several `CloneableAttribute` types.

## Where to go next

- [Attributes](attributes.md): opting in, and the escape hatches
- [What gets copied](members.md): the member classification rules
- [Inheritance](inheritance.md): how the `CloneFromTo` chain works
- [Clone interfaces](interfaces.md): `ICloneable<T>` and `System.ICloneable`

## Limitations

- Classes only. Value types are not supported.
- No nested types.
- No cycle detection. An object graph containing a cycle will recurse until the stack runs out.
- Back-references are deep-copied like anything else, which detaches them from the new graph. Use `[ShallowClone]` where you need identity preserved.
