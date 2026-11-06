using Content.Server._Kessler.Characters;
using System.Linq;
using Content.Server._Kessler.Reputation;
using Content.Server.Administration;
using Content.Shared._Kessler.Licences;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._Kessler.Licences;

/// <summary>
/// Lists the licences, what each needs and which you hold; <c>licence buy &lt;id&gt;</c> buys one.
/// </summary>
[AnyCommand]
public sealed class LicenceCommand : LocalizedEntityCommands
{
    [Dependency] private readonly KesslerLicenceSystem _licences = default!;
    [Dependency] private readonly KesslerReputationSystem _reputation = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    [Dependency] private readonly KesslerCharacterSystem _characters = default!;

    public override string Command => "licence";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player || !_characters.TryGetCharacter(player, out var body, out var slot))
        {
            shell.WriteError(Loc.GetString("kessler-licence-no-character"));
            return;
        }

        if (args.Length == 2 && args[0] == "buy")
        {
            if (!_proto.TryIndex<LicencePrototype>(args[1], out var licence))
            {
                shell.WriteError(Loc.GetString("cmd-licence-unknown", ("id", args[1])));
                return;
            }

            var failure = await _licences.TryBuyAsync(body, licence);
            if (failure != null)
                shell.WriteError(failure);
            else
                shell.WriteLine(Loc.GetString("cmd-licence-bought", ("licence", Loc.GetString(licence.Name)), ("fee", licence.Fee)));
            return;
        }

        if (args.Length != 0)
        {
            shell.WriteError(Loc.GetString("cmd-licence-help"));
            return;
        }

        var held = await _licences.LoadAsync(player.UserId, slot);
        var reputation = await _reputation.GetReputationAsync(player.UserId, slot);
        if (held == null || reputation == null)
        {
            shell.WriteError(Loc.GetString("kessler-licence-no-character"));
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var licence in _licences.Licences())
        {
            var name = Loc.GetString(licence.Name);
            if (held.TryGetValue(licence.ID, out var grantedAt))
            {
                shell.WriteLine(Loc.GetString("cmd-licence-held", ("licence", name), ("date", grantedAt.ToString("yyyy-MM-dd"))));
                continue;
            }

            var missing = _licences.MissingRequirements(licence, reputation, held, now);
            shell.WriteLine(missing.Count == 0
                ? Loc.GetString("cmd-licence-available", ("licence", name), ("id", licence.ID), ("fee", licence.Fee))
                : Loc.GetString("cmd-licence-locked", ("licence", name), ("fee", licence.Fee), ("needs", string.Join("; ", missing))));
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(new[] { "buy" }, "[buy]"),
            2 when args[0] == "buy" => CompletionResult.FromHintOptions(
                _proto.EnumeratePrototypes<LicencePrototype>().Select(l => l.ID), "<licence>"),
            _ => CompletionResult.Empty,
        };
    }
}
