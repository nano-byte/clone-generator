namespace NanoByte.CloneGenerator;

internal static class SymbolHelpers
{
    public static string Qualified(this ITypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    public static AttributeData? GetAttribute(this ISymbol symbol, string metadataName)
        => symbol.GetAttributes().FirstOrDefault(x => x.AttributeClass?.ToDisplayString() == metadataName);

    public static bool HasAttribute(this ISymbol symbol, string metadataName)
        => symbol.GetAttribute(metadataName) != null;

    public static bool IsCloneable(this ITypeSymbol type)
        => type.HasAttribute(AttributeSource.CloneableAttribute);

    /// <summary>
    /// Walks the base type chain, excluding <see cref="object"/>.
    /// </summary>
    public static IEnumerable<INamedTypeSymbol> BaseTypes(this INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is {SpecialType: not SpecialType.System_Object}; current = current.BaseType)
            yield return current;
    }

    /// <summary>
    /// The type and its base types, nearest first.
    /// </summary>
    public static IEnumerable<INamedTypeSymbol> SelfAndBaseTypes(this INamedTypeSymbol type)
    {
        yield return type;
        foreach (var baseType in type.BaseTypes()) yield return baseType;
    }

    /// <summary>
    /// Matches any <c>ICloneable&lt;T&gt;</c>-shaped interface, regardless of namespace, so that this package works with <c>NanoByte.Common.ICloneable&lt;T&gt;</c> without referencing it.
    /// </summary>
    public static bool IsGenericCloneInterface(this INamedTypeSymbol candidate, ITypeSymbol forType)
        => candidate is {Name: "ICloneable", TypeArguments.Length: 1}
        && SymbolEqualityComparer.Default.Equals(candidate.TypeArguments[0], forType)
        && candidate.GetMembers("Clone").OfType<IMethodSymbol>().Any(x => x.Parameters.Length == 0);

    /// <summary>
    /// Matches an <c>ICloneable&lt;TSelf&gt;</c> declared by the root of a curiously recurring hierarchy, where the type argument stands in for the eventual leaf rather than for the declaring type.
    /// </summary>
    /// <remarks>Only used to recognise the contract. Everything downstream reads the return type off the <em>constructed</em> interface, where Roslyn has already substituted the leaf.</remarks>
    private static bool IsSelfTypeCloneInterface(this INamedTypeSymbol candidate, INamedTypeSymbol forType)
        => candidate is {Name: "ICloneable", TypeArguments.Length: 1}
        && candidate.GetMembers("Clone").OfType<IMethodSymbol>().Any(x => x.Parameters.Length == 0)
        && candidate.TypeArguments[0] switch
        {
            // Seen from the definition: 'TSelf', constrained to the declaring type or something below it
            ITypeParameterSymbol parameter => parameter.ConstraintTypes
                                                       .OfType<INamedTypeSymbol>()
                                                       .SelectMany(x => x.SelfAndBaseTypes())
                                                       .Any(x => SymbolEqualityComparer.Default.Equals(x.OriginalDefinition, forType.OriginalDefinition)),
            // Seen from a leaf: already substituted, so the argument derives from the declaring type
            INamedTypeSymbol named => named.SelfAndBaseTypes().Contains(forType, SymbolEqualityComparer.Default),
            _ => false
        };

    /// <summary>The <c>ICloneable&lt;Self&gt;</c> interface declared directly on <paramref name="type"/>, if any.</summary>
    public static INamedTypeSymbol? DeclaredGenericCloneInterface(this INamedTypeSymbol type)
        => type.Interfaces.FirstOrDefault(x => x.IsGenericCloneInterface(type) || x.IsSelfTypeCloneInterface(type));

    /// <summary>
    /// What this type's clone contract returns. Usually the type itself, but the self-type in a curiously recurring hierarchy, and whatever a hand-written <c>Clone()</c> declares.
    /// </summary>
    public static ITypeSymbol CloneContractReturnType(this INamedTypeSymbol type)
        => type.DeclaredGenericCloneInterface()?.TypeArguments[0]
        ?? type.FindDeclaredCloneMethod()?.ReturnType
        ?? type;

