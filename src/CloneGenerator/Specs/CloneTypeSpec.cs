namespace NanoByte.CloneGenerator.Specs;

/// <summary>
/// Everything the emitter needs to write the generated source for one type.
/// </summary>
internal sealed record CloneTypeSpec(
    string HintName,
    string? Namespace,
    EquatableArray<string> TypeDeclarations,
    string DocName,
    string QualifiedName,
    bool IsSealed,
    string? BaseCloneFromTo,
    string CloneMethodName,
    string CloneReturnType,
    string? OverrideReturnType,
    bool EmitAbstractClone,
    string? ExplicitGenericInterface,
    bool EmitSystemCloneable,
    bool EmitCloneFromTo,
    bool EmitCloneMethod,
    EquatableArray<string> InterfaceBridges,
    EquatableArray<MemberSpec> Members,
    EquatableArray<MemberSpec> InitializerMembers);
