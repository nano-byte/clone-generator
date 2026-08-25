namespace NanoByte.CloneGenerator;

/// <summary>
/// Verifies the shape of the generated code.
/// </summary>
public class EmissionFacts
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
    public void GeneratesCloneFromToAndFactory()
    {
        var result = GeneratorHarness.RunValid(
            """
            using NanoByte.CloneGenerator;
            namespace Test
            {
                [Cloneable]
                public partial class Simple
                {
                    public string? Name { get; set; }
                    public int Count { get; set; }
                }
            }
            """);

        string source = result.SourceFor("Simple");
        source.Should().Contain("protected static void CloneFromTo(global::Test.Simple from, global::Test.Simple to)");
        source.Should().Contain("to.Name = from.Name;");
        source.Should().Contain("to.Count = from.Count;");
        source.Should().Contain("public virtual global::Test.Simple Clone()");
    }

    [Fact]
    public void MakesCloneNonVirtualOnSealedTypes()
    {
        var result = GeneratorHarness.RunValid(
            """
            using NanoByte.CloneGenerator;
            namespace Test
            {
                [Cloneable]
                public sealed partial class Leaf { public string? Name { get; set; } }
            }
            """);

        result.SourceFor("Leaf").Should().Contain("public global::Test.Leaf Clone()")
              .And.NotContain("virtual");
    }

    [Fact]
    public void SetsRequiredAndInitMembersInTheObjectInitializer()
    {
        var result = GeneratorHarness.RunValid(
            """
            using NanoByte.CloneGenerator;
            namespace Test
            {
                [Cloneable]
                public partial class WithRequired
                {
                    public required string Name { get; set; }
                    public string? Id { get; init; }
                    public string? Path { get; set; }
                }
            }
            """);

        string source = result.SourceFor("WithRequired");
        source.Should().Contain("Name = from.Name");
        source.Should().Contain("Id = from.Id");
        source.Should().Contain("to.Path = from.Path;");

        // Required and init members cannot be assigned after construction
        source.Should().NotContain("to.Name =");
        source.Should().NotContain("to.Id =");
    }

    [Fact]
    public void ImplementsGenericCloneInterfaceImplicitlyOnTheRoot()
    {
        var result = GeneratorHarness.RunValid(CloneableInterface,
            """
            using NanoByte.CloneGenerator;
            namespace Test
            {
                [Cloneable]
                public partial class Item : global::Contracts.ICloneable<Item>
                {
                    public string? Value { get; set; }
                }
            }
            """);

        // A public Clone() returning the concrete type already satisfies the interface
        result.SourceFor("Item").Should().Contain("public virtual global::Test.Item Clone()")
              .And.NotContain("ICloneable<global::Test.Item>.Clone()");
    }

    [Fact]
    public void BridgesSystemICloneableExplicitly()
    {
        var result = GeneratorHarness.RunValid(
            """
            using NanoByte.CloneGenerator;
            namespace Test
            {
                [Cloneable]
                public partial class Item : System.ICloneable
                {
                    public string? Value { get; set; }
                }
            }
            """);

        result.SourceFor("Item").Should().Contain("object global::System.ICloneable.Clone() => Clone();");
    }

    [Fact]
    public void BridgesSystemICloneableOnAnAbstractRoot()
    {
        // Nothing below the root declares the interface, so without a bridge here it would go unimplemented
        var result = GeneratorHarness.RunValid(
            """
            using NanoByte.CloneGenerator;
            namespace Test
            {
                [Cloneable] public abstract partial class Control : System.ICloneable { public string? Name { get; set; } }
                [Cloneable] public partial class Label : Control { public string? Text { get; set; } }
            }
            """);

        result.SourceFor("Control").Should().Contain("public abstract global::Test.Control Clone();")
              .And.Contain("object global::System.ICloneable.Clone() => Clone();");

        // The derived type inherits the bridge rather than duplicating it
        result.SourceFor("Label").Should().Contain("public override global::Test.Control Clone() => CloneLabel();")
              .And.NotContain("System.ICloneable");
    }

    [Fact]
    public void KeepsAHandWrittenSystemICloneableBridge()
    {
        var result = GeneratorHarness.RunValid(
            """
            using NanoByte.CloneGenerator;
            namespace Test
            {
                [Cloneable]
                public partial class Item : System.ICloneable
                {
                    public string? Value { get; set; }
                    object System.ICloneable.Clone() => Clone();
                }
            }
            """);

        result.SourceFor("Item").Should().NotContain("System.ICloneable");
    }

    [Fact]
    public void SupportsBothCloneInterfacesOnTheSameType()
    {
        var result = GeneratorHarness.RunValid(CloneableInterface,
            """
            using NanoByte.CloneGenerator;
            namespace Test
            {
                [Cloneable]
                public partial class Item : global::Contracts.ICloneable<Item>, System.ICloneable
                {
                    public string? Value { get; set; }
                }
            }
            """);

        string source = result.SourceFor("Item");
        source.Should().Contain("public virtual global::Test.Item Clone()");
        source.Should().Contain("object global::System.ICloneable.Clone() => Clone();");
    }

    [Fact]
    public void ChainsCloneFromToThroughTheInheritanceHierarchy()
    {
        var result = GeneratorHarness.RunValid(CloneableInterface,
            """
            using NanoByte.CloneGenerator;
            namespace Test
            {
                [Cloneable] public abstract partial class Root { public string? A { get; set; } }
                [Cloneable] public abstract partial class Element : Root, global::Contracts.ICloneable<Element> { public string? B { get; set; } }
                [Cloneable] public abstract partial class Middle : Element { public string? C { get; set; } }
                [Cloneable] public partial class Leaf : Middle { public string? D { get; set; } }
            }
            """);

        // Root has no clone contract, so it only contributes a helper and does not claim the Clone() name
        result.SourceFor("Root").Should().Contain("CloneFromTo").And.NotContain("Clone()");

        // Element owns the contract
        result.SourceFor("Element").Should().Contain("public abstract global::Test.Element Clone();");
        result.SourceFor("Element").Should().Contain("global::Test.Root.CloneFromTo(from, to);");

        // Middle is abstract, so it only extends the chain
        result.SourceFor("Middle").Should().Contain("global::Test.Element.CloneFromTo(from, to);")
              .And.NotContain("Clone()");

        // The leaf gets a strongly typed method plus the override
        string leaf = result.SourceFor("Leaf");
        leaf.Should().Contain("public global::Test.Leaf CloneLeaf()");
        leaf.Should().Contain("public override global::Test.Element Clone() => CloneLeaf();");
        leaf.Should().Contain("global::Test.Middle.CloneFromTo(from, to);");
        leaf.Should().Contain("to.D = from.D;");

        // Each level copies only what it declares
        leaf.Should().NotContain("to.A =").And.NotContain("to.B =").And.NotContain("to.C =");
    }

    [Fact]
    public void HonoursCustomMethodName()
    {
        var result = GeneratorHarness.RunValid(CloneableInterface,
            """
            using NanoByte.CloneGenerator;
            namespace Test
            {
                [Cloneable] public abstract partial class Element : global::Contracts.ICloneable<Element> { public string? A { get; set; } }
                [Cloneable(MethodName = "CloneImplementation")] public partial class PackageImplementation : Element {}
            }
            """);

        result.SourceFor("PackageImplementation")
              .Should().Contain("public global::Test.PackageImplementation CloneImplementation()")
              .And.Contain("public override global::Test.Element Clone() => CloneImplementation();");
    }

    [Fact]
    public void AddsExplicitInterfaceImplementationWhenTheMethodIsRenamed()
    {
        var result = GeneratorHarness.RunValid(CloneableInterface,
            """
            using NanoByte.CloneGenerator;
            namespace Test
            {
                [Cloneable] public abstract partial class ArgBase : global::Contracts.ICloneable<ArgBase> { public string? A { get; set; } }
                [Cloneable] public partial class Arg : ArgBase, global::Contracts.ICloneable<Arg> { public string? Value { get; set; } }
            }
            """);

        result.SourceFor("Arg")
              .Should().Contain("global::Test.Arg global::Contracts.ICloneable<global::Test.Arg>.Clone() => CloneArg();");
    }

    [Fact]
    public void BridgesACloneContractInheritedFromAnInterface()
    {
        var result = GeneratorHarness.RunValid(CloneableInterface,
            """
            using NanoByte.CloneGenerator;
            namespace Test
            {
                public interface IRecipeStep : global::Contracts.ICloneable<IRecipeStep> {}

                [Cloneable] public partial class RemoveStep : IRecipeStep { public string? Path { get; set; } }
            }
            """);

        // Clone() returns RemoveStep, which does not satisfy ICloneable<IRecipeStep> on its own
        result.SourceFor("RemoveStep")
              .Should().Contain("global::Test.IRecipeStep global::Contracts.ICloneable<global::Test.IRecipeStep>.Clone() => Clone();");
    }

    [Fact]
    public void CastsTheBridgeWhenTheAbstractCloneReturnsAnUnrelatedType()
    {
        var result = GeneratorHarness.RunValid(CloneableInterface,
            """
            using NanoByte.CloneGenerator;
            namespace Test
            {
                public interface IRecipeStep : global::Contracts.ICloneable<IRecipeStep> {}

                [Cloneable] public abstract partial class RetrievalMethod : global::Contracts.ICloneable<RetrievalMethod> {}
                [Cloneable] public abstract partial class DownloadRetrievalMethod : RetrievalMethod, IRecipeStep { public long Size { get; set; } }
                [Cloneable] public partial class Archive : DownloadRetrievalMethod { public string? Extract { get; set; } }
            }
            """);

        // Clone() returns RetrievalMethod here, which is not an IRecipeStep
        result.SourceFor("DownloadRetrievalMethod")
              .Should().Contain("global::Test.IRecipeStep global::Contracts.ICloneable<global::Test.IRecipeStep>.Clone() => (global::Test.IRecipeStep)Clone();");

        // The derived type inherits the bridge rather than duplicating it
        result.SourceFor("Archive").Should().NotContain("ICloneable<global::Test.IRecipeStep>");
    }

    [Fact]
    public void DoesNotCallCloneOnAnAbstractTypeThatHasNoContract()
    {
        // PhoneNumber is [Cloneable] but declares no clone interface, so it only contributes a
        // CloneFromTo helper. Predicting a Clone() on it would generate code that does not compile.
        var result = GeneratorHarness.RunValid(
            """
            using System.Collections.Generic;
            using NanoByte.CloneGenerator;
            namespace Test
            {
                [Cloneable] public abstract partial class PhoneNumber { public string? LocalNumber { get; set; } }
                [Cloneable] public partial class Contact { public List<PhoneNumber> PhoneNumbers { get; } = []; }
            }
            """);

        result.SourceFor("Contact").Should().Contain("to.PhoneNumbers.Add(item);");
        result.DiagnosticIds.Should().Contain("CLONE003");
    }

    [Fact]
    public void ClonesElementsOfAnAbstractTypeThatDeclaresAContract()
    {
        var result = GeneratorHarness.RunValid(CloneableInterface,
            """
            using System.Collections.Generic;
            using NanoByte.CloneGenerator;
            namespace Test
            {
                [Cloneable] public abstract partial class PhoneNumber : global::Contracts.ICloneable<PhoneNumber> { public string? LocalNumber { get; set; } }
                [Cloneable] public partial class MobileNumber : PhoneNumber { public string? Carrier { get; set; } }
                [Cloneable] public partial class Contact { public List<PhoneNumber> PhoneNumbers { get; } = []; }
            }
            """);

        result.SourceFor("Contact").Should().Contain("to.PhoneNumbers.Add(item?.Clone()!);");
        result.SourceFor("PhoneNumber").Should().Contain("public abstract global::Test.PhoneNumber Clone();");
        result.SourceFor("MobileNumber").Should().Contain("public override global::Test.PhoneNumber Clone() => CloneMobileNumber();");
    }

    [Fact]
    public void ClonesCollectionElementsButNotStrings()
    {
        var result = GeneratorHarness.RunValid(CloneableInterface,
            """
            using System.Collections.Generic;
            using NanoByte.CloneGenerator;
            namespace Test
            {
                [Cloneable] public partial class Item : global::Contracts.ICloneable<Item> { public string? Value { get; set; } }
                [Cloneable]
                public partial class Holder
                {
                    public List<Item> Items { get; } = new();
                    public List<string> Names { get; } = new();
                }
            }
            """);

        string source = result.SourceFor("Holder");
        source.Should().Contain("foreach (var item in from.Items) to.Items.Add(item?.Clone()!);");
        source.Should().Contain("foreach (var item in from.Names) to.Names.Add(item);");
    }

    [Fact]
    public void RebuildsCollectionsThatHaveNoAddMethod()
    {
        // Queue<T> and Stack<T> are not ICollection<T>, so there is nothing to loop Add() over,
        // but both can be rebuilt from the original's contents
        var result = GeneratorHarness.RunValid(
            """
            using System.Collections.Generic;
            using NanoByte.CloneGenerator;
            namespace Test
            {
                [Cloneable]
                public partial class Path
                {
                    public Queue<int> Nodes { get; private set; } = new();
                    public Stack<string>? History { get; set; }
                }
            }
            """);

        string source = result.SourceFor("Path");
        source.Should().Contain("to.Nodes = new global::System.Collections.Generic.Queue<int>(from.Nodes);");

        // Passing null to the copy constructor would throw
        source.Should().Contain("to.History = from.History == null ? null : new global::System.Collections.Generic.Stack<string>(from.History);");

        result.DiagnosticIds.Should().NotContain("CLONE003");
    }

    [Fact]
    public void CopiesRecordMembersWithWith()
    {
        var result = GeneratorHarness.RunValid(
            """
            using NanoByte.CloneGenerator;
            namespace Test
            {
                public record Settings(string Name);
                [Cloneable] public partial class Holder { public Settings? Settings { get; set; } }
            }
            """);

        result.SourceFor("Holder")
              .Should().Contain("to.Settings = from.Settings == null ? null : from.Settings with {};");
    }

    [Fact]
    public void SuppressesNullOnNonNullableRecordMembers()
    {
        var result = GeneratorHarness.RunValid(
            """
            using NanoByte.CloneGenerator;
            namespace Test
            {
                public record Settings(string Name);
                [Cloneable] public partial class Holder
                {
                    public required Settings Required { get; set; }
                    public Settings Settings { get; set; } = new("");
                }
            }
            """);

        string source = result.SourceFor("Holder");
        source.Should().Contain("Required = (from.Required == null ? null : from.Required with {})!");
        source.Should().Contain("to.Settings = (from.Settings == null ? null : from.Settings with {})!;");
    }

    [Fact]
    public void SkipsIgnoredMembers()
    {
        var result = GeneratorHarness.RunValid(
            """
            using NanoByte.CloneGenerator;
            namespace Test
            {
                [Cloneable]
                public partial class Item
                {
                    public string? Kept { get; set; }
                    [IgnoreClone] public string? Dropped { get; set; }
                }
            }
            """);

        result.SourceFor("Item").Should().Contain("to.Kept = from.Kept;").And.NotContain("Dropped");
    }

    [Fact]
    public void SkipsComputedProperties()
    {
        // These XML-serialization shims project onto real state, which is copied directly.
        // Writing them too would re-derive that state in an order-dependent way.
        var result = GeneratorHarness.RunValid(
            """
            using NanoByte.CloneGenerator;
            namespace Test
            {
                [Cloneable]
                public partial class Element
                {
                    public int Version { get; set; }
                    public string? VersionString
                    {
                        get => Version.ToString();
                        set => Version = value == null ? 0 : int.Parse(value);
                    }
                }
            }
            """);

        result.SourceFor("Element").Should().Contain("to.Version = from.Version;")
              .And.NotContain("VersionString");
    }

    [Fact]
    public void CopiesManuallyImplementedPropertiesViaTheirBackingField()
    {
        // The property is skipped, but the field it wraps is copied directly, so no state is lost
        var result = GeneratorHarness.RunValid(
            """
            using NanoByte.CloneGenerator;
            namespace Test
            {
                [Cloneable]
                public partial class Holder
                {
                    private string? _value;
                    public string? Value { get => _value; set => _value = value; }
                }
            }
            """);

        result.SourceFor("Holder").Should().Contain("to._value = from._value;");
    }

    [Fact]
    public void ReturnsTheSelfTypeOfACuriouslyRecurringHierarchy()
    {
        // Modelled on AlphaFramework: Template<TSelf> promises to return the leaf, and only the leaf
        // can construct it, which is exactly what the CloneFromTo chain already does
        var result = GeneratorHarness.RunValid(CloneableInterface,
            """
            using NanoByte.CloneGenerator;
            namespace Test
            {
                [Cloneable]
                public abstract partial class Template<TSelf> : System.ICloneable, global::Contracts.ICloneable<TSelf>
                    where TSelf : Template<TSelf>
                {
                    public required string Name { get; set; }
                    public string? Description { get; set; }
                }

                [Cloneable]
                public abstract partial class EntityTemplateBase<TSelf> : Template<TSelf>
                    where TSelf : EntityTemplateBase<TSelf>
                {
                    public string? Sound { get; set; }
                }

                [Cloneable]
                public partial class EntityTemplate : EntityTemplateBase<EntityTemplate>
                {
                    public int Collision { get; set; }
                }
            }
            """);

        // The root's contract returns TSelf, not Template<TSelf>
        string root = result.SourceFor("Template");
        root.Should().Contain("public abstract TSelf Clone();");
        root.Should().Contain("object global::System.ICloneable.Clone() => Clone();");

        // The middle layer only extends the chain
        result.SourceFor("EntityTemplateBase")
              .Should().Contain("global::Test.Template<TSelf>.CloneFromTo(from, to);")
              .And.NotContain("Clone()");

        // The leaf substitutes TSelf for itself
        string leaf = result.SourceFor("EntityTemplate");
        leaf.Should().Contain("global::Test.EntityTemplateBase<global::Test.EntityTemplate>.CloneFromTo(from, to);");
        leaf.Should().Contain("public global::Test.EntityTemplate CloneEntityTemplate()");
        leaf.Should().Contain("public override global::Test.EntityTemplate Clone() => CloneEntityTemplate();");

        // Required members are collected across the generic chain
        leaf.Should().Contain("Name = from.Name");
    }

    [Fact]
    public void ClonesMembersTypedAsATypeParameterConstrainedToACloneableType()
    {
        var result = GeneratorHarness.RunValid(CloneableInterface,
            """
            using NanoByte.CloneGenerator;
            namespace Test
            {
                [Cloneable] public partial class Item : global::Contracts.ICloneable<Item> { public string? V { get; set; } }
                [Cloneable] public partial class Holder<T> where T : Item { public T? Value { get; set; } }
            }
            """);

        // Clone() returns Item, so the result has to come back down to T
        result.SourceFor("Holder").Should().Contain("to.Value = (T)from.Value?.Clone()!;");
    }

    [Fact]
    public void ClonesMembersTypedAsASelfTypeParameterWithoutACast()
    {
        // Modelled on AlphaFramework's EntityBase<TCoordinates, TTemplate>
        var result = GeneratorHarness.RunValid(CloneableInterface,
            """
            using NanoByte.CloneGenerator;
            namespace Test
            {
                [Cloneable]
                public abstract partial class Template<TSelf> : global::Contracts.ICloneable<TSelf>
                    where TSelf : Template<TSelf>
                { public string? Description { get; set; } }

                [Cloneable]
                public abstract partial class EntityTemplateBase<TSelf> : Template<TSelf>
                    where TSelf : EntityTemplateBase<TSelf>
                { public string? Sound { get; set; } }

                [Cloneable]
                public abstract partial class EntityBase<TTemplate>
                    where TTemplate : EntityTemplateBase<TTemplate>
                { private TTemplate? _template; }
            }
            """);

        // The contract already returns TTemplate, so no cast is needed
        result.SourceFor("EntityBase").Should().Contain("to._template = from._template?.Clone();");
    }

    [Fact]
    public void ClonesCollectionElementsTypedAsATypeParameter()
    {
        var result = GeneratorHarness.RunValid(CloneableInterface,
            """
            using System.Collections.Generic;
            using NanoByte.CloneGenerator;
            namespace Test
            {
                [Cloneable] public partial class Item : global::Contracts.ICloneable<Item> { public string? V { get; set; } }
                [Cloneable] public partial class Holder<T> where T : Item { public List<T> Values { get; } = new(); }
            }
            """);

        result.SourceFor("Holder").Should().Contain("to.Values.Add((T)item?.Clone()!);");
    }

    [Fact]
    public void ClonesMembersTypedAsACuriouslyRecurringLeaf()
    {
        var result = GeneratorHarness.RunValid(CloneableInterface,
            """
            using NanoByte.CloneGenerator;
            namespace Test
            {
                [Cloneable]
                public abstract partial class Template<TSelf> : global::Contracts.ICloneable<TSelf>
                    where TSelf : Template<TSelf>
                {
                    public string? Description { get; set; }
                }

                [Cloneable] public partial class EntityTemplate : Template<EntityTemplate> {}
                [Cloneable] public partial class Holder { public EntityTemplate? Template { get; set; } }
            }
            """);

        result.SourceFor("Holder").Should().Contain("to.Template = from.Template?.CloneEntityTemplate();");
    }

    [Fact]
    public void SetsRequiredMembersInheritedFromAnotherAssembly()
    {
        var result = GeneratorHarness.RunAcrossAssemblies(
            """
            using NanoByte.CloneGenerator;
            namespace Library
            {
                [Cloneable]
                public abstract partial class Template : System.ICloneable
                {
                    public required string Name { get; set; }
                    public string? Description { get; set; }
                }
            }
            """,
            """
            using NanoByte.CloneGenerator;
            namespace Test
            {
                [Cloneable] public partial class Leaf : global::Library.Template { public int Extra { get; set; } }
            }
            """);

        result.SourceFor("Leaf").Should().Contain("Name = from.Name");
    }

    [Fact]
    public void CopiesPrivateStateDeclaredInABaseClass()
    {
        // A flat initializer in the derived type could never reach this
        var result = GeneratorHarness.RunValid(
            """
            using NanoByte.CloneGenerator;
            namespace Test
            {
                [Cloneable] public abstract partial class Base { private string? _secret; protected string? Protected { get; set; } }
                [Cloneable] public partial class Derived : Base { public string? Public { get; set; } }
            }
            """);

        result.SourceFor("Base").Should().Contain("to._secret = from._secret;")
              .And.Contain("to.Protected = from.Protected;");
    }
}
