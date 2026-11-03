using JetBrains.Annotations;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Validation;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Serialization.TypeSerializers.Interfaces;

namespace Content.Shared._Kessler.Serialization;

/// <summary>
/// Serializes <see cref="Guid"/> data fields. The engine has no serializer for them, so any entity with a
/// <see cref="Guid"/> data field (for example <c>RoleplayLevelComponent</c> on every player body) threw while being
/// saved and was dropped from the save together with everything it contained.
/// </summary>
[TypeSerializer]
public sealed class GuidSerializer : ITypeSerializer<Guid, ValueDataNode>, ITypeCopyCreator<Guid>
{
    public Guid Read(
        ISerializationManager serializationManager,
        ValueDataNode node,
        IDependencyCollection dependencies,
        SerializationHookContext hookCtx,
        ISerializationContext? context = null,
        ISerializationManager.InstantiationDelegate<Guid>? instanceProvider = null)
    {
        if (Guid.TryParse(node.Value, out var guid))
            return guid;

        throw new FormatException($"The input string '{node.Value}' can't be converted to Guid");
    }

    public ValidationNode Validate(
        ISerializationManager serializationManager,
        ValueDataNode node,
        IDependencyCollection dependencies,
        ISerializationContext? context = null)
    {
        return Guid.TryParse(node.Value, out _)
            ? new ValidatedValueNode(node)
            : new ErrorNode(node, "Failed parsing Guid");
    }

    public DataNode Write(
        ISerializationManager serializationManager,
        Guid value,
        IDependencyCollection dependencies,
        bool alwaysWrite = false,
        ISerializationContext? context = null)
    {
        return new ValueDataNode(value.ToString());
    }

    [MustUseReturnValue]
    public Guid CreateCopy(
        ISerializationManager serializationManager,
        Guid source,
        IDependencyCollection dependencies,
        SerializationHookContext hookCtx,
        ISerializationContext? context = null)
    {
        return source;
    }
}
