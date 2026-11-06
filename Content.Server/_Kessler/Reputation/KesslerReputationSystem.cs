using System.Linq;
using System.Threading.Tasks;
using Content.Server._Kessler.Characters;
using Content.Server.Chat.Managers;
using Content.Server.Database;
using Content.Shared._Kessler.Reputation;
using Content.Shared._NF.Bank.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Kessler.Reputation;

/// <summary>
/// Per-character reputation with NPC bodies (docs/kessler/design/progression.md). Awards are sized as minutes of
/// expected effort, about 1 point per minute of useful work. There is no daily cap (owner's decision, 2026-11-06).
/// </summary>
public sealed class KesslerReputationSystem : EntitySystem
{
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly KesslerCharacterSystem _characters = default!;

    /// <summary>
    /// Points needed for levels 1 to 5.
    /// </summary>
    public static readonly int[] LevelThresholds = { 120, 600, 1800, 4800, 12000 };

    private readonly Dictionary<(Guid User, int Slot), int> _profileIds = new();

    public static int GetLevel(int points)
    {
        var level = 0;
        while (level < LevelThresholds.Length && points >= LevelThresholds[level])
            level++;

        return level;
    }

    public static string LevelName(int level) => Robust.Shared.Localization.Loc.GetString($"kessler-reputation-level-{level}");

    /// <summary>
    /// Awards reputation to the character <paramref name="actor"/> is playing. Does nothing for entities without a
    /// connected player and a character's bank account. The award is written to the database in the background.
    /// </summary>
    public void TryAward(EntityUid actor, ProtoId<ReputationTrackPrototype> track, int points, string reason)
    {
        if (points <= 0
            || !_proto.HasIndex(track)
            || !_characters.TryGetSession(actor, out var session)
            || !_characters.TryGetSlot(actor, out _, out var slot))
        {
            return;
        }

        _ = AwardAsync(session, slot, track, points, reason);
    }

    private async Task AwardAsync(ICommonSession session, int slot, ProtoId<ReputationTrackPrototype> track, int points, string reason)
    {
        try
        {
            if (await GetProfileIdAsync(session.UserId, slot) is not { } profileId)
                return;

            var total = await _db.AddReputationAsync(profileId, track, points);
            if (total == null)
            {
                // The character in that slot was deleted and remade since the id was cached; look it up again.
                _profileIds.Remove((session.UserId.UserId, slot));
                if (await GetProfileIdAsync(session.UserId, slot) is not { } freshId
                    || freshId == profileId
                    || await _db.AddReputationAsync(freshId, track, points) is not { } freshTotal)
                {
                    return;
                }

                total = freshTotal;
            }

            var name = Loc.GetString(_proto.Index(track).Name);
            _chat.DispatchServerMessage(session, Loc.GetString("kessler-reputation-gained",
                ("points", points), ("track", name), ("reason", reason)));

            var level = GetLevel(total.Value);
            if (level > GetLevel(total.Value - points))
            {
                _chat.DispatchServerMessage(session, Loc.GetString("kessler-reputation-level-up",
                    ("track", name), ("level", LevelName(level))));
            }
        }
        catch (Exception e)
        {
            Log.Error($"Failed to award {points} {track} reputation to {session.Name}: {e}");
        }
    }

    /// <summary>
    /// Awards reputation to every player on <paramref name="map"/>, e.g. everyone on an expedition when it is
    /// completed. Crews earn it together.
    /// </summary>
    public void AwardEveryoneOnMap(EntityUid map, ProtoId<ReputationTrackPrototype> track, int points, string reason)
    {
        var query = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapUid == map)
                TryAward(uid, track, points, reason);
        }
    }

    /// <summary>
    /// A character's reputation points per track, or null if the character has no profile in the database.
    /// </summary>
    public async Task<Dictionary<string, int>?> GetReputationAsync(NetUserId user, int slot)
    {
        if (await GetProfileIdAsync(user, slot) is not { } profileId)
            return null;

        return await _db.GetReputationAsync(profileId);
    }

    public IEnumerable<ReputationTrackPrototype> Tracks()
    {
        return _proto.EnumeratePrototypes<ReputationTrackPrototype>().OrderBy(t => t.Order);
    }

    private async Task<int?> GetProfileIdAsync(NetUserId user, int slot)
    {
        if (_profileIds.TryGetValue((user.UserId, slot), out var cached))
            return cached;

        var id = await _db.GetProfileIdAsync(user, slot);
        if (id is { } found)
            _profileIds[(user.UserId, slot)] = found;

        return id;
    }
}
