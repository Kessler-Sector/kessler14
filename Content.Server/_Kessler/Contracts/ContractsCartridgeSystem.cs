using System.Linq;
using Content.Server.CartridgeLoader;
using Content.Server.Popups;
using Content.Shared._Kessler.Contracts;
using Content.Shared.CartridgeLoader;
using Content.Shared.PDA;
using Robust.Server.GameObjects;

namespace Content.Server._Kessler.Contracts;

/// <summary>
/// The Contracts PDA program: lists open procurement contracts at every station and the viewer's own, and accepts or
/// abandons them.
/// </summary>
[RegisterComponent]
public sealed partial class ContractsCartridgeComponent : Component;

public sealed class ContractsCartridgeSystem : EntitySystem
{
    [Dependency] private readonly CartridgeLoaderSystem _cartridgeLoader = default!;
    [Dependency] private readonly ContractSystem _contracts = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ContractsCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<ContractsCartridgeComponent, CartridgeMessageEvent>(OnMessage);
    }

    private void OnUiReady(Entity<ContractsCartridgeComponent> ent, ref CartridgeUiReadyEvent args)
    {
        var viewer = _ui.GetActors(args.Loader, PdaUiKey.Key).FirstOrDefault();
        Send(args.Loader, viewer == default ? null : viewer);
    }

    private void OnMessage(Entity<ContractsCartridgeComponent> ent, ref CartridgeMessageEvent args)
    {
        var loader = GetEntity(args.LoaderUid);
        var user = args.User;
        var now = DateTime.UtcNow;

        switch (args)
        {
            case ContractAcceptMessage accept:
                if (_contracts.Accept(user, GetEntity(accept.Station), accept.Id, now) is { } problem)
                    _popup.PopupEntity(Loc.GetString(problem), user, user);
                break;
            case ContractAbandonMessage abandon:
                _contracts.Abandon(user, GetEntity(abandon.Station), abandon.Id);
                break;
        }

        Send(loader, user);
    }

    private void Send(EntityUid loader, EntityUid? viewer)
    {
        _cartridgeLoader.UpdateCartridgeUiState(loader, _contracts.BuildState(viewer, DateTime.UtcNow));
    }
}
