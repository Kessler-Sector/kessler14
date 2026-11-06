using System.Diagnostics.CodeAnalysis;
using Content.Server.Preferences.Managers;
using Content.Shared._NF.Bank.Components;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Preferences;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._Kessler.Characters;

/// <summary>
/// Works out which saved character a body is: the player and character slot that reputation, licences, contracts and
/// ship ownership are stored against. Everything Kessler stores per character goes through here, so they all agree.
/// </summary>
public sealed class KesslerCharacterSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IServerPreferencesManager _prefs = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;

    public override void Initialize()
    {
        base.Initialize();
        // Before the systems that load per-character data on spawn, so they see the slot.
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawn, before: new[] { typeof(Licences.KesslerLicenceSystem) });
    }

    private void OnPlayerSpawn(PlayerSpawnCompleteEvent ev)
    {
        // Spawning only records the slot when the profile is the very same object as the cached one, so it is missed
        // sometimes. Record it by value instead; the bank system already pays from the selected character either way.
        if (!TryComp<BankAccountComponent>(ev.Mob, out var bank)
            || bank.CharacterSlot >= 0
            || !_prefs.TryGetCachedPreferences(ev.Player.UserId, out var prefs))
        {
            return;
        }

        if (FindSlot(prefs, ev.Profile) is { } slot)
            SetSlot((ev.Mob, bank), slot);
    }

    private static int? FindSlot(PlayerPreferences prefs, ICharacterProfile profile)
    {
        foreach (var (slot, saved) in prefs.Characters)
        {
            if (ReferenceEquals(saved, profile) || saved.MemberwiseEquals(profile))
                return slot;
        }

        return null;
    }

    private static void SetSlot(Entity<BankAccountComponent> body, int slot)
    {
        // Server-only field, not networked.
        body.Comp.CharacterSlot = slot;
    }

    /// <summary>
    /// The player and character slot <paramref name="body"/> belongs to. Works while the player is away from the body
    /// (ghosted or disconnected) as long as their mind still owns it. False for bodies that aren't a saved character.
    /// </summary>
    public bool TryGetSlot(EntityUid body, out NetUserId user, out int slot)
    {
        user = default;
        slot = -1;

        if (!TryComp<BankAccountComponent>(body, out var bank))
            return false;

        if (_mind.TryGetMind(body, out _, out var mind) && mind.UserId is { } mindUser)
            user = mindUser;
        else if (_players.TryGetSessionByEntity(body, out var session))
            user = session.UserId;
        else
            return false;

        if (bank.CharacterSlot >= 0)
        {
            slot = bank.CharacterSlot;
            return true;
        }

        // Same fallback as the bank: the character selected in the lobby. Recorded, so later changes to the
        // selection don't move this body to another character.
        if (!_prefs.TryGetCachedPreferences(user, out var prefs) || prefs.SelectedCharacterIndex < 0)
            return false;

        slot = prefs.SelectedCharacterIndex;
        SetSlot((body, bank), slot);
        return true;
    }

    /// <summary>
    /// The character <paramref name="session"/> is playing: the body their mind owns, even while they are ghosted
    /// (an admin ghost included), and its slot.
    /// </summary>
    public bool TryGetCharacter(ICommonSession session, out EntityUid body, out int slot)
    {
        body = default;
        slot = -1;

        EntityUid? candidate = _mind.TryGetMind(session, out _, out var mind) && mind.OwnedEntity is { } owned
            ? owned
            : session.AttachedEntity;

        if (candidate is not { } found || !TryGetSlot(found, out _, out slot))
            return false;

        body = found;
        return true;
    }

    /// <summary>
    /// The connected session of the player <paramref name="body"/> belongs to, if they're online.
    /// </summary>
    public bool TryGetSession(EntityUid body, [NotNullWhen(true)] out ICommonSession? session)
    {
        session = null;
        return TryGetSlot(body, out var user, out _) && _players.TryGetSessionById(user, out session);
    }
}
