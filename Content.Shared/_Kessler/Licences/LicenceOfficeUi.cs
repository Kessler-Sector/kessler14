using Robust.Shared.Serialization;

namespace Content.Shared._Kessler.Licences;

[Serializable, NetSerializable]
public enum LicenceOfficeUiKey : byte
{
    Key,
}

/// <summary>
/// One reputation track as the licensing office shows it.
/// </summary>
[Serializable, NetSerializable]
public sealed class LicenceOfficeTrack
{
    public string Name = string.Empty;
    public string Description = string.Empty;
    public string LevelName = string.Empty;
    public int Points;

    /// <summary>
    /// Points where the current level starts, and where the next one does (null at the top level).
    /// </summary>
    public int LevelStart;
    public int? NextLevel;
}

/// <summary>
/// One licence: held, ready to buy, or what is still missing.
/// </summary>
[Serializable, NetSerializable]
public sealed class LicenceOfficeLicence
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public string Description = string.Empty;
    public int Fee;
    public DateTime? HeldSince;
    public List<string> Missing = new();
}

/// <summary>
/// Sent to one viewer only: the office shows each person their own character.
/// </summary>
[Serializable, NetSerializable]
public sealed class LicenceOfficeStateMessage : BoundUserInterfaceMessage
{
    public string? CharacterName;
    public string? Problem;
    public string? Notice;
    public List<LicenceOfficeTrack> Tracks = new();
    public List<LicenceOfficeLicence> Licences = new();
}

[Serializable, NetSerializable]
public sealed class LicenceOfficeBuyMessage(string licence) : BoundUserInterfaceMessage
{
    public readonly string Licence = licence;
}
