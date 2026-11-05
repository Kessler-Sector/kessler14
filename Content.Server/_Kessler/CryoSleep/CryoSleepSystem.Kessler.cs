using System.Linq;
using Content.Server._Kessler.CryoSleep;
using Content.Server._Kessler.Persistence;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Network;

namespace Content.Server._NF.CryoSleep;

// Kessler: persistent cryo storage. Stored bodies live in a paused container on the sector map and the list of them
// is saved with the sector, instead of a separate map and memory that a restart wipes.
public sealed partial class CryoSleepSystem
{
    [Dependency] private readonly MetaDataSystem _metaData = default!;

    private TimeSpan _kesslerNextPauseCheck;
    private static readonly TimeSpan KesslerPauseCheckInterval = TimeSpan.FromSeconds(5);

    private void InitKessler()
    {
        SubscribeLocalEvent<KesslerCryoStorageComponent, ComponentStartup>(OnKesslerStorageStartup);
        SubscribeLocalEvent<BeforeSectorSaveEvent>(OnBeforeSectorSave);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Loading a map or unpausing it unpauses everything on it, so keep stored bodies paused.
        if (_timing.CurTime < _kesslerNextPauseCheck)
            return;

        _kesslerNextPauseCheck = _timing.CurTime + KesslerPauseCheckInterval;
        var query = EntityQueryEnumerator<KesslerCryoStorageComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (!_container.TryGetContainer(uid, KesslerCryoStorageComponent.ContainerId, out var container))
                continue;

            foreach (var body in container.ContainedEntities)
            {
                if (!MetaData(body).EntityPaused)
                    SetPausedRecursive(body, true);
            }
        }
    }

    /// <summary>
    /// The container that holds stored bodies, on a holder entity on the sector map (or on the map of
    /// <paramref name="near"/> when there is no sector map, as in tests). A separate holder, rather than the map itself,
    /// keeps grid traversal from re-parenting the bodies out of their container.
    /// </summary>
    private bool TryGetKesslerStorage(EntityUid near, out BaseContainer? container)
    {
        container = null;
        EntityUid holder;
        var query = EntityQueryEnumerator<KesslerCryoStorageComponent>();
        if (query.MoveNext(out var existing, out _))
        {
            holder = existing;
        }
        else
        {
            var map = _map.GetMapOrInvalid(_gameTicker.DefaultMap);
            if (!map.IsValid())
                map = Transform(near).MapUid ?? EntityUid.Invalid;

            if (!map.IsValid() || map == _storageMap)
                return false;

            // Far from anything, and without physics, so nothing ever bumps into or traverses it.
            holder = Spawn(null, new EntityCoordinates(map, new System.Numerics.Vector2(-100000, -100000)));
            _metaData.SetEntityName(holder, "cryo storage");
            EnsureComp<KesslerCryoStorageComponent>(holder);
        }

        container = _container.EnsureContainer<Container>(holder, KesslerCryoStorageComponent.ContainerId);
        return true;
    }

    /// <summary>
    /// Moves a body into storage. Falls back to Frontier's storage map when there is no map to store on.
    /// </summary>
    private void KesslerStoreBody(EntityUid body, EntityUid cryopod)
    {
        if (TryGetKesslerStorage(cryopod, out var container) && _container.Insert(body, container!, force: true))
        {
            SetPausedRecursive(body, true);
            return;
        }

        _transform.SetCoordinates(body, new EntityCoordinates(GetStorageMap(), System.Numerics.Vector2.Zero));
    }

    /// <summary>
    /// Wakes a body taken out of storage, before it goes into a cryopod.
    /// </summary>
    private void KesslerPrepareReturn(EntityUid body)
    {
        SetPausedRecursive(body, false);
    }

    private void SetPausedRecursive(EntityUid uid, bool paused)
    {
        _metaData.SetEntityPaused(uid, paused);
        var children = Transform(uid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            SetPausedRecursive(child, paused);
        }
    }

    private void OnBeforeSectorSave(BeforeSectorSaveEvent args)
    {
        var query = EntityQueryEnumerator<KesslerCryoStorageComponent>();
        while (query.MoveNext(out _, out var storage))
        {
            storage.Bodies.Clear();
            foreach (var (userId, bodies) in _storedBodies)
            {
                foreach (var stored in bodies)
                {
                    if (Deleted(stored.Body))
                        continue;

                    storage.Bodies.Add(new KesslerStoredBody
                    {
                        UserId = userId.UserId,
                        Body = stored.Body,
                        Cryopod = stored.Cryopod,
                        Mind = stored.Mind,
                        StationName = stored.StationName,
                        CharacterSlot = stored.CharacterSlot,
                    });
                }
            }
        }
    }

    private void OnKesslerStorageStartup(Entity<KesslerCryoStorageComponent> ent, ref ComponentStartup args)
    {
        // A loaded sector replaces whatever was stored before it.
        if (ent.Comp.Bodies.Count == 0)
            return;

        _storedBodies.Clear();
        foreach (var stored in ent.Comp.Bodies)
        {
            if (!stored.Body.IsValid() || Deleted(stored.Body))
                continue;

            var userId = new NetUserId(stored.UserId);
            if (!_storedBodies.TryGetValue(userId, out var bodies))
                _storedBodies[userId] = bodies = new List<StoredBody>();

            bodies.Add(new StoredBody
            {
                Body = stored.Body,
                Cryopod = stored.Cryopod,
                Mind = stored.Mind,
                StationName = stored.StationName,
                CharacterSlot = stored.CharacterSlot,
            });
        }

        Log.Info($"Restored {ent.Comp.Bodies.Count} bodies in cryo storage from the save");
    }

    /// <summary>
    /// Puts a player's SSD body into cryo storage without them using a pod, for players who have been away for a long
    /// time. Uses a fallback cryopod (or any cryopod) as the pod they will wake up in. Returns false when there is no
    /// cryopod at all.
    /// </summary>
    public bool TryCryoStoreAwayBody(EntityUid body)
    {
        EntityUid? pod = null;
        var fallback = EntityQueryEnumerator<CryoSleepFallbackComponent, CryoSleepComponent>();
        if (fallback.MoveNext(out var fallbackPod, out _, out _))
            pod = fallbackPod;

        if (pod == null)
        {
            var any = EntityQueryEnumerator<CryoSleepComponent>();
            if (any.MoveNext(out var anyPod, out _))
                pod = anyPod;
        }

        if (pod == null)
            return false;

        CryoStoreBody(body, pod.Value, immediate: true, ghost: false);
        return true;
    }

    /// <summary>
    /// Whether this body is in cryo storage.
    /// </summary>
    public bool IsInCryoStorage(EntityUid body)
    {
        return _storedBodies.Values.Any(list => list.Any(s => s.Body == body));
    }
}
