using System.Collections.Generic;
using System.Linq;
using Content.Server._Kessler.Persistence;
using Content.Server.Anomaly;
using Content.Server.Anomaly.Components;
using Content.Shared.Anomaly;
using Content.Shared.Anomaly.Components;
using Content.Shared.Interaction;
using Content.Shared.Xenoarchaeology.Artifact;
using Content.Shared.Xenoarchaeology.Artifact.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Kessler.Persistence;

/// <summary>
/// Science equipment that links entities together must still be linked after a persistent save and load.
/// </summary>
[TestFixture]
public sealed class ScienceRoundTripTest
{
    /// <summary>
    /// An artifact keeps its node graph: the same nodes, edges, active nodes and segments, all pointing at the loaded
    /// node entities, and every node still attached to the loaded artifact.
    /// </summary>
    [Test]
    public async Task XenoArtifactKeepsNodeGraph()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entManager = server.EntMan;
        var snapshots = entManager.System<SectorSnapshotSystem>();
        var artifacts = entManager.System<SharedXenoArtifactSystem>();
        var containers = entManager.System<SharedContainerSystem>();

        var testMap = await pair.CreateTestMap();

        EntityUid artifact = default;
        await server.WaitPost(() =>
        {
            artifact = entManager.SpawnEntity("ComplexXenoArtifact", testMap.GridCoords);
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            // A fresh artifact has no active nodes until one is unlocked.
            var comp = entManager.GetComponent<XenoArtifactComponent>(artifact);
            var root = artifacts.GetAllNodes((artifact, comp)).MinBy(node => node.Comp.Depth);
            artifacts.SetNodeUnlocked((artifact, comp), root);

            var before = Graph(entManager, artifacts, artifact);
            Assert.That(before.Nodes, Is.Not.Empty, "the artifact generated nodes");
            Assert.That(before.Active, Is.Not.Empty, "the artifact has active nodes");

            var loadedMap = RoundTripTest.SaveAndLoad(entManager, testMap.MapUid,
                new ResPath("/kessler-roundtrip-test-artifact.yml"));
            var loaded = snapshots.CollectTree([loadedMap])
                .Single(uid => entManager.HasComponent<XenoArtifactComponent>(uid));

            var after = Graph(entManager, artifacts, loaded);
            comp = entManager.GetComponent<XenoArtifactComponent>(loaded);
            var container = containers.GetContainer(loaded, XenoArtifactComponent.NodeContainerId);
            var loadedNet = entManager.GetNetEntity(loaded);
            Assert.Multiple(() =>
            {
                Assert.That(after.Nodes, Is.EqualTo(before.Nodes), "same nodes at the same indices");
                Assert.That(after.Edges, Is.EqualTo(before.Edges), "same edges");
                Assert.That(after.Active, Is.EqualTo(before.Active), "same active nodes");
                Assert.That(after.Segments, Is.EqualTo(before.Segments), "same segments");
                foreach (var node in artifacts.GetAllNodes((loaded, comp)))
                {
                    Assert.That(container.Contains(node), $"{entManager.ToPrettyString(node)} is in the loaded artifact");
                    Assert.That(node.Comp.Attached, Is.EqualTo(loadedNet), $"{entManager.ToPrettyString(node)} is attached to the loaded artifact");
                }
                Assert.That(container.ContainedEntities, Has.Count.EqualTo(before.Nodes.Count), "no nodes were added");
            });
        });

        await pair.CleanReturnAsync();
    }

    private sealed record ArtifactGraph(
        List<string> Nodes,
        List<string> Edges,
        List<int> Active,
        List<List<int>> Segments);

    /// <summary>
    /// Describes an artifact's graph by node index, so graphs on different entities can be compared.
    /// </summary>
    private static ArtifactGraph Graph(IEntityManager entManager, SharedXenoArtifactSystem artifacts, EntityUid uid)
    {
        var comp = entManager.GetComponent<XenoArtifactComponent>(uid);
        var ent = new Entity<XenoArtifactComponent>(uid, comp);

        var nodes = new List<string>();
        var edges = new List<string>();
        foreach (var index in artifacts.GetAllNodeIndices(ent))
        {
            var node = artifacts.GetNode(ent, index);
            var proto = entManager.GetComponent<MetaDataComponent>(node).EntityPrototype?.ID;
            nodes.Add($"{index}: {proto} depth {node.Comp.Depth} locked {node.Comp.Locked} durability {node.Comp.Durability}");
            foreach (var successor in artifacts.GetDirectSuccessorNodes((uid, comp), index).Order())
            {
                edges.Add($"{index} -> {successor}");
            }
        }

        int IndexOf(NetEntity net) => artifacts.GetIndex(ent, entManager.GetEntity(net));

        return new ArtifactGraph(
            nodes,
            edges,
            comp.CachedActiveNodes.Select(IndexOf).Order().ToList(),
            comp.CachedSegments.Select(segment => segment.Select(IndexOf).Order().ToList()).ToList());
    }

    /// <summary>
    /// A vessel with an anomaly stays linked to it, both ways, so it keeps earning points and the anomaly cannot be
    /// linked to a second vessel.
    /// </summary>
    [Test]
    public async Task AnomalyVesselKeepsAnomaly()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entManager = server.EntMan;
        var snapshots = entManager.System<SectorSnapshotSystem>();
        var appearance = entManager.System<SharedAppearanceSystem>();

        var testMap = await pair.CreateTestMap();

        await server.WaitPost(() =>
        {
            var vessel = entManager.SpawnEntity("MachineAnomalyVessel", testMap.GridCoords);
            var anomaly = entManager.SpawnEntity("AnomalyPyroclastic", testMap.GridCoords);
            var scanner = entManager.SpawnEntity("AnomalyScanner", testMap.GridCoords);

            entManager.System<AnomalySystem>().UpdateScannerWithNewAnomaly(scanner, anomaly);
            entManager.EventBus.RaiseLocalEvent(vessel, new InteractUsingEvent(scanner, scanner, vessel, testMap.GridCoords));
            Assert.That(entManager.GetComponent<AnomalyVesselComponent>(vessel).Anomaly, Is.EqualTo(anomaly),
                "the scanner linked the vessel");

            var loadedMap = RoundTripTest.SaveAndLoad(entManager, testMap.MapUid,
                new ResPath("/kessler-roundtrip-test-anomaly-vessel.yml"));
            var tree = snapshots.CollectTree([loadedMap]).ToList();
            var loadedVessel = tree.Single(uid => entManager.HasComponent<AnomalyVesselComponent>(uid));
            var loadedAnomaly = tree.Single(uid => entManager.HasComponent<AnomalyComponent>(uid));

            Assert.Multiple(() =>
            {
                Assert.That(entManager.GetComponent<AnomalyVesselComponent>(loadedVessel).Anomaly, Is.EqualTo(loadedAnomaly));
                Assert.That(entManager.GetComponent<AnomalyComponent>(loadedAnomaly).ConnectedVessel, Is.EqualTo(loadedVessel));
                Assert.That(appearance.TryGetData<bool>(loadedVessel, AnomalyVesselVisuals.HasAnomaly, out var hasAnomaly) && hasAnomaly,
                    "the loaded vessel shows its anomaly");
            });
        });

        await pair.CleanReturnAsync();
    }
}
