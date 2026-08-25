namespace NanoByte.CloneGenerator;

/// <summary>
/// The diagnostics reported by <see cref="CloneSourceGenerator"/>.
/// </summary>
internal static class Diagnostics
{
    private const string Category = "CloneGenerator";

    /// <summary>A type annotated with <c>[Cloneable]</c> is not <c>partial</c>.</summary>
    public static readonly DiagnosticDescriptor NotPartial = new(
        "CLONE001",
        "Cloneable type must be partial",
        "'{0}' is annotated with [Cloneable] but is not declared as 'partial', so no Clone() method can be generated",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>The type cannot be instantiated by the generated code.</summary>
    public static readonly DiagnosticDescriptor NotConstructible = new(
        "CLONE002",
        "Cloneable type cannot be constructed",
        "'{0}' cannot be cloned because {1}",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>A member is copied by reference although its type looks mutable.</summary>
    public static readonly DiagnosticDescriptor ShallowCopy = new(
        "CLONE003",
        "Member is copied by reference",
        "'{0}' is copied by reference because '{1}' offers no clone method, so the clone will share this instance. Give '{1}' a public Clone() or an ICloneable<{1}> interface, or silence this with [ShallowClone] or [assembly: CloneShallow(typeof({1}))].",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    /// <summary>The base type holds state that the generated code cannot reach.</summary>
    public static readonly DiagnosticDescriptor BaseNotCloneable = new(
        "CLONE004",
        "Base type is not cloneable",
        "'{0}' derives from '{1}', which holds copyable state but is not annotated with [Cloneable]; that state would be silently dropped",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>A hand-written implementation already exists, so the generator backs off.</summary>
    public static readonly DiagnosticDescriptor HandWritten = new(
        "CLONE005",
        "Using hand-written clone implementation",
        "'{0}' already declares '{1}', so the generator did not emit one",
        Category, DiagnosticSeverity.Info, isEnabledByDefault: true);

    /// <summary>The shape of the type is not supported.</summary>
    public static readonly DiagnosticDescriptor Unsupported = new(
        "CLONE006",
        "Unsupported cloneable type",
        "'{0}' cannot be cloned because {1}",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);
}
