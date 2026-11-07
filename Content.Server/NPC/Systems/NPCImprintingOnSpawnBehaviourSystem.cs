using System.Numerics;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Whitelist;
using Robust.Shared.Collections;
using Robust.Shared.Map;
using Robust.Shared.Random;
using NPCImprintingOnSpawnBehaviourComponent = Content.Server.NPC.Components.NPCImprintingOnSpawnBehaviourComponent;

using Content.Server._Kessler.Persistence; // Kessler
using Robust.Shared.Map.Events; // Kessler

namespace Content.Server.NPC.Systems;

public sealed partial class NPCImprintingOnSpawnBehaviourSystem : SharedNPCImprintingOnSpawnBehaviourSystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly NPCSystem _npc = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelistSystem = default!;
    [Dependency] private readonly PersistenceSaveSystem _persistence = default!; // Kessler

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NPCImprintingOnSpawnBehaviourComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<BeforeSerializationEvent>(OnBeforeSerialization); // Kessler
    }

    // Kessler: friends are remembered from spawn and never removed, so a friend that was deleted (or one the persistent
    // save leaves out) made saving log a reference to a missing entity. Prune them before any save.
    private void OnBeforeSerialization(BeforeSerializationEvent args)
    {
        var query = EntityQueryEnumerator<NPCImprintingOnSpawnBehaviourComponent>();
        while (query.MoveNext(out _, out var imprinting))
        {
            imprinting.Friends.RemoveAll(friend => !Exists(friend)
                                                   || MetaData(friend).EntityPrototype is { } proto && !_persistence.IsPersisted(proto));
        }
    }

    private void OnMapInit(Entity<NPCImprintingOnSpawnBehaviourComponent> imprinting, ref MapInitEvent args)
    {
        HashSet<EntityUid> friends = new();
        _lookup.GetEntitiesInRange(imprinting, imprinting.Comp.SpawnFriendsSearchRadius, friends);

        foreach (var friend in friends)
        {
            if (_whitelistSystem.IsWhitelistPassOrNull(imprinting.Comp.Whitelist, friend))
            {
                AddImprintingTarget(imprinting, friend, imprinting.Comp);
            }
        }

        if (imprinting.Comp.Follow && imprinting.Comp.Friends.Count > 0)
        {
            var mommy = _random.Pick(imprinting.Comp.Friends);
            _npc.SetBlackboard(imprinting, NPCBlackboard.FollowTarget, new EntityCoordinates(mommy, Vector2.Zero));
        }
    }

    public void AddImprintingTarget(EntityUid entity, EntityUid friend, NPCImprintingOnSpawnBehaviourComponent component)
    {
        component.Friends.Add(friend);
        var exception = EnsureComp<FactionExceptionComponent>(entity);
        exception.Ignored.Add(friend);
    }
}
