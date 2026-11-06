using Content.Server.Administration;
using Content.Shared._Kessler.Reputation;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._Kessler.Reputation;

/// <summary>
/// Shows the reputation of the character you are playing.
/// </summary>
[AnyCommand]
public sealed class ReputationCommand : LocalizedEntityCommands
{
    [Dependency] private readonly KesslerReputationSystem _reputation = default!;

    public override string Command => "reputation";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { AttachedEntity: { } body } player
            || !EntityManager.TryGetComponent<BankAccountComponent>(body, out var bank)
            || bank.CharacterSlot < 0)
        {
            shell.WriteError(Loc.GetString("cmd-reputation-no-character"));
            return;
        }

        var points = await _reputation.GetReputationAsync(player.UserId, bank.CharacterSlot);
        if (points == null)
        {
            shell.WriteError(Loc.GetString("cmd-reputation-no-character"));
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-reputation-header", ("name", EntityManager.GetComponent<MetaDataComponent>(body).EntityName)));
        foreach (var track in _reputation.Tracks())
        {
            var value = points.GetValueOrDefault(track.ID);
            var level = KesslerReputationSystem.GetLevel(value);
            var next = level < KesslerReputationSystem.LevelThresholds.Length
                ? Loc.GetString("cmd-reputation-next", ("points", KesslerReputationSystem.LevelThresholds[level]))
                : string.Empty;
            shell.WriteLine(Loc.GetString("cmd-reputation-line",
                ("track", Loc.GetString(track.Name)),
                ("level", KesslerReputationSystem.LevelName(level)),
                ("points", value),
                ("next", next)));
        }
    }
}

/// <summary>
/// Gives reputation to the character a player is playing, for testing and staff use.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed class ReputationAwardCommand : LocalizedEntityCommands
{
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly KesslerReputationSystem _reputation = default!;

    public override string Command => "reputationaward";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 3 || !int.TryParse(args[2], out var points) || points <= 0)
        {
            shell.WriteError(Loc.GetString("cmd-reputationaward-help"));
            return;
        }

        if (!_players.TryGetSessionByUsername(args[0], out var session) || session.AttachedEntity is not { } body)
        {
            shell.WriteError(Loc.GetString("cmd-reputationaward-no-player"));
            return;
        }

        if (!_proto.HasIndex<ReputationTrackPrototype>(args[1]))
        {
            shell.WriteError(Loc.GetString("cmd-reputationaward-no-track"));
            return;
        }

        _reputation.TryAward(body, args[1], points, Loc.GetString("cmd-reputationaward-reason"));
        shell.WriteLine(Loc.GetString("cmd-reputationaward-done"));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(CompletionHelper.SessionNames(players: _players), "<player>"),
            2 => CompletionResult.FromHintOptions(CompletionHelper.PrototypeIDs<ReputationTrackPrototype>(proto: _proto), "<track>"),
            3 => CompletionResult.FromHint("<points>"),
            _ => CompletionResult.Empty,
        };
    }
}
