using Content.Shared._WF.Silicons.ShipAi;
using Content.Shared.CCVar;
using Content.Shared.Silicons.StationAi;
using Content.Shared.SSDIndicator;
using Content.Shared.StatusIcon.Components;
using Content.Client.Overlays;
using Content.Shared.StatusIcon;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.Silicons.ShipAi;

public sealed class ShipAiSsdSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly SharedStationAiSystem _stationAi = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    private static readonly ProtoId<JobIconPrototype> AiJobIcon = "JobIconStationAi";

    private bool _showSsd;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipAiInterfaceComponent, GetStatusIconsEvent>(OnGetStatusIcons, after: [typeof(ShowJobIconsSystem)]);
        Subs.CVar(_cfg, CCVars.ICShowSSDIndicator, value => _showSsd = value, true);
    }

    private void OnGetStatusIcons(Entity<ShipAiInterfaceComponent> ent, ref GetStatusIconsEvent args)
    {
        var showsJobs = args.StatusIcons.RemoveAll(icon => icon is JobIconPrototype) > 0;
        if (!_stationAi.TryGetHeld(new Entity<StationAiCoreComponent?>(ent, null), out var brain))
            return;

        if (showsJobs)
            args.StatusIcons.Add(_prototype.Index(AiJobIcon));

        if (!_showSsd ||
            !TryComp<SSDIndicatorComponent>(brain, out var ssd) ||
            !ssd.IsSSD)
            return;

        args.StatusIcons.Add(_prototype.Index(ssd.ManualSSD ? ssd.ManualIcon : ssd.Icon));
    }
}
