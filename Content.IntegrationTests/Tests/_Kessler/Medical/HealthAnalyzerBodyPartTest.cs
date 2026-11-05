using System.Linq;
using Content.Server.Medical;
using Content.Server.Medical.Components;
using Content.Shared._Shitmed.Targeting;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Kessler.Medical;

/// <summary>
/// The health analyzer (and the PDA cartridge, which uses the same system) reports how hurt each body part is, and the
/// damage of a selected part.
/// </summary>
[TestFixture]
public sealed class HealthAnalyzerBodyPartTest
{
    private static readonly ProtoId<DamageTypePrototype> Blunt = "Blunt";

    [Test]
    public async Task ReportsBodyPartsAndSelectedPart()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var analyzerSystem = entMan.System<HealthAnalyzerSystem>();
        var body = entMan.System<SharedBodySystem>();
        var damageable = entMan.System<DamageableSystem>();

        var map = await pair.CreateTestMap();

        EntityUid human = default;
        EntityUid analyzer = default;
        await server.WaitPost(() =>
        {
            human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            analyzer = entMan.SpawnEntity("HandheldHealthAnalyzer", map.GridCoords);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var blunt = server.ProtoMan.Index<DamageTypePrototype>(Blunt);
            damageable.TryChangeDamage(human, new DamageSpecifier(blunt, 40), true,
                targetPart: TargetBodyPart.LeftArm);

            var leftArm = body.GetBodyChildrenOfType(human, BodyPartType.Arm, symmetry: BodyPartSymmetry.Left).Single();
            Assert.That(entMan.GetComponent<DamageableComponent>(leftArm.Id).TotalDamage.Float(), Is.GreaterThan(0),
                "the targeted arm took damage");

            var comp = entMan.GetComponent<HealthAnalyzerComponent>(analyzer);
#pragma warning disable RA0002
            comp.ScannedEntity = human;
#pragma warning restore RA0002

            var state = analyzerSystem.GetHealthAnalyzerUiState(analyzer, human);
            Assert.That(state.Body, Is.Not.Null, "the analyzer reports body parts for a human");
            Assert.That(state.Body![TargetBodyPart.LeftArm], Is.Not.EqualTo(TargetIntegrity.Healthy), "the left arm shows as hurt");
            Assert.That(state.Body[TargetBodyPart.RightArm], Is.EqualTo(TargetIntegrity.Healthy), "the right arm shows as healthy");
            Assert.That(state.Part, Is.Null, "no part selected yet");

#pragma warning disable RA0002
            comp.CurrentBodyPart = leftArm.Id;
#pragma warning restore RA0002
            state = analyzerSystem.GetHealthAnalyzerUiState(analyzer, human);
            Assert.That(state.Part, Is.EqualTo(entMan.GetNetEntity(leftArm.Id)), "the selected part is reported");
        });

        await pair.CleanReturnAsync();
    }
}
