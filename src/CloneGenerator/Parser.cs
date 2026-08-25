using System.Collections.Immutable;
using NanoByte.CloneGenerator.Specs;

namespace NanoByte.CloneGenerator;

/// <summary>
/// Turns a <c>[Cloneable]</c>-annotated symbol into a <see cref="CloneTypeSpec"/> plus diagnostics.
/// </summary>
/// <remarks>Everything symbol-related happens here, so that the emitter works purely on values.</remarks>
internal sealed class Parser(IReadOnlyCollection<string> shallowTypes, bool supportsNullRefSuppression)
{
    private readonly List<DiagnosticSpec> _diagnostics = [];
    private IAssemblySymbol? _assembly;

    public ParseResult Parse(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        _assembly = type.ContainingAssembly;

        if (type.ContainingType != null)
            return Fail(Diagnostics.Unsupported, type, type.Name, "it is a nested type");
        if (type.TypeKind != TypeKind.Class)
            return Fail(Diagnostics.Unsupported, type, type.Name, "it is not a class; value types cannot use the CloneFromTo pattern");
        if (!IsPartial(type, cancellationToken))
            return Fail(Diagnostics.NotPartial, type, type.Name);

        var root = type.SelfAndBaseTypes().LastOrDefault(x => x.HasCloneContract()) ?? type;
        bool isRoot = SymbolEqualityComparer.Default.Equals(root, type);
        string methodName = type.CloneMethodName();

        // Back off wherever the user has written the code by hand
        bool handWrittenClone = type.FindDeclaredCloneMethod() != null
                             || type.GetMembers(methodName).OfType<IMethodSymbol>().Any(x => x is {IsStatic: false, Parameters.Length: 0});
        if (handWrittenClone)
            Report(Diagnostics.HandWritten, type, type.Name, $"{methodName}()");

        bool handWrittenCloneFromTo = type.HasDeclaredCloneFromTo();
        if (handWrittenCloneFromTo)
            Report(Diagnostics.HandWritten, type, type.Name, "CloneFromTo()");

        string? baseCloneFromTo = ResolveBaseCloneFromTo(type);

        var declaredMembers = CopyableMembers(type).ToList();
        var members = new List<MemberSpec>();
        foreach (var member in declaredMembers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (member.IsRequiredOrInit()) continue; // set by the leaf's object initializer instead

            if (BuildStatement(member) is {} statement)
                members.Add(new(member.Name, statement));
        }

        bool emitCloneMethod = !type.IsAbstract && !handWrittenClone;
        bool emitAbstractClone = isRoot && type.IsAbstract && !handWrittenClone && type.HasCloneContract();

        var initializerMembers = new List<MemberSpec>();
        if (emitCloneMethod)
        {
            if (!type.InstanceConstructors.Any(x => x.Parameters.Length == 0))
                return Fail(Diagnostics.NotConstructible, type, type.Name, "it has no parameterless constructor");

            foreach (var member in type.SelfAndBaseTypes().Reverse().SelectMany(RequiredOrInitMembers))
            {
                if (!member.IsAccessibleFrom(type))
                    return Fail(Diagnostics.NotConstructible, type, type.Name, $"the required member '{member.Name}' is not accessible");

                if (BuildExpression(member) is {} expression)
                    initializerMembers.Add(new(member.Name, expression));
            }
        }

        return new(new CloneTypeSpec(
            HintName: HintName(type),
            Namespace: type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString(),
            TypeDeclarations: new([TypeDeclaration(type)]),
            DocName: DocName(type),
            QualifiedName: type.Qualified(),
            IsSealed: type.IsSealed,
            BaseCloneFromTo: baseCloneFromTo,
            CloneMethodName: methodName,
            CloneReturnType: type.CloneContractReturnType().Qualified(),
            OverrideReturnType: emitCloneMethod && !isRoot && IsRootCloneOverridable(root) ? root.CloneContractReturnType().Qualified() : null,
            EmitAbstractClone: emitAbstractClone,
            ExplicitGenericInterface: emitCloneMethod && methodName != "Clone" ? type.DeclaredGenericCloneInterface()?.Qualified() : null,
            // An abstract root needs the bridge just as much as a concrete type: nothing below it declares
            // the interface, so there would be no implementation of it anywhere
            EmitSystemCloneable: (emitCloneMethod || emitAbstractClone)
                              && type.DeclaresSystemCloneable()
                              && !type.HasDeclaredSystemCloneableBridge(),
            EmitCloneFromTo: !handWrittenCloneFromTo,
            EmitCloneMethod: emitCloneMethod,
            InterfaceBridges: BuildInterfaceBridges(type, root, methodName, emitCloneMethod).ToEquatableArray(),
            Members: members.ToEquatableArray(),
            InitializerMembers: initializerMembers.ToEquatableArray()),
            _diagnostics.ToEquatableArray());
    }

