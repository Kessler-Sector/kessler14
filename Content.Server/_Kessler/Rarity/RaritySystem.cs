using Content.Shared._Kessler.Rarity;
using Content.Shared.Examine;
using Robust.Shared.Prototypes;

namespace Content.Server._Kessler.Rarity;

/// <summary>
/// Shows an item's rarity tier on examine, from the <see cref="RarityPrototype"/> lists.
/// </summary>
public sealed class RaritySystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;

    private readonly Dictionary<string, RarityPrototype> _byEntity = new();
    private readonly Dictionary<string, RarityPrototype?> _resolved = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MetaDataComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        Rebuild();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<RarityPrototype>() || args.WasModified<EntityPrototype>())
            Rebuild();
    }

    private void Rebuild()
    {
        _byEntity.Clear();
        _resolved.Clear();
        foreach (var rarity in _proto.EnumeratePrototypes<RarityPrototype>())
        {
            foreach (var entity in rarity.Entities)
            {
                if (!_byEntity.TryGetValue(entity, out var existing) || existing.Order < rarity.Order)
                    _byEntity[entity] = rarity;
            }
        }
    }

    /// <summary>
    /// The rarity of an entity prototype: its own entry, or that of the nearest listed parent.
    /// </summary>
    public RarityPrototype? GetRarity(string prototype)
    {
        if (_resolved.TryGetValue(prototype, out var cached))
            return cached;

        RarityPrototype? found = null;
        if (_proto.HasIndex<EntityPrototype>(prototype))
        {
            foreach (var proto in _proto.EnumerateParents<EntityPrototype>(prototype, includeSelf: true))
            {
                if (_byEntity.TryGetValue(proto.ID, out found))
                    break;
            }
        }

        _resolved[prototype] = found;
        return found;
    }

    public RarityPrototype? GetRarity(EntityUid uid)
    {
        return MetaData(uid).EntityPrototype is { } proto ? GetRarity(proto.ID) : null;
    }

    private void OnExamined(Entity<MetaDataComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.EntityPrototype is not { } proto || GetRarity(proto.ID) is not { } rarity)
            return;

        args.PushMarkup(Loc.GetString("kessler-rarity-examine",
                ("color", rarity.Color.ToHex()),
                ("rarity", Loc.GetString(rarity.Name))),
            priority: -10);
    }
}
