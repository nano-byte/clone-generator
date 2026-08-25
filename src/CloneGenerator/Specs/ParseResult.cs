namespace NanoByte.CloneGenerator.Specs;

/// <summary>
/// The parser result for one candidate type.
/// </summary>
internal readonly record struct ParseResult(CloneTypeSpec? Spec, EquatableArray<DiagnosticSpec> Diagnostics);