    /// <summary>
    /// Implements <c>ICloneable&lt;TOther&gt;</c> contracts that this type picks up from an interface it
    /// declares, e.g. <c>IRecipeStep : ICloneable&lt;IRecipeStep&gt;</c>. The generated method returns the
    /// concrete type, which does not satisfy such an interface on its own.
    /// </summary>
    private static IEnumerable<string> BuildInterfaceBridges(INamedTypeSymbol type, INamedTypeSymbol root, string methodName, bool emitCloneMethod)
    {
        var inherited = type.BaseType?.AllInterfaces ?? ImmutableArray<INamedTypeSymbol>.Empty;

        // What the method we delegate to actually returns
        var returnType = emitCloneMethod ? type : root;
        string call = emitCloneMethod ? $"{methodName}()" : "Clone()";

        foreach (var candidate in type.Interfaces.SelectMany(x => x.AllInterfaces.Concat([x])).Distinct(SymbolEqualityComparer.Default).OfType<INamedTypeSymbol>())
        {
            if (candidate is not {Name: "ICloneable", TypeArguments.Length: 1}) continue;
            if (candidate.TypeArguments[0] is not INamedTypeSymbol target) continue;

            // Handled as an implicit or explicit implementation of the type's own contract
            if (SymbolEqualityComparer.Default.Equals(target, type)) continue;

            // Already satisfied further up the hierarchy
            if (inherited.Contains(candidate, SymbolEqualityComparer.Default)) continue;

            // The user wrote the bridge by hand
            if (type.GetMembers().OfType<IMethodSymbol>().Any(x => x.ExplicitInterfaceImplementations.Any(e => SymbolEqualityComparer.Default.Equals(e.ContainingType, candidate)))) continue;

            bool convertible = SymbolEqualityComparer.Default.Equals(returnType, target)
                            || returnType.AllInterfaces.Contains(target, SymbolEqualityComparer.Default)
                            || returnType.BaseTypes().Contains(target, SymbolEqualityComparer.Default);
            string cast = convertible ? "" : $"({target.Qualified()})";

            yield return $"{target.Qualified()} {candidate.Qualified()}.Clone() => {cast}{call};";
        }
    }

    /// <summary>
    /// Whether the root's <c>Clone()</c> can be overridden. Generated roots are abstract or virtual;
    /// hand-written ones have to say so themselves.
    /// </summary>
    private static bool IsRootCloneOverridable(INamedTypeSymbol root)
        => root.FindDeclaredCloneMethod() is {} declared
            ? declared.IsAbstract || declared.IsVirtual || declared.IsOverride
            : root.IsCloneable();

    private string? ResolveBaseCloneFromTo(INamedTypeSymbol type)
    {
        if (type.BaseType is not {SpecialType: not SpecialType.System_Object} baseType) return null;

        if (baseType.IsCloneable() || baseType.HasDeclaredCloneFromTo())
            return baseType.Qualified();

        // The base holds state we would silently drop
        if (CopyableMembers(baseType).Any() || baseType.BaseTypes().Any(x => CopyableMembers(x).Any()))
            Report(Diagnostics.BaseNotCloneable, type, type.Name, baseType.Name);

        return null;
    }

