using Content.Shared._Shitmed.Targeting; // Shitmed Change
using Robust.Shared.Serialization;

namespace Content.Shared.MedicalScanner;

/// <summary>
/// On interacting with an entity retrieves the entity UID for use with getting the current damage of the mob.
/// </summary>
[Serializable, NetSerializable]
public sealed class HealthAnalyzerScannedUserMessage : BoundUserInterfaceMessage
{
    public HealthAnalyzerUiState State;

    public HealthAnalyzerScannedUserMessage(HealthAnalyzerUiState state)
    {
        State = state;
    }
}

/// <summary>
/// Contains the current state of a health analyzer control. Used for the health analyzer and cryo pod.
/// </summary>
[Serializable, NetSerializable]
public struct HealthAnalyzerUiState
{
    public readonly NetEntity? TargetEntity;
    public float Temperature;
    public float BloodLevel;
    public bool? ScanMode;
    public bool? Bleeding;
    public bool? Unrevivable;
    public bool? Unclonable; // Frontier
    public bool Printable; // Frontier

    // Shitmed Change Start
    /// <summary>
    /// How hurt each body part is, for the body doll. Null for patients without body parts.
    /// </summary>
    public Dictionary<TargetBodyPart, TargetIntegrity>? Body;

    /// <summary>
    /// The body part being looked at, or null for the whole body.
    /// </summary>
    public NetEntity? Part;
    // Shitmed Change End

    public HealthAnalyzerUiState() {}

    public HealthAnalyzerUiState(NetEntity? targetEntity, float temperature, float bloodLevel, bool? scanMode, bool? bleeding, bool? unrevivable, bool? unclonable, bool printable = false) // Frontier: added unclonable, printable
    {
        TargetEntity = targetEntity;
        Temperature = temperature;
        BloodLevel = bloodLevel;
        ScanMode = scanMode;
        Bleeding = bleeding;
        Unrevivable = unrevivable;
        Unclonable = unclonable; // Frontier
        Printable = printable; // Frontier
    }
}

// Shitmed Change Start
/// <summary>
/// Sent by the health analyzer UI to look at one body part of the patient, or the whole body when the part is null.
/// </summary>
[Serializable, NetSerializable]
public sealed class HealthAnalyzerPartMessage(NetEntity? owner, TargetBodyPart? bodyPart) : BoundUserInterfaceMessage
{
    public readonly NetEntity? Owner = owner;
    public readonly TargetBodyPart? BodyPart = bodyPart;
}
// Shitmed Change End
