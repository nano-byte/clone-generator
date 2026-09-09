# Inheritance

## The `CloneFromTo` chain

Every `[Cloneable]` type contributes a helper that copies **only the members it declares** and chains to its base:

```csharp
protected static void CloneFromTo(PhoneNumber from, PhoneNumber to)
{
    ContactMethod.CloneFromTo(from, to);
    to.CountryCode = from.CountryCode;
    to.LocalNumber = from.LocalNumber;
}
```

Every concrete type additionally gets a factory that news up the instance and runs the chain:

```csharp
public MobileNumber CloneMobileNumber()
{
    var from = this;
    var to = new MobileNumber {Carrier = from.Carrier};
    CloneFromTo(from, to);
    return to;
}

public override PhoneNumber Clone() => CloneMobileNumber();
```

Each helper can copy `private` and `protected` state of the type that declares it. Where a base class provides no helper of its own, the nearest `[Cloneable]` descendant additionally copies that base's accessible members as part of its own helper (see [below](#absorbing-plain-base-classes)).

`required` and `init`-only members are collected from the **whole** inheritance chain and set in the leaf's object initializer.

## Which type owns the `Clone()` name

The **clone root** is the topmost ancestor that declares a [clone interface](interfaces.md) or a `Clone()` method of its own.

- The root gets `Clone()`, returning the root type.
- Everything below it gets `Clone` + its own type name, plus an `override` of `Clone()`.

```csharp
[Cloneable] public abstract partial class ContactMethod {}
[Cloneable] public abstract partial class PhoneNumber : ContactMethod, ICloneable<PhoneNumber> {}
[Cloneable] public partial class MobileNumber : PhoneNumber {}
```

| Type            | Generated                                                                                       |
| --------------- | ----------------------------------------------------------------------------------------------- |
| `ContactMethod` | `CloneFromTo` only                                                                              |
| `PhoneNumber`   | `public abstract PhoneNumber Clone();` + `CloneFromTo`                                          |
| `MobileNumber`  | `CloneMobileNumber()`, `public override PhoneNumber Clone() => CloneMobileNumber();` + `CloneFromTo` |

A type that is `[Cloneable]` but declares **no** clone interface and no `Clone()` of its own — `ContactMethod` above — only contributes a helper, leaving the `Clone()` name to its subclasses. Such a type cannot be cloned polymorphically: a member typed `ContactMethod` is copied by reference, with a warning. Declare a clone interface on the base wherever you need that.

Use [`[Cloneable(MethodName = "...")]`](attributes.md#methodname) where the default name does not match an existing public method you want to keep.

## Virtual by default

The generated root `Clone()` is `virtual` unless the class is `sealed`.

## Absorbing plain base classes

A base class does not have to be `[Cloneable]`. If it provides no `CloneFromTo` helper of its own — a plain POCO base, or a base from another assembly — the nearest `[Cloneable]` descendant copies that base's members as part of its own `CloneFromTo`, walking up until it reaches a base that *does* provide a helper (which it then chains to as before).

The same deep-copy rules apply to absorbed members as to declared ones.

Annotating the base with `[Cloneable]` anyway is still preferable: only the base's own helper can reach its `private` state. When absorption cannot reach a member — a `private` field, or a cross-assembly `internal` one — that member's state is lost and **CLONE004** warns about it. Annotate the base with `[Cloneable]`, or put `[IgnoreClone]` on the member to silence the warning.