    private static IEnumerable<ISymbol> CopyableMembers(INamedTypeSymbol type)
        => type.GetMembers()
               .Where(member => member switch
                {
                    // A settable property must hold state of its own; a computed one just projects
                    // other members, which are copied directly anyway
                    IPropertySymbol {IsStatic: false, IsIndexer: false, GetMethod: not null, SetMethod: not null} property
                        => property.IsAutoProperty(),
                    IPropertySymbol {IsStatic: false, IsIndexer: false, GetMethod: not null} => true,
                    IFieldSymbol {IsStatic: false, IsConst: false, IsImplicitlyDeclared: false, IsReadOnly: false} => true,
                    _ => false
                })
               .Where(member => !member.HasAttribute(AttributeSource.IgnoreCloneAttribute));

    /// <summary>
    /// Members the leaf's object initializer has to set, gathered across the whole inheritance chain.
    /// </summary>
    private static IEnumerable<ISymbol> RequiredOrInitMembers(INamedTypeSymbol type)
        => type.GetMembers()
               .Where(member => member is IPropertySymbol {IsStatic: false, IsIndexer: false} or IFieldSymbol {IsStatic: false})
               .Where(member => member.IsRequiredOrInit())
               .Where(member => !member.HasAttribute(AttributeSource.IgnoreCloneAttribute));

    /// <summary>Builds a complete statement assigning the member into <c>to</c>.</summary>
    private string? BuildStatement(ISymbol member)
    {
        var type = member.MemberType();
        string name = member.Name;
        string from = $"from.{name}";
        string to = $"to.{name}";

        if (IsShallow(member, type))
            return member.IsSettable() ? $"{to} = {from};" : null;

        // Get-only collections: copy the contents into the existing instance
        if (!member.IsSettable())
        {
            if (type.CollectionElementType() is not {} elementType) return null;
            return AddLoop(member, to, from, elementType, type.NullableAnnotation == NullableAnnotation.Annotated);
        }

        if (CloneCall(type, from) is {} cloneCall)
            return $"{to} = {cloneCall};";

        if (type.IsRecordClass())
            return $"{to} = {RecordCopy(type, from)};";

        if (type.CollectionElementType() is {} settableElement)
        {
            if (type.HasPublicParameterlessConstructor())
            {
                return $"{to} = new {type.Qualified()}();\n"
                     + AddLoop(member, to, from, settableElement, type.NullableAnnotation == NullableAnnotation.Annotated);
            }
            if (type.HasCopyConstructor(settableElement))
                return CopyConstructorCall(type, to, from);
        }

        // Not an ICollection<T>, so there is no Add() to loop over, but it can still be rebuilt from the
        // original's contents. This is what covers Queue<T> and Stack<T>.
        if (type.EnumerableElementType() is {} enumerableElement && type.HasCopyConstructor(enumerableElement))
            return CopyConstructorCall(type, to, from);

        ReportShallow(member, type);
        return $"{to} = {from};";
    }

    /// <summary>Builds a statement rebuilding a collection from the original's contents.</summary>
    /// <remarks>Only the collection itself is copied; the elements end up shared.</remarks>
    private static string CopyConstructorCall(ITypeSymbol type, string to, string from)
    {
        string copy = $"new {type.Qualified()}({from})";

        // Passing null to a copy constructor throws, so a nullable member has to be guarded
        return type.NullableAnnotation == NullableAnnotation.Annotated
            ? $"{to} = {from} == null ? null : {copy};"
            : $"{to} = {copy};";
    }

    /// <summary>Builds an expression for the leaf's object initializer.</summary>
    private string? BuildExpression(ISymbol member)
    {
        var type = member.MemberType();
        string from = $"from.{member.Name}";

        if (IsShallow(member, type)) return from;
        if (CloneCall(type, from) is {} cloneCall) return cloneCall;
        if (type.IsRecordClass()) return RecordCopy(type, from);

        ReportShallow(member, type);
        return from;
    }

