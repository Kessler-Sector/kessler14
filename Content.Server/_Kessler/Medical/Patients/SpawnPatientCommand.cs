using System.Linq;
using Content.Server.Administration;
using Content.Server.Humanoid.Systems;
using Content.Shared._Kessler.Medical.Conditions;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._Kessler.Medical.Patients;

/// <summary>
/// Spawns a living NPC patient for testing and staff-run events.
/// </summary>
[AdminCommand(AdminFlags.Spawn)]
public sealed class SpawnPatientCommand : LocalizedEntityCommands
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly PatientSystem _patients = default!;
    [Dependency] private readonly RandomHumanoidSystem _humanoid = default!;

    public override string Command => "spawnpatient";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player?.AttachedEntity is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-only-players-can-run-this-command"));
            return;
        }

        MedicalConditionPrototype? condition = null;
        if (args.Length > 0 && !_proto.TryIndex(args[0], out condition))
        {
            shell.WriteError(Loc.GetString("cmd-spawnpatient-unknown", ("condition", args[0])));
            return;
        }

        var patient = _humanoid.SpawnRandomHumanoid("KesslerPatientService", EntityManager.GetComponent<TransformComponent>(player).Coordinates, "patient");
        if (!EntityManager.TryGetComponent<KesslerPatientComponent>(patient, out var comp))
            return;

        if (condition != null)
            _patients.SetCondition((patient, comp), condition.ID);
        else
            _patients.Setup((patient, comp));

        var name = comp.Condition is { } id && _proto.TryIndex(id, out var proto) ? Loc.GetString(proto.Name) : "?";
        shell.WriteLine(Loc.GetString("cmd-spawnpatient-done", ("patient", EntityManager.ToPrettyString(patient)), ("condition", name)));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHintOptions(_proto.EnumeratePrototypes<MedicalConditionPrototype>().Select(c => c.ID), "condition")
            : CompletionResult.Empty;
    }
}
