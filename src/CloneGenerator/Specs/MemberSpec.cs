namespace NanoByte.CloneGenerator.Specs;

/// <summary>
/// One member to copy.
/// </summary>
/// <param name="Code">Either a complete statement (for the <c>CloneFromTo</c> body) or an expression (for the leaf object initializer).</param>
internal readonly record struct MemberSpec(string Name, string Code);
