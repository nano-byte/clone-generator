using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;

namespace NanoByte.CloneGenerator;

/// <summary>
/// Compiles source in memory, runs the generator over it and exposes the results.
/// </summary>
internal static class GeneratorHarness
{
    /// <summary>The reference assemblies of the runtime this test process is using.</summary>
    private static readonly MetadataReference[] References =
    [
        ..((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(x => !string.IsNullOrEmpty(x))
            .Select(x => MetadataReference.CreateFromFile(x))
    ];

    public static Result Run(params string[] sources)
        => Run(references: [], sources);

    /// <summary>
    /// Compiles <paramref name="referencedSource"/> into a separate assembly first, so that the generator sees its types as metadata rather than source.
    /// </summary>
    public static Result RunAcrossAssemblies(string referencedSource, params string[] sources)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var referenced = CSharpCompilation.Create(
            "Referenced",
            [CSharpSyntaxTree.ParseText(referencedSource, parseOptions)],
            References,
            new(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        // The referenced assembly uses the generator too, exactly as a real project would
        CSharpGeneratorDriver
            .Create([new CloneSourceGenerator().AsSourceGenerator()], parseOptions: parseOptions)
            .RunGeneratorsAndUpdateCompilation(referenced, out var withGenerated, out _);

        using var stream = new MemoryStream();
        var emit = withGenerated.Emit(stream);
        emit.Success.Should().BeTrue(because: string.Join("\n", emit.Diagnostics.Where(x => x.Severity == DiagnosticSeverity.Error)));

        return Run([MetadataReference.CreateFromImage(stream.ToArray())], sources);
    }

    private static Result Run(IReadOnlyList<MetadataReference> references, string[] sources)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var compilation = CSharpCompilation.Create(
            "GeneratorTests",
            sources.Select(x => CSharpSyntaxTree.ParseText(x, parseOptions)),
            [..References, ..references],
            new(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver
            .Create([new CloneSourceGenerator().AsSourceGenerator()], parseOptions: parseOptions)
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        var result = driver.GetRunResult().Results.Single();

        return new(
            Sources: result.GeneratedSources.ToDictionary(
                x => x.HintName,
                x => x.SourceText.ToString()),
            GeneratorDiagnostics: [..result.Diagnostics],
            CompilationDiagnostics: [..output.GetDiagnostics().Where(x => x.Severity >= DiagnosticSeverity.Warning)],
            Compilation: output);
    }

    /// <summary>Runs the generator and asserts that the result compiles without errors or warnings.</summary>
    public static Result RunValid(params string[] sources)
    {
        var result = Run(sources);
        result.CompilationDiagnostics
              .Should().BeEmpty(because: "the generated code should compile cleanly:\n"
                                       + string.Join("\n", result.CompilationDiagnostics)
                                       + $"\n\n{result.AllSources}");
        return result;
    }

    internal sealed record Result(
        IReadOnlyDictionary<string, string> Sources,
        IReadOnlyList<Diagnostic> GeneratorDiagnostics,
        IReadOnlyList<Diagnostic> CompilationDiagnostics,
        Compilation Compilation)
    {
        /// <summary>The generated source for one type, excluding the injected attributes. Generic types carry an arity suffix.</summary>
        public string SourceFor(string typeName)
            => Sources.Single(x => Regex.IsMatch(x.Key, $@"(^|\.){Regex.Escape(typeName)}(_\d+)?\.Clone\.g\.cs$")).Value;

        public string AllSources => string.Join("\n\n", Sources.Select(x => $"// === {x.Key} ===\n{x.Value}"));

        public IEnumerable<string> DiagnosticIds => GeneratorDiagnostics.Select(x => x.Id);

        /// <summary>Emits the compilation and loads it, so that the generated clone can actually be run.</summary>
        public Assembly Load()
        {
            using var stream = new MemoryStream();
            var emit = Compilation.Emit(stream);
            emit.Success.Should().BeTrue(because: string.Join("\n", emit.Diagnostics.Where(x => x.Severity == DiagnosticSeverity.Error)));
            return Assembly.Load(stream.ToArray());
        }
    }
}
