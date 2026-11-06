using System.Linq;
using Content.Shared._Kessler.CCVar;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Random;

namespace Content.Server._Kessler.Medical;

/// <summary>
/// Medical bounty corpses that need surgery (docs/kessler/design/medical-science.md, M6). Some arrive with arms or legs
/// detached; the limbs travel with the corpse, and the bounty pays a bonus for each one that is attached again.
/// </summary>
public sealed class BountySurgerySystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;

    private static readonly BodyPartType[] LimbTypes = { BodyPartType.Arm, BodyPartType.Leg };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BountySurgeryComponent, EntGotInsertedIntoContainerMessage>(OnCorpseInserted);
    }

    /// <summary>
    /// Maybe detaches some limbs from a new bounty corpse. Returns the bonus added to the bounty's value, which is lost
    /// again for every limb still missing when it is redeemed.
    /// </summary>
    public int ApplyMissingLimbs(EntityUid corpse)
    {
        if (!HasComp<BodyComponent>(corpse) || !_random.Prob(_cfg.GetCVar(KesslerCVars.MedicalBountyLimbChance)))
            return 0;

        var limbs = LimbTypes
            .SelectMany(type => _body.GetBodyChildrenOfType(corpse, type))
            .Select(p => p.Id)
            .ToList();
        if (limbs.Count == 0)
            return 0;

        _random.Shuffle(limbs);
        var wanted = _random.Next(1, Math.Max(1, _cfg.GetCVar(KesslerCVars.MedicalBountyMaxLimbs)) + 1);
        var surgery = EnsureComp<BountySurgeryComponent>(corpse);
        surgery.BonusPerLimb = _cfg.GetCVar(KesslerCVars.MedicalBountyLimbBonus);

        foreach (var limb in limbs.Take(wanted))
        {
            if (!TryComp<BodyPartComponent>(limb, out var part) || !TryDetach(limb, part))
                continue;

            surgery.Missing.Add(new MissingLimb { Type = part.PartType, Symmetry = part.Symmetry });
            surgery.DetachedParts.Add(limb);
        }

        if (surgery.Missing.Count == 0)
        {
            RemComp<BountySurgeryComponent>(corpse);
            return 0;
        }

        KeepPartsWithCorpse(corpse, surgery);
        return surgery.BonusPerLimb * surgery.Missing.Count;
    }

    private bool TryDetach(EntityUid limb, BodyPartComponent part)
    {
        if (!_container.TryGetContainingContainer((limb, null, null), out var slotContainer))
            return false;

        var containerId = slotContainer.ID;
        if (!containerId.StartsWith(SharedBodySystem.PartSlotContainerIdPrefix))
            return false;

        var slotId = containerId[SharedBodySystem.PartSlotContainerIdPrefix.Length..];
        return _body.DetachPart(slotContainer.Owner, slotId, limb, part: part);
    }

    /// <summary>
    /// What the bounty loses for limbs that are still missing.
    /// </summary>
    public int MissingLimbPenalty(EntityUid corpse)
    {
        if (!TryComp<BountySurgeryComponent>(corpse, out var surgery))
            return 0;

        var missing = 0;
        foreach (var limb in surgery.Missing)
        {
            var attached = _body.GetBodyChildrenOfType(corpse, limb.Type)
                .Any(p => p.Component.Symmetry == limb.Symmetry);
            if (!attached)
                missing++;
        }

        return missing * surgery.BonusPerLimb;
    }

    private void OnCorpseInserted(Entity<BountySurgeryComponent> ent, ref EntGotInsertedIntoContainerMessage args)
    {
        KeepPartsWithCorpse(ent, ent.Comp);
    }

    /// <summary>
    /// Puts the detached limbs in whatever the corpse is in (its rescue pod), so they don't drift off into space.
    /// </summary>
    private void KeepPartsWithCorpse(EntityUid corpse, BountySurgeryComponent surgery)
    {
        if (!_container.TryGetContainingContainer((corpse, null, null), out var container))
            return;

        foreach (var part in surgery.DetachedParts.ToList())
        {
            if (TerminatingOrDeleted(part) || _container.IsEntityInContainer(part))
            {
                surgery.DetachedParts.Remove(part);
                continue;
            }

            if (_container.Insert(part, container))
                surgery.DetachedParts.Remove(part);
        }
    }
}
