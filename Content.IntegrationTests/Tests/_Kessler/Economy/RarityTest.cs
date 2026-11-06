using Content.Server._Kessler.Rarity;
using Content.Shared._Kessler.Rarity;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Kessler.Economy;

/// <summary>
/// Items get the rarity tier of their nearest listed prototype, and every listed prototype exists.
/// </summary>
[TestFixture]
public sealed class RarityTest
{
    [Test]
    public async Task TiersFollowParents()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var rarity = server.EntMan.System<RaritySystem>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(rarity.GetRarity("BluespaceMatterBinStockPart")?.ID, Is.EqualTo("Rare"));
                Assert.That(rarity.GetRarity("BluespaceMatterBinStockPart10")?.ID, Is.EqualTo("Rare"), "a stack inherits its part's tier");
                Assert.That(rarity.GetRarity("SuperMatterBinStockPart")?.ID, Is.EqualTo("Superior"));
                Assert.That(rarity.GetRarity("PowerCellAntiqueProto")?.ID, Is.EqualTo("Exotic"));
                Assert.That(rarity.GetRarity("Crowbar"), Is.Null, "ordinary items show nothing");
            });

            foreach (var tier in protoMan.EnumeratePrototypes<RarityPrototype>())
            {
                foreach (var entity in tier.Entities)
                    Assert.That(protoMan.HasIndex<EntityPrototype>(entity), $"{tier.ID} lists {entity}, which doesn't exist");
            }
        });

        await pair.CleanReturnAsync();
    }
}
