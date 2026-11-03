using Robust.Shared.Configuration;

namespace Content.Shared._Kessler.CCVar;

/// <summary>
/// Contains CVars used by Kessler Sector.
/// </summary>
[CVarDefs]
public sealed class KesslerCVars
{
    /// <summary>
    /// Whether the sector is persistent: round start loads the saved sector instead of generating one, and the sector
    /// is saved at round end and at server shutdown.
    /// </summary>
    public static readonly CVarDef<bool> PersistenceEnabled =
        CVarDef.Create("kessler.persistence.enabled", false, CVar.SERVERONLY);

    /// <summary>
    /// Folder in the server data directory that holds the sector save and its backups.
    /// </summary>
    public static readonly CVarDef<string> PersistencePath =
        CVarDef.Create("kessler.persistence.path", "/kessler/persistence", CVar.SERVERONLY);

    /// <summary>
    /// How many previous saves to keep as backups.
    /// </summary>
    public static readonly CVarDef<int> PersistenceBackups =
        CVarDef.Create("kessler.persistence.backups", 5, CVar.SERVERONLY);

    /// <summary>
    /// Minutes between automatic saves while a round is running. 0 disables autosave. Each save pauses the server for
    /// its duration.
    /// </summary>
    public static readonly CVarDef<int> PersistenceAutosaveMinutes =
        CVarDef.Create("kessler.persistence.autosave_minutes", 0, CVar.SERVERONLY);
}
