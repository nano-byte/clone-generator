# NanoByte Clone Generator

[![Build](https://github.com/nano-byte/clone-generator/actions/workflows/build.yml/badge.svg)](https://github.com/nano-byte/clone-generator/actions/workflows/build.yml)
[![NuGet](https://img.shields.io/nuget/v/NanoByte.CloneGenerator.svg)](https://www.nuget.org/packages/NanoByte.CloneGenerator/)
[![Documentation](https://img.shields.io/badge/api-docs-orange.svg)](https://clone-generator.nano-byte.net/)  
A Roslyn source generator for deep `Clone()` methods.

Annotate a `partial` class with `[Cloneable]` and the member-by-member copy is generated at compile time. No reflection, no runtime dependency, trim- and AOT-safe.

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

gives you:

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

Full documentation: **<https://clone-generator.nano-byte.net/>**

## Installation

```bash
dotnet add package NanoByte.CloneGenerator
```

The package is an analyzer, so mark it as a build-time-only dependency:

```xml
<PackageReference Include="NanoByte.CloneGenerator" PrivateAssets="All" />
```

## Clone interfaces

The generator adapts to whichever cloning contract your type already declares:

| Your type declares                                      | Generated                                                             |
| ------------------------------------------------------- | --------------------------------------------------------------------- |
| nothing                                                 | `public virtual T Clone()`                                            |
| `ICloneable<T>` (any namespace, e.g. `NanoByte.Common`) | `public virtual T Clone()`, implicitly implementing it                |
| `System.ICloneable`                                     | plus `object System.ICloneable.Clone()` as an explicit implementation |
| both                                                    | both                                                                  |

`ICloneable<T>` is matched *structurally*: any interface named `ICloneable` with one type parameter and a `T Clone()` method, regardless of namespace.

## What gets copied

Rules are applied in order, per property **and** field:

| Condition                                                                 | Result                                                                       |
| ------------------------------------------------------------------------- | ---------------------------------------------------------------------------- |
| `static`, `const`, indexer, write-only, `readonly` field, `[IgnoreClone]` | skipped                                                                      |
| `required` or `init`-only                                                 | set in the leaf object initializer                                           |
| get-only collection                                                       | contents copied into the existing instance, elements cloned if cloneable     |
| type is cloneable                                                         | `from.X?.Clone()`                                                            |
| settable collection                                                       | new instance, elements cloned if cloneable; falls back to a copy constructor |
| primitive, `string`, `enum`, value type, registered shallow               | copied directly                                                              |
| type parameter                                                            | classified by its constraints                                                |
| `record`                                                                  | `from.X with {}`                                                             |
| anything else                                                             | copied by reference, with a warning                                          |

## Attributes

| Attribute                             | Target         | Meaning                                                                    |
| ------------------------------------- | -------------- | -------------------------------------------------------------------------- |
| `[Cloneable]`                         | class          | Opt in. `MethodName` overrides the generated method name.                  |
| `[IgnoreClone]`                       | property/field | Exclude; the clone keeps the default value.                                |
| `[ShallowClone]`                      | property/field | Copy by reference even if the type is cloneable.                           |
| `[assembly: CloneShallow(typeof(T))]` | assembly       | Register an immutable type once, instead of annotating every member of it. |

## Limitations

- Classes only.
- No nested types.
- No cycle detection.
- Back-references are deep-copied like anything else, which detaches them from the new graph. Use `[ShallowClone]` where you need identity preserved.

## Building

The source code is in [`src/`](src/), config for building the API documentation is in [`doc/`](doc/) and generated build artifacts are placed in `artifacts/`. The source code does not contain version numbers. Instead the version is determined during CI using [GitVersion](https://gitversion.net/).

To build run `.\build.ps1` or `./build.sh` (.NET SDK is automatically downloaded if missing using [0install](https://0install.net/)).

## Contributing

We welcome contributions to this project such as bug reports, recommendations and pull requests.

This repository contains an [EditorConfig](http://editorconfig.org/) file. Please make sure to use an editor that supports it to ensure consistent code style, file encoding, etc.. For full tooling support for all style and naming conventions consider using JetBrains' [ReSharper](https://www.jetbrains.com/resharper/) or [Rider](https://www.jetbrains.com/rider/) products.
