namespace NanoByte.CloneGenerator;

/// <summary>
/// Verifies the diagnostics the generator reports.
/// </summary>
public class DiagnosticFacts
{
    /// <summary>An <c>ICloneable&lt;T&gt;</c> in a namespace of its own, like the one in NanoByte.Common.</summary>
    private const string CloneableInterface =
        """
        namespace Contracts
        {
            public interface ICloneable<out T> { T Clone(); }
        }
        """;

    [Fact]
    public void ReportsNonPartialTypes()
    {
        var result = GeneratorHarness.Run(
            """
            using NanoByte.CloneGenerator;
            namespace Test
            {
                [Cloneable] public class NotPartial { public string? Name { get; set; } }
            }
            """);

        result.DiagnosticIds.Should().Contain("CLONE001");
        result.Sources.Keys.Should().NotContain(x => x.Contains("NotPartial"));
    }

    [Fact]
    public void ReportsMissingParameterlessConstructor()
    {
        var result = GeneratorHarness.Run(
            """
            using NanoByte.CloneGenerator;
            namespace Test
            {
                [Cloneable] public partial class NeedsArgs { public NeedsArgs(int x) {} public string? Name { get; set; } }
            }
            """);

        result.DiagnosticIds.Should().Contain("CLONE002");
    }

    [Fact]
    public void WarnsAboutShallowCopies()
    {
        var result = GeneratorHarness.Run(
            """
            using NanoByte.CloneGenerator;
            namespace Test
            {
                public class Mutable { public string? Value { get; set; } }
                [Cloneable] public partial class Holder { public Mutable? Payload { get; set; } }
            }
            """);

        result.DiagnosticIds.Should().Contain("CLONE003");
        result.SourceFor("Holder").Should().Contain("to.Payload = from.Payload;");
    }

    [Fact]
    public void ShallowCloneAttributeSilencesTheWarning()
    {
        var result = GeneratorHarness.Run(
            """
            using NanoByte.CloneGenerator;
            namespace Test
            {
                public class Mutable { public string? Value { get; set; } }
                [Cloneable] public partial class Holder { [ShallowClone] public Mutable? Payload { get; set; } }
            }
            """);

        result.DiagnosticIds.Should().NotContain("CLONE003");
        result.SourceFor("Holder").Should().Contain("to.Payload = from.Payload;");
    }

    [Fact]
    public void AssemblyLevelRegistrationSilencesTheWarning()
    {
        var result = GeneratorHarness.Run(
            """
            using NanoByte.CloneGenerator;
            [assembly: CloneShallow(typeof(Test.Immutable))]
            namespace Test
            {
                public class Immutable { public string? Value { get; } }
                [Cloneable] public partial class Holder { public Immutable? Payload { get; set; } }
            }
            """);

        result.DiagnosticIds.Should().NotContain("CLONE003");
        result.SourceFor("Holder").Should().Contain("to.Payload = from.Payload;");
    }

    [Fact]
    public void DoesNotWarnAboutExternalTypes()
    {
        // Types from other assemblies are usually immutable value objects; warning about them is noise
        var result = GeneratorHarness.Run(
            """
            using NanoByte.CloneGenerator;
            namespace Test
            {
                [Cloneable] public partial class Holder { public System.Text.StringBuilder? Builder { get; set; } }
            }
            """);

        result.DiagnosticIds.Should().NotContain("CLONE003");
    }

    [Fact]
    public void ReportsBaseTypesThatAreNotCloneable()
    {
        var result = GeneratorHarness.Run(
            """
            using NanoByte.CloneGenerator;
            namespace Test
            {
                public class Base { public string? Inherited { get; set; } }
                [Cloneable] public partial class Derived : Base { public string? Own { get; set; } }
            }
            """);

        result.DiagnosticIds.Should().Contain("CLONE004");
    }

    [Fact]
    public void BacksOffFromHandWrittenCloneMethods()
    {
        var result = GeneratorHarness.RunValid(
            """
            using NanoByte.CloneGenerator;
            namespace Test
            {
                [Cloneable]
                public partial class Custom
                {
                    public string? Name { get; set; }
                    public Custom Clone() => new Custom {Name = Name};
                }
            }
            """);

        result.DiagnosticIds.Should().Contain("CLONE005");

        // The helper is still generated, but the method the user wrote is left alone
        string source = result.SourceFor("Custom");
        source.Should().Contain("CloneFromTo");
        source.Should().NotContain("Custom Clone()");
    }

    [Fact]
    public void BacksOffFromHandWrittenCloneFromTo()
    {
        var result = GeneratorHarness.RunValid(
            """
            using NanoByte.CloneGenerator;
            namespace Test
            {
                [Cloneable]
                public partial class Custom
                {
                    public string? Name { get; set; }
                    protected static void CloneFromTo(Custom from, Custom to) { to.Name = from.Name; }
                }
            }
            """);

        result.DiagnosticIds.Should().Contain("CLONE005");
        result.SourceFor("Custom").Should().NotContain("protected static void CloneFromTo");
    }

    [Fact]
    public void RejectsValueTypesAndNestedTypes()
    {
        GeneratorHarness.Run(
            """
            using NanoByte.CloneGenerator;
            namespace Test
            {
                public partial class Outer { [Cloneable] public partial class Inner { public string? Name { get; set; } } }
            }
            """)
            .DiagnosticIds.Should().Contain("CLONE006");
    }

    [Fact]
    public void WarnsAboutMembersTypedAsAnUnconstrainedTypeParameter()
    {
        // Nothing about 'T' says whether it can be cloned, so this has to be flagged rather than assumed
        GeneratorHarness.Run(
            """
            using NanoByte.CloneGenerator;
            namespace Test
            {
                [Cloneable] public partial class Holder<T> { public T? Value { get; set; } }
            }
            """)
            .DiagnosticIds.Should().Contain("CLONE003");
    }

    [Fact]
    public void DoesNotWarnAboutAStructConstrainedTypeParameter()
    {
        GeneratorHarness.Run(
            """
            using NanoByte.CloneGenerator;
            namespace Test
            {
                [Cloneable] public partial class Holder<T> where T : struct { public T Value { get; set; } }
            }
            """)
            .DiagnosticIds.Should().NotContain("CLONE003");
    }
}
