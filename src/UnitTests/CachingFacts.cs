using Microsoft.CodeAnalysis.CSharp;

namespace NanoByte.CloneGenerator;

/// <summary>
/// Verifies that the generator's analysis is properly cached.
/// </summary>
public class CachingFacts
{
    [Fact]
    public void ReusesAnalysisAcrossUnrelatedEdits()
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var compilation = CSharpCompilation.Create(
            "CachingTests",
            [CSharpSyntaxTree.ParseText(
                """
                using NanoByte.CloneGenerator;
                namespace Test
                {
                    [Cloneable] public partial class Item { public string? Name { get; set; } }
                }
                """, parseOptions)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Where(x => !string.IsNullOrEmpty(x))
                .Select(x => MetadataReference.CreateFromFile(x)),
            new(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new CloneSourceGenerator().AsSourceGenerator()],
            parseOptions: parseOptions,
            optionsProvider: null,
            driverOptions: new(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation);

        // An edit somewhere else in the project must not invalidate the analysis of Item
        var edited = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("namespace Other { public class Unrelated {} }", parseOptions));
        driver = driver.RunGenerators(edited);

        var steps = driver.GetRunResult().Results.Single().TrackedSteps;
        steps.Should().ContainKey(CloneSourceGenerator.TrackingName);
        steps[CloneSourceGenerator.TrackingName]
            .SelectMany(x => x.Outputs)
            .Should().OnlyContain(x => x.Reason == IncrementalStepRunReason.Cached || x.Reason == IncrementalStepRunReason.Unchanged);
    }
}
