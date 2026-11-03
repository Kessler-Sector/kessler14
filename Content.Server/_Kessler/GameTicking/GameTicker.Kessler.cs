using Content.Server._Kessler.Persistence;

namespace Content.Server.GameTicking;

// Kessler: persistent sector.
public sealed partial class GameTicker
{
    [Dependency] private readonly PersistentSectorSystem _persistentSector = default!;

    /// <summary>
    /// Loads the persistent sector as the round's default map, if persistence is enabled and a save exists.
    /// </summary>
    private bool TryLoadPersistentSector()
    {
        if (!_persistentSector.TryLoadSector(out var mapId))
            return false;

        DefaultMap = mapId;
        return true;
    }
}
