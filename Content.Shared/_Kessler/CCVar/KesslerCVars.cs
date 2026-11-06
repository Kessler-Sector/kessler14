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

    /// <summary>
    /// Seconds between tick time log lines from the tick time monitor. 0 disables the log.
    /// </summary>
    public static readonly CVarDef<int> TickLogSeconds =
        CVarDef.Create("kessler.diagnostics.tick_log_seconds", 0, CVar.SERVERONLY);

    /// <summary>
    /// Whether ship owners are billed daily upkeep.
    /// </summary>
    public static readonly CVarDef<bool> UpkeepEnabled =
        CVarDef.Create("kessler.upkeep.enabled", true, CVar.SERVERONLY);

    /// <summary>
    /// Upkeep per billing interval, as a share of the ship's purchase price.
    /// </summary>
    public static readonly CVarDef<float> UpkeepRate =
        CVarDef.Create("kessler.upkeep.rate", 0.01f, CVar.SERVERONLY);

    /// <summary>
    /// Real hours between upkeep charges.
    /// </summary>
    public static readonly CVarDef<float> UpkeepIntervalHours =
        CVarDef.Create("kessler.upkeep.interval_hours", 24f, CVar.SERVERONLY);

    /// <summary>
    /// Real days a ship may stay in upkeep debt before it is due for impounding.
    /// </summary>
    public static readonly CVarDef<float> UpkeepGraceDays =
        CVarDef.Create("kessler.upkeep.grace_days", 3f, CVar.SERVERONLY);

    /// <summary>
    /// How often, in seconds, the server checks for upkeep that is due.
    /// </summary>
    public static readonly CVarDef<float> UpkeepCheckSeconds =
        CVarDef.Create("kessler.upkeep.check_seconds", 60f, CVar.SERVERONLY);

    /// <summary>
    /// Whether impounded ships and ships of inactive owners are moved into storage automatically.
    /// </summary>
    public static readonly CVarDef<bool> StorageEnabled =
        CVarDef.Create("kessler.storage.enabled", true, CVar.SERVERONLY);

    /// <summary>
    /// Real days without the owner logging in before their ships are moved into storage.
    /// </summary>
    public static readonly CVarDef<float> StorageInactiveDays =
        CVarDef.Create("kessler.storage.inactive_days", 7f, CVar.SERVERONLY);

    /// <summary>
    /// How often, in minutes, the server looks for ships to store.
    /// </summary>
    public static readonly CVarDef<float> StorageCheckMinutes =
        CVarDef.Create("kessler.storage.check_minutes", 10f, CVar.SERVERONLY);

    /// <summary>
    /// Folder in the server data directory that holds stored ships.
    /// </summary>
    public static readonly CVarDef<string> StoragePath =
        CVarDef.Create("kessler.storage.path", "/kessler/storage", CVar.SERVERONLY);

    /// <summary>
    /// Whether bodies in cryo storage are deleted after Frontier's nf14.uncryo.maxtime. Off: a stored character waits
    /// for its player however long they're away.
    /// </summary>
    public static readonly CVarDef<bool> CryoExpire =
        CVarDef.Create("kessler.cryo.expire", false, CVar.SERVERONLY);

    /// <summary>
    /// Share of an opened parcel's value paid to whoever carried it to the recipient. The rest goes to the Frontier
    /// sector account as before.
    /// </summary>
    public static readonly CVarDef<float> MailCarrierShare =
        CVarDef.Create("kessler.mail.carrier_share", 0.3f, CVar.SERVERONLY);

    /// <summary>
    /// Spesos per expected minute of work paid for station work orders (the S0 band in the progression plan).
    /// </summary>
    public static readonly CVarDef<int> WorkOrderRate =
        CVarDef.Create("kessler.workorders.rate", 150, CVar.SERVERONLY);

    /// <summary>
    /// Whether shipyards require pilot licences by ship size. Vouchers and job shipyards are exempt either way.
    /// </summary>
    public static readonly CVarDef<bool> LicencesEnforced =
        CVarDef.Create("kessler.licences.enforced", true, CVar.SERVERONLY);

    /// <summary>
    /// Whether gas sale prices fall as a station is flooded with one gas.
    /// </summary>
    public static readonly CVarDef<bool> GasMarketEnabled =
        CVarDef.Create("kessler.market.gas_enabled", true, CVar.SERVERONLY);

    /// <summary>
    /// Moles of one gas a station can take before its price has halved.
    /// </summary>
    public static readonly CVarDef<float> GasMarketDepth =
        CVarDef.Create("kessler.market.gas_depth", 50000f, CVar.SERVERONLY);

    /// <summary>
    /// Real hours for a station's gas saturation to halve.
    /// </summary>
    public static readonly CVarDef<float> GasMarketHalfLifeHours =
        CVarDef.Create("kessler.market.gas_half_life_hours", 24f, CVar.SERVERONLY);
}
