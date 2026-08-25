using System.Text;

namespace NanoByte.CloneGenerator;

/// <summary>
/// Generates deep <c>Clone()</c> methods for types annotated with <c>[Cloneable]</c>.
/// </summary>
[Generator(LanguageNames.CSharp)]
public class CloneSourceGenerator : IIncrementalGenerator
{
    /// <summary>The name of the analysis step, so that tests can assert it is properly cached.</summary>
    public const string TrackingName = "CloneSpecs";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static ctx
            => ctx.AddSource(AttributeSource.HintName, AttributeSource.Text));

        // Types registered as safe to copy by reference, e.g. [assembly: CloneShallow(typeof(Uri))]
        var shallowTypes = context.CompilationProvider.Select(static (compilation, _)
            => compilation.Assembly
                          .GetAttributes()
                          .Where(x => x.AttributeClass?.ToDisplayString() == AttributeSource.CloneShallowAttribute)
                          .Select(x => x.ConstructorArguments.FirstOrDefault().Value)
                          .OfType<ITypeSymbol>()
                          .Select(x => x.OriginalDefinition.ToDisplayString())
                          .ToEquatableArray());

        // C# 8 introduced nullable reference types along with the '!' suppression operator
        var supportsNullable = context.ParseOptionsProvider.Select(static (options, _)
            => options is CSharpParseOptions {LanguageVersion: >= LanguageVersion.CSharp8});

        var candidates = context.SyntaxProvider.ForAttributeWithMetadataName(
            AttributeSource.CloneableAttribute,
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol);

        var results = candidates
            .Combine(shallowTypes)
            .Combine(supportsNullable)
            .Select(static (input, cancellationToken) =>
            {
                var ((type, shallow), nullable) = input;
                return (Result: new Parser(shallow, nullable).Parse(type, cancellationToken), Nullable: nullable);
            })
            .WithTrackingName(TrackingName);

        context.RegisterSourceOutput(results, static (ctx, item) =>
        {
            foreach (var diagnostic in item.Result.Diagnostics)
                ctx.ReportDiagnostic(diagnostic.ToDiagnostic());

            if (item.Result.Spec is {} spec)
                ctx.AddSource(HintName(spec.HintName), Emitter.Emit(spec, item.Nullable));
        });
    }

    /// <summary>
    /// Turns a fully qualified type name into a file name that is unique and legal.
    /// </summary>
    private static string HintName(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (char c in name)
            builder.Append(char.IsLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_');
        return builder.ToString();
    }
}