    public static bool DeclaresSystemCloneable(this INamedTypeSymbol type)
        => type.Interfaces.Any(x => x.ToDisplayString() == "System.ICloneable");

    /// <summary>
    /// Whether the type already implements <c>System.ICloneable</c> explicitly by hand, so that generating the bridge would collide with it.
    /// </summary>
    public static bool HasDeclaredSystemCloneableBridge(this INamedTypeSymbol type)
        => type.GetMembers()
               .OfType<IMethodSymbol>()
               .Any(x => x.ExplicitInterfaceImplementations.Any(e => e.ContainingType.ToDisplayString() == "System.ICloneable"));

    /// <summary>
    /// Whether the type participates in a public cloning contract, as opposed to merely contributing a <c>CloneFromTo</c> helper.
    /// </summary>
    /// <remarks>Types like <c>TargetBase</c> are cloneable but declare no clone interface, so they must not claim the <c>Clone()</c> name away from their derived types.</remarks>
    public static bool HasCloneContract(this INamedTypeSymbol type)
        => type.DeclaredGenericCloneInterface() != null
        || type.DeclaresSystemCloneable()
        || type.FindDeclaredCloneMethod() != null;

    /// <summary>
    /// A hand-written parameterless instance method named <c>Clone</c> declared on this exact type.
    /// </summary>
    public static IMethodSymbol? FindDeclaredCloneMethod(this INamedTypeSymbol type)
        => type.GetMembers("Clone")
               .OfType<IMethodSymbol>()
               .FirstOrDefault(x => x is {IsStatic: false, Parameters.Length: 0, MethodKind: MethodKind.Ordinary});

    /// <summary>
    /// The topmost ancestor (or the type itself) that defines the cloning contract.
    /// That type owns the <c>Clone()</c> name; everything below it gets a differently named method plus an override.
    /// </summary>
    public static INamedTypeSymbol CloneRoot(this INamedTypeSymbol type)
        => type.SelfAndBaseTypes().LastOrDefault(x => x.HasCloneContract()) ?? type;

    /// <summary>The name of the generated method returning the concrete type.</summary>
    public static string CloneMethodName(this INamedTypeSymbol type)
    {
        if (type.GetAttribute(AttributeSource.CloneableAttribute)
                ?.NamedArguments.FirstOrDefault(x => x.Key == "MethodName").Value.Value is string {Length: > 0} custom)
            return custom;

        return SymbolEqualityComparer.Default.Equals(type.CloneRoot(), type)
            ? "Clone"
            : $"Clone{type.Name}";
    }

    /// <summary>
    /// Whether the type declares a hand-written <c>CloneFromTo</c> helper.
    /// </summary>
    public static bool HasDeclaredCloneFromTo(this INamedTypeSymbol type)
        => type.GetMembers("CloneFromTo")
               .OfType<IMethodSymbol>()
               .Any(x => x is {IsStatic: true, Parameters.Length: 2});

    /// <summary>
    /// The element type if the type is (or implements) <c>ICollection&lt;T&gt;</c>.
    /// </summary>
    public static ITypeSymbol? CollectionElementType(this ITypeSymbol type)
        => (type as INamedTypeSymbol)?.AllInterfaces
            .Concat(type is INamedTypeSymbol named ? [named] : Array.Empty<INamedTypeSymbol>())
            .FirstOrDefault(x => x.ConstructedFrom?.ToDisplayString() == "System.Collections.Generic.ICollection<T>")
            ?.TypeArguments[0];

