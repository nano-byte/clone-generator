using System.Collections;

namespace NanoByte.CloneGenerator;

/// <summary>
/// Compiles the generated code, runs it and checks that the clones really are independent.
/// </summary>
public class BehaviourFacts
{
    private const string Model =
        """
        using System.Collections.Generic;
        using NanoByte.CloneGenerator;
        namespace Test
        {
            public interface ICloneable<out T> { T Clone(); }

            [Cloneable] public partial class Child : ICloneable<Child> { public string? Value { get; set; } }

            [Cloneable] public abstract partial class Element : ICloneable<Element>
            {
                public string? Name { get; set; }
                public List<Child> Children { get; } = new();
            }

            [Cloneable] public partial class Leaf : Element
            {
                public required string Id { get; set; }
                public Child? Payload { get; set; }
            }
        }
        """;

    [Fact]
    public void ClonesAreIndependent()
    {
        var (leaf, type) = CreateLeaf();

        object clone = type.GetMethod("CloneLeaf")!.Invoke(leaf, null)!;

        clone.Should().NotBeSameAs(leaf);
        Get(clone, "Id").Should().Be("leaf-1");
        Get(clone, "Name").Should().Be("original");
        Get(Get(clone, "Payload")!, "Value").Should().Be("payload");

        // Mutating the clone's child must not reach the original
        Set(Get(clone, "Payload")!, "Value", "changed");
        Get(Get(leaf, "Payload")!, "Value").Should().Be("payload");
    }

    [Fact]
    public void CollectionsAreDeepCopied()
    {
        var (leaf, type) = CreateLeaf();

        object clone = type.GetMethod("CloneLeaf")!.Invoke(leaf, null)!;

        var originalChildren = ((IEnumerable)Get(leaf, "Children")!).Cast<object>().ToList();
        var clonedChildren = ((IEnumerable)Get(clone, "Children")!).Cast<object>().ToList();

        clonedChildren.Should().HaveCount(2);
        clonedChildren[0].Should().NotBeSameAs(originalChildren[0]);
        Get(clonedChildren[0], "Value").Should().Be("a");

        // The list itself is a different instance, so adding to it does not affect the original
        Get(clone, "Children").Should().NotBeSameAs(Get(leaf, "Children"));
    }

    [Fact]
    public void CloningThroughTheBaseTypeKeepsTheDerivedState()
    {
        var (leaf, type) = CreateLeaf();

        // Call Clone() as declared on the abstract base
        object clone = type.BaseType!.GetMethod("Clone")!.Invoke(leaf, null)!;

        clone.GetType().Name.Should().Be("Leaf", because: "the override must dispatch to the derived implementation");
        Get(clone, "Id").Should().Be("leaf-1");
    }

    [Fact]
    public void QueuesAreCopiedInOrderAndIndependently()
    {
        // Queue<T> is rebuilt through its copy constructor, so both the order and the independence
        // of the copy have to hold for callers that Peek() and Dequeue() their way through it
        var assembly = GeneratorHarness.RunValid(
            """
            using System.Collections.Generic;
            using NanoByte.CloneGenerator;
            namespace Test
            {
                [Cloneable] public partial class Path { public Queue<int> Nodes { get; private set; } = new(); }
            }
            """).Load();

        var pathType = assembly.GetType("Test.Path")!;
        object path = Activator.CreateInstance(pathType)!;
        var nodes = (Queue<int>)Get(path, "Nodes")!;
        nodes.Enqueue(1);
        nodes.Enqueue(2);
        nodes.Enqueue(3);

        object clone = pathType.GetMethod("Clone")!.Invoke(path, null)!;
        var clonedNodes = (Queue<int>)Get(clone, "Nodes")!;

        clonedNodes.Should().NotBeSameAs(nodes);
        // The copy constructor must preserve FIFO order
        clonedNodes.Should().Equal(1, 2, 3);

        // Draining the clone must not consume the original's path
        clonedNodes.Dequeue().Should().Be(1);
        nodes.Should().Equal(1, 2, 3);
    }

    private static (object leaf, Type type) CreateLeaf()
    {
        var assembly = GeneratorHarness.RunValid(Model).Load();
        var leafType = assembly.GetType("Test.Leaf")!;
        var childType = assembly.GetType("Test.Child")!;

        object leaf = Activator.CreateInstance(leafType)!;
        Set(leaf, "Id", "leaf-1");
        Set(leaf, "Name", "original");
        Set(leaf, "Payload", NewChild(childType, "payload"));

        var children = (IList)Get(leaf, "Children")!;
        children.Add(NewChild(childType, "a"));
        children.Add(NewChild(childType, "b"));

        return (leaf, leafType);
    }

    private static object NewChild(Type childType, string value)
    {
        object child = Activator.CreateInstance(childType)!;
        Set(child, "Value", value);
        return child;
    }

    private static object? Get(object target, string name)
        => target.GetType().GetProperty(name)!.GetValue(target);

    private static void Set(object target, string name, object? value)
        => target.GetType().GetProperty(name)!.SetValue(target, value);
}