    /// <summary>Builds an expression copying a record value, null-checked where the value may be null.</summary>
    private string RecordCopy(ITypeSymbol type, string from)
    {
        if (!type.MayBeNull()) return $"{from} with {{}}";

        // The null check makes the result nullable, which a non-annotated target does not accept
        string copy = $"{from} == null ? null : {from} with {{}}";
        return NullSuppression(type) is {Length: > 0} suppression ? $"({copy}){suppression}" : copy;
    }

    private string AddLoop(ISymbol member, string to, string from, ITypeSymbol elementType, bool guardNull)
    {
        string? clone = CloneCall(elementType, "item");

        // The elements end up shared, which is just as easy to miss as a shared member
        if (clone == null && !IsShallowSafe(elementType))
            ReportShallow(member, elementType);

        string loop = $"foreach (var item in {from}) {to}.Add({clone ?? "item"});";
        return guardNull ? $"if ({from} != null) {loop}" : loop;
    }

    /// <summary>Resolves how to deep-copy a value of the given type, or <c>null</c> if it is not cloneable.</summary>
    private string? CloneCall(ITypeSymbol type, string expression)
    {
        if (type.IsInherentlyShallowSafe() || shallowTypes.Contains(type.OriginalDefinition.ToDisplayString())) return null;

        if (type is ITypeParameterSymbol parameter) return TypeParameterCloneCall(parameter, expression);
        if (type is not INamedTypeSymbol named) return null;

        if (ResolveCloneMethod(named) is {} methodName)
        {
            // '?.' keeps this null-safe even where the annotation claims otherwise, which is why the
            // suppression is needed: the result is nullable but the target may not be annotated.
            return named.MayBeNull()
                ? $"{expression}?.{methodName}(){NullSuppression(type)}"
                : $"{expression}.{methodName}()";
        }

        return CastCloneCall(named, expression);
    }

    /// <summary>
    /// Resolves how to deep-copy a value whose type is a type parameter, by looking at what its constraints promise.
    /// </summary>
    /// <remarks>Without this a type parameter would be copied by reference, silently dropping the deep copy of whatever it is constrained to.</remarks>
    private string? TypeParameterCloneCall(ITypeParameterSymbol parameter, string expression)
    {
        foreach (var constraint in parameter.ConstraintTypes.OfType<INamedTypeSymbol>().SelectMany(x => x.SelfAndBaseTypes()))
        {
            if (ResolveCloneMethod(constraint) is not {} methodName) continue;

            // '?.' because a type parameter that reaches this point is never a value type
            string call = $"{expression}?.{methodName}()";

            // A curiously recurring hierarchy already returns the type parameter itself
            if (SymbolEqualityComparer.Default.Equals(constraint.CloneContractReturnType(), parameter))
                return $"{call}{NullSuppression(parameter)}";

            // Otherwise the method returns the constraint, so the result has to come back down. That is
            // only sound where the type parameter is known to be a reference type, and it relies on the
            // hierarchy overriding Clone() to return the concrete type, which generated ones always do.
            if (!parameter.IsReferenceType) return null;

            // Always suppress here: casting the result of '?.' to the type parameter is a possible null
            // conversion regardless of how the target is annotated
            return $"({parameter.Qualified()}){call}{(supportsNullRefSuppression ? "!" : "")}";
        }

        return null;
    }

    private static string? ResolveCloneMethod(INamedTypeSymbol type)
    {
        // A type we generate for: predict the name we are going to give it. An abstract type only
        // gets a method if it declares a clone contract; without one there is nothing to call.
        if (type.IsCloneable() && (!type.IsAbstract || type.HasCloneContract()))
            return type.CloneMethodName();

        // A hand-written Clone() returning the same type
        if (type.FindDeclaredCloneMethod() is {DeclaredAccessibility: Accessibility.Public} declared
         && SymbolEqualityComparer.Default.Equals(declared.ReturnType, type))
            return declared.Name;

        // A hand-written, differently named strongly typed method such as CloneRunner()
        return type.GetMembers()
                   .OfType<IMethodSymbol>()
                   .FirstOrDefault(x => x is {IsStatic: false, Parameters.Length: 0, DeclaredAccessibility: Accessibility.Public, MethodKind: MethodKind.Ordinary}
                                     && x.Name.StartsWith("Clone", StringComparison.Ordinal)
                                     && SymbolEqualityComparer.Default.Equals(x.ReturnType, type))
                   ?.Name;
    }