    /// <summary>
    /// The element type if the type is (or implements) <c>IEnumerable&lt;T&gt;</c>.
    /// </summary>
    /// <remarks>Wider than <see cref="CollectionElementType"/>: it also matches types with no <c>Add</c> to fill, such as <c>Queue&lt;T&gt;</c>.</remarks>
    public static ITypeSymbol? EnumerableElementType(this ITypeSymbol type)
        => (type as INamedTypeSymbol)?.AllInterfaces
            .Concat(type is INamedTypeSymbol named ? [named] : Array.Empty<INamedTypeSymbol>())
            .FirstOrDefault(x => x.ConstructedFrom?.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>")
            ?.TypeArguments[0];

    /// <summary>
    /// Whether the type has an accessible constructor taking an <c>IEnumerable&lt;element&gt;</c>.
    /// </summary>
    public static bool HasCopyConstructor(this ITypeSymbol type, ITypeSymbol elementType)
        => type is INamedTypeSymbol named
        && named.InstanceConstructors.Any(ctor =>
               ctor.DeclaredAccessibility == Accessibility.Public
            && ctor.Parameters.Length == 1
            && ctor.Parameters[0].Type is INamedTypeSymbol {TypeArguments.Length: 1} p
            && p.ConstructedFrom.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>"
            && SymbolEqualityComparer.Default.Equals(p.TypeArguments[0], elementType));

    public static bool HasPublicParameterlessConstructor(this ITypeSymbol type)
        => type is INamedTypeSymbol named
        && named.InstanceConstructors.Any(x => x is {DeclaredAccessibility: Accessibility.Public, Parameters.Length: 0});

    /// <summary>Types that are always safe to copy by reference.</summary>
    /// <remarks>
    /// A type parameter is deliberately not on this list: whether it can be cloned depends on its
    /// constraints, so it is classified through those instead. A <c>struct</c>-constrained one is still
    /// covered here, because Roslyn reports it as a value type.
    /// </remarks>
    public static bool IsInherentlyShallowSafe(this ITypeSymbol type)
        => type.IsValueType
        || type.SpecialType == SpecialType.System_String
        || type.TypeKind is TypeKind.Enum or TypeKind.Delegate or TypeKind.Pointer
        || type.ToDisplayString() is "System.Type" or "System.Uri" or "System.Version" or "System.Enum" or "System.Delegate";

    /// <summary>
    /// Whether the property has a compiler-generated backing field, i.e. holds state of its own.
    /// </summary>
    /// <remarks>A property computed from other members must not be copied: writing it would just re-derive state that is already being copied directly, in an order-dependent way.</remarks>
    /// <remarks>Only meaningful for a type we have source for; metadata exposes no backing fields at all.</remarks>
    public static bool IsAutoProperty(this IPropertySymbol property)
        => property.ContainingType
                   .GetMembers()
                   .OfType<IFieldSymbol>()
                   .Any(x => SymbolEqualityComparer.Default.Equals(x.AssociatedSymbol, property));

    public static bool IsRecordClass(this ITypeSymbol type)
        => type is INamedTypeSymbol {IsRecord: true, IsReferenceType: true};

    public static bool IsRequiredOrInit(this ISymbol member)
        => member switch
        {
            IPropertySymbol property => property.IsRequired || property.SetMethod is {IsInitOnly: true},
            IFieldSymbol field => field.IsRequired,
            _ => false
        };

    public static ITypeSymbol MemberType(this ISymbol member)
        => member switch
        {
            IPropertySymbol property => property.Type,
            IFieldSymbol field => field.Type,
            _ => throw new ArgumentException("Not a property or field.", nameof(member))
        };

    public static bool IsSettable(this ISymbol member)
        => member switch
        {
            IPropertySymbol property => property.SetMethod != null,
            IFieldSymbol field => !field.IsReadOnly,
            _ => false
        };

    /// <summary>
    /// Whether generated code inside <paramref name="fromType"/> can read and write this member.
    /// </summary>
    public static bool IsAccessibleFrom(this ISymbol member, INamedTypeSymbol fromType)
    {
        if (SymbolEqualityComparer.Default.Equals(member.ContainingType, fromType)) return true;

        return member.DeclaredAccessibility switch
        {
            Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal => true,
            Accessibility.Internal or Accessibility.ProtectedAndInternal =>
                SymbolEqualityComparer.Default.Equals(member.ContainingAssembly, fromType.ContainingAssembly),
            _ => false
        };
    }

    /// <summary>
    /// Whether a null check is needed before dereferencing the member.
    /// </summary>
    public static bool MayBeNull(this ITypeSymbol type)
        => !type.IsValueType || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
}
