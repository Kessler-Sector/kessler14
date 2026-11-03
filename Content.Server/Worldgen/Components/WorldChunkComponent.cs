using Content.Server.Worldgen.Systems;

namespace Content.Server.Worldgen.Components;

/// <summary>
///     This is used for marking an entity as being a world chunk.
/// </summary>
[RegisterComponent]
[Access(typeof(WorldControllerSystem))]
public sealed partial class WorldChunkComponent : Component
{
    /// <summary>
    ///     The coordinates of the chunk, in chunk space.
    /// </summary>
    [DataField("coordinates")] public Vector2i Coordinates;

    /// <summary>
    ///     The map this chunk belongs to.
    /// </summary>
    /// <remarks>
    ///     Kessler: nullable so the prototype default is not written to saves as an invalid entity reference.
    /// </remarks>
    [DataField("map")] public EntityUid? Map; // Kessler
}