    /// <summary>Falls back to an interface cast when the clone method is implemented explicitly.</summary>
    private string? CastCloneCall(INamedTypeSymbol type, string expression)
    {
        // ICloneable<T> is an explicit, unambiguously deep contract, so it is safe to use across assemblies
        if (type.AllInterfaces.FirstOrDefault(x => x.IsGenericCloneInterface(type)) is {} generic)
            return $"(({generic.Qualified()}){expression}).Clone()";

        // System.ICloneable does not specify whether it copies deeply, and half the BCL implements it.
        // Only honour it for types from this assembly, where declaring it was a deliberate choice.
        if (SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, _assembly)
         && type.AllInterfaces.Any(x => x.ToDisplayString() == "System.ICloneable"))
            return $"({type.Qualified()})((global::System.ICloneable){expression}).Clone()";

        return null;
    }

    private string NullSuppression(ITypeSymbol type)
        => supportsNullRefSuppression && type.NullableAnnotation != NullableAnnotation.Annotated ? "!" : "";

    private bool IsShallow(ISymbol member, ITypeSymbol type)
        => member.HasAttribute(AttributeSource.ShallowCloneAttribute)
        || IsShallowSafe(type);

    private bool IsShallowSafe(ITypeSymbol type)
        => type.IsInherentlyShallowSafe()
        || shallowTypes.Contains(type.OriginalDefinition.ToDisplayString());

    private void ReportShallow(ISymbol member, ITypeSymbol type)
    {
        // Only complain about types from this assembly; external ones are usually immutable value objects
        if (SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, member.ContainingAssembly))
            Report(Diagnostics.ShallowCopy, member, member.Name, type.Name);
    }

    private static bool IsPartial(INamedTypeSymbol type, CancellationToken cancellationToken)
        => type.DeclaringSyntaxReferences
               .Select(x => x.GetSyntax(cancellationToken))
               .OfType<TypeDeclarationSyntax>()
               .Any(x => x.Modifiers.Any(SyntaxKind.PartialKeyword));

    /// <summary>
    /// A file name for the generated source. Type parameters are reduced to the arity, because angle brackets are not valid in a path.
    /// </summary>
    private static string HintName(INamedTypeSymbol type)
    {
        string prefix = type.ContainingNamespace.IsGlobalNamespace ? "" : $"{type.ContainingNamespace.ToDisplayString()}.";
        string arity = type.Arity == 0 ? "" : $"_{type.Arity}";
        return $"{prefix}{type.Name}{arity}.Clone.g.cs";
    }

    /// <summary>The name to use in an XML doc <c>cref</c>, where type parameters are written in braces.</summary>
    private static string DocName(INamedTypeSymbol type)
        => type.Arity == 0
            ? type.Name
            : $"{type.Name}{{{string.Join(", ", type.TypeParameters.Select(x => x.Name))}}}";

    private static string TypeDeclaration(INamedTypeSymbol type)
    {
        // Constraints may be omitted from a partial declaration, but the parameter names have to match
        string name = type.Arity == 0
            ? type.Name
            : $"{type.Name}<{string.Join(", ", type.TypeParameters.Select(x => x.Name))}>";

        return type switch
        {
            {IsRecord: true, TypeKind: TypeKind.Struct} => $"partial record struct {name}",
            {IsRecord: true} => $"partial record {name}",
            {TypeKind: TypeKind.Struct} => $"partial struct {name}",
            _ => $"partial class {name}"
        };
    }

    private void Report(DiagnosticDescriptor descriptor, ISymbol symbol, params string[] messageArgs)
        => _diagnostics.Add(DiagnosticSpec.Create(descriptor, symbol, messageArgs));

    private ParseResult Fail(DiagnosticDescriptor descriptor, ISymbol symbol, params string[] messageArgs)
    {
        Report(descriptor, symbol, messageArgs);
        return new(null, _diagnostics.ToEquatableArray());
    }
}
