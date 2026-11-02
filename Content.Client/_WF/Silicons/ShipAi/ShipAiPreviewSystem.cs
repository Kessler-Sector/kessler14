using Content.Shared._WF.Silicons.ShipAi;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Silicons.StationAi;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.Silicons.ShipAi;

public sealed class ShipAiPreviewSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _protos = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;

    public void Apply(EntityUid dummy, HumanoidCharacterProfile profile)
    {
        if (!HasComp<ShipAiInterfaceComponent>(dummy))
            return;

        profile.Loadouts.TryGetValue(ShipAiSystem.Loadout, out var loadout);

        _appearance.SetData(dummy, ShipAiVisuals.Screen, Pick(ShipAiSystem.ScreenGroup, loadout, ShipAiSystem.DefaultScreen).Id);
        _appearance.SetData(dummy, ShipAiVisuals.Casing, Pick(ShipAiSystem.CasingGroup, loadout, ShipAiSystem.Fallback(_protos, ShipAiSystem.CasingGroup)).Id);
        _appearance.SetData(dummy, ShipAiVisuals.Accessory, Pick(ShipAiSystem.AccessoryGroup, loadout, ShipAiSystem.Fallback(_protos, ShipAiSystem.AccessoryGroup)).Id);

        if (ShipAiSystem.TryGetFaceLayer(_protos, Pick(ShipAiSystem.FaceGroup, loadout, ShipAiSystem.DefaultFace), out var layer))
            _appearance.SetData(dummy, StationAiVisualState.Key, layer);
    }

    private ProtoId<StationAiCustomizationPrototype> Pick(ProtoId<StationAiCustomizationGroupPrototype> group, RoleLoadout? loadout, ProtoId<StationAiCustomizationPrototype> fallback)
    {
        var chosen = ShipAiSystem.Chosen(loadout, group);
        return chosen != null && _protos.TryIndex(group, out var choices) && choices.ProtoIds.Contains(chosen)
            ? new ProtoId<StationAiCustomizationPrototype>(chosen)
            : fallback;
    }
}
