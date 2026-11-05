# Medical, chemistry and science: long-term depth

Status: design proposal, no code changed. Written alongside `progression.md` (general framework: stages, reputation and licences, item rarity tiers, economy rates) and the other-departments design. Where this document needs something from the framework it says so, and the full list is in [What this needs from the progression framework](#what-this-needs-from-the-progression-framework).

## Summary

Medical, chemistry and science run out for the same reason: each is one short loop with a fixed answer, everything is unlocked or buyable within hours, and nothing a player learns or builds carries forward.

- **Medical** has one source of work at lowpop: corpses in wreck pods with random damage. Treatment is "bring each damage number to zero", and nothing about the patient needs diagnosing.
- **Chemistry** builds every medicine from 20 base elements that ChemVend sells on day one. Once you know the recipes from the wiki, there is nothing left to find.
- **Science** has about 140 techs worth about 809k points in total. Two or three anomaly vessels earn that in an afternoon. A blueprint costs 300 paper, prints in 5 seconds and never wears out, so one finished ship can supply the sector.

Top recommendations, in build order:

1. **Fix three persistence bugs found during the survey** (medical bounty corpses take their damage twice on load, artifact graphs store `NetEntity` ids, anomaly vessels forget their anomaly). They affect live data now and must be fixed before anything below.
2. **Living patients and a case library (M1, M2).** Generated NPC patients with *conditions* (multi-stage, hidden cause, specific treatment) instead of damage sets. This is the biggest single fix for lowpop medical, and it reuses the bounty redemption flow.
3. **Science currencies beyond points (S1).** Higher-tier techs also need *research data* of a specific kind (anomalous, xenoarchaeological, biological, material, field), each earned by a different activity. This makes the tree long without making it a grind, and it ties science to the other departments.
4. **Blueprint copies with limited runs (S3, the EVE model).** Research unlocks a master blueprint for the researching ship; anything sold is a copy with N runs that costs research points to print. Science ships become ongoing suppliers rather than one-shot vendors.
5. **Tiered chemistry with per-sector formulas (C1, C2).** Basic medicine stays cheap and plentiful. Strong medicine needs rare inputs from exploration and science, and the upper-tier recipes are seeded per sector, so a chemist's knowledge is earned in game and worth something.
6. **Licences gate contracts and supplies, never mechanics (M4).** Anyone can operate. Licensed surgeons, chemists and scientists get the paid cases, the restricted inputs and the reputation.

## Contents

- [What exists today](#what-exists-today)
- [Why it runs out](#why-it-runs-out)
- [What other games do](#what-other-games-do)
- [Design principles](#design-principles)
- [Medical proposals](#medical-proposals)
- [Chemistry proposals](#chemistry-proposals)
- [Science proposals](#science-proposals)
- [How the three feed each other and the economy](#how-the-three-feed-each-other-and-the-economy)
- [Persistence approach](#persistence-approach)
- [Persistence bugs found during the survey](#persistence-bugs-found-during-the-survey)
- [What this needs from the progression framework](#what-this-needs-from-the-progression-framework)
- [Build order](#build-order)
- [Open decisions](#open-decisions)

## What exists today

### Medical bounties

- Prototype: `Content.Shared/_NF/Medical/Prototypes/MedicalBountyPrototype.cs`. A bounty is a `baseReward`, a set of damage types with min/max, value per point and penalty per point, optional reagents injected into the bloodstream, and `maximumDamageToRedeem` (99).
- Data: `Resources/Prototypes/_NF/Catalog/Bounties/medical_bounties.yml`, 17 bounties (blunt/pierce/slash murders, suffocation, plasma fire, spaced, burn mix, shock, acid, spider venom, amatoxin, radiation, rot, ...). Base rewards are 13,500 to 40,500 (Coyote multiplied them by 3), plus 10 to 25 per damage point. A typical bounty pays about 30k to 60k.
- Generation: `Content.Server/_NF/Medical/MedicalBountySystem.cs`. On `ComponentStartup` the corpse picks a random bounty, takes the damage and reagents, and caches `MaxBountyValue`.
- Source: `MedicalPodFilled` (`Resources/Prototypes/_NF/Catalog/Fills/Crates/emergency_pod.yml`) holds one random corpse by job (`Resources/Prototypes/_NF/Entities/Mobs/Corpses/medical_corpse.yml`). The pod is a 3% per-tile entry in wreck debris (`Resources/Prototypes/_NF/Entities/World/Debris/wrecks.yml`). No map places pods directly. So medical work at lowpop depends on someone salvaging wrecks.
- Redemption: `MedicalBountyRedemptionComponent` machine. Pays the station bank (`MedicalBountyBankPaymentComponent`) or spawns cash. The price gun (`MedicalBountyPriceGunSystem.cs`) shows the value.
- Treatment never involves surgery, organs, limbs, diagnosis or time pressure: the corpse is already dead and stays dead until healed under 99 damage.

### Surgery, organs and cybernetics (Shitmed)

- Code: `Content.Shared/_Shitmed/` (Body, Surgery, Cybernetics, Autodoc, Targeting) and `Content.Server/_Shitmed/`.
- Surgeries: `Resources/Prototypes/_Shitmed/Entities/Surgery/surgeries.yml`, about 30: incisions and ribcage, remove part, attach each limb, hand and foot, tend brute and burn wounds, insert item, and remove or insert brain, borg brain, heart, liver, lungs, stomach and eyes. Steps are in `surgery_steps.yml`.
- Organs: generic, felinid and animal sets (`Resources/Prototypes/_Shitmed/Body/Organs/`). Animal bodies exist for carp, dragon, goliath, kobold, laser raptor, monkey and space cobra.
- Cybernetics: eyes (basic, security, medical), Jaws of Life arms, speed legs, dexterity hands (`Body/Organs/cybernetic.yml`, `Body/Parts/cybernetic.yml`).
- Research: `HighEndSurgery` (20k, NFMedical) unlocks the omnimed tool and the Autodoc board (`Resources/Prototypes/_Shitmed/Research/civilianservices.yml`).
- Without Woundmed there is no organ damage, bleeding wound, fracture or infection. Surgery is a fixed procedure, not a response to a problem. That is the gap M2 fills.

### Diseases

There is no disease system. Wizden removed it, and only orphaned prototypes are left: `DiseaseDiagnoser` and `DiagnosisReportPaper` (`Resources/Prototypes/Entities/Structures/Machines/Medical/disease_diagnoser.yml`) and `DiseaseSwab`, `BoxMouthSwab` and `Vaccine` (`Resources/Prototypes/Entities/Objects/Specific/Medical/disease.yml`), plus sounds and emotes. Zombies (`Content.Server/Zombies/`) and inner-body anomalies (`Content.Shared/Anomaly/Components/InnerBodyAnomalyComponent.cs`) are the only "infections".

### Chemistry

- About 490 reagents and 373 reactions across all forks. Medicine has 55 reactions in `Resources/Prototypes/Recipes/Reactions/medicine.yml`, plus 6 in `_NF` and 1 in `_WF`.
- Dispensers are Frontier's jug model (`Resources/Prototypes/Entities/Structures/Dispensers/chem.yml`): the dispenser holds jugs and dispenses from them. Jugs of the 20 base elements are sold by ChemVend (`Resources/Prototypes/Catalog/VendingMachines/Inventories/chemvend.yml`, 4 of each) and cargo crates.
- A few reactions already need inputs from outside chemistry: `Ambuzol` needs zombie blood, `Cognizine` needs carpotoxin, several need plasma, and some need heat (`minTemp` 350 to 540). These are the natural hooks for a rare tier.
- Recipes are shown in full by the guidebook and reagent UI, and they are on the wiki. There is no purity, quality or discovery.

### Research and the tech tree

- Disciplines: the research server (`Resources/Prototypes/Entities/Structures/Machines/research.yml`) supports only Frontier's seven disciplines. Wizden's Industrial, Arsenal, Experimental and Civilian Services are commented out, so their 57 techs (490k points) are unused.
- Active tree (counted from all `type: technology` prototypes):

  | Discipline | Techs | Total cost |
  |---|---|---|
  | NFEngineering | 51 | 260,000 |
  | NFArsenalMercenary | 20 | 151,000 |
  | NFScience | 14 | 118,500 |
  | NFMedical | 12 | 107,500 |
  | NFService | 26 | 99,000 |
  | NFSalvage | 17 | 73,000 |
  | NFArsenalNfsd | 0 | 0 |
  | **Total** | **140** | **809,000** |

  Almost everything is tier 1. Only three techs are tier 2 or 3.
- Tiers and specialisation already exist in the engine and go unused: `TechDisciplinePrototype.tierPrerequisites` (the share of a tier needed before the next opens) and `lockoutTier` with `TechnologyDatabaseComponent.MainDiscipline` (`Content.Shared/Research/Systems/SharedResearchSystem.cs`, lines 150 and 240): once a server picks a main discipline, other disciplines are locked from `lockoutTier` up.
- Point income:
  - Anomalies: `AnomalyComponent.MinPointsPerSecond` 5 to `MaxPointsPerSecond` 50 by severity, times `wf.research.anomaly_multiplier` (0.7), plus a bonus above the growth threshold and the vessel's `PointMultiplier` (`Content.Server/Anomaly/AnomalySystem.cs` line 186, `AnomalySystem.Vessel.cs`). A mid-severity anomaly yields about 20 points a second, about 72k an hour.
  - Artifacts: each node is worth `1.4^((predecessors+1)^1.2) × BasePointValue (1000) × durability factor × 0.9` (`SharedXenoArtifactSystem.Node.cs` around line 409). A depth-0 node is about 1.3k, depth 3 about 5k, depth 5 about 16k. Extraction sums every unlocked node (`Content.Server/Xenoarchaeology/Equipment/ArtifactAnalyzerSystem.cs`).
  - Research disks: 5k/10k/20k/50k points (`Resources/Prototypes/Entities/Objects/Specific/Research/disk.yml`; the 5k disk sells for 1,000). Mono's point disk printer (`Content.Server/_Mono/Research/PointDiskPrinter/`) turns server points into disks, so points can be traded and sold.
  - `ResearchPointSource` (25 a second) exists only on an abstract base.
- Arithmetic: 809k points ÷ (2 to 3 vessels × 72k an hour, plus artifacts) is **about 4 to 6 hours** for the whole tree, which matches the owner's "a couple of hours" for the parts anyone wants.
- Research servers are per ship: about 15 shuttles (`_NF/Shuttles/` investigator, phoenix, pulsar, crescent, lyrae, spectre, stronghold, ...; `_WF/Shuttles/` swan, zenith, icarus) and a few POIs carry one.

### Blueprints

- Frontier does not put research into lathes directly. Research lets the `BlueprintLithograph` (`Resources/Prototypes/_NF/Entities/Structures/Machines/lathe.yml`, `Content.Server/_NF/Lathe/BlueprintLatheSystem.cs`) print blueprints for the unlocked recipes, grouped by discipline packs (`Resources/Prototypes/_NF/Research/blueprints.yml`).
- A blueprint costs **300 paper and 5 seconds**. Inserting it into any lathe with a `BlueprintReceiver` (`Content.Shared/Research/Systems/BlueprintSystem.cs`) adds its recipes for good. There are no charges, no owner and no licence check.
- So one ship that finishes the tree can print every blueprint at almost no cost and sell them across the sector. After that nobody else has a reason to research. This is the "one ship that has them all" problem.

### Anomalies

- 21 anomaly types (`Resources/Prototypes/Entities/Structures/Specific/Anomaly/anomalies.yml`): pyroclastic, gravity, electric, flesh, bluespace, ice, 8 rock variants, flora (two), liquid, shadow, tech (two), santa. Cores in `cores.yml`, anomaly injections (inner-body anomalies) in `anomaly_injections.yml`.
- Generator: `MachineAnomalyGenerator` (`Resources/Prototypes/Entities/Structures/Machines/anomaly_equipment.yml`), 300 Anomalite per anomaly, 5-minute cooldown (`Content.Server/Anomaly/Components/AnomalyGeneratorComponent.cs`). It is on about 26 maps, 11 of them shuttles.
- The loop is always the same: generate, scan, find the particle that stabilises it, link a vessel, wait for points, harvest the core or let it decay.

### Artifacts

- The newer graph-based xenoarchaeology (`Content.Shared/Xenoarchaeology/Artifact/`, `Content.Server/Xenoarchaeology/`): 26 triggers (`Resources/Prototypes/XenoArch/triggers.yml`), 72 effects (`effects.yml`), with node depth, durability, locking and artifexium.
- Found in wreck debris (`RandomArtifactSpawner`, 5% per tile), salvage loot tables and a cargo crate.
- Deep, but each artifact is the same puzzle again: find triggers, unlock nodes, extract. Nothing makes the tenth artifact different from the first.

## Why it runs out

1. **One loop per job, with a solved answer.** Medical bounty: heal to zero. Chemistry: follow the recipe. Anomaly: find the particle. Once learned, every repeat is the same.
2. **Everything is available at once.** Base chems are sold in vending machines, the whole tree costs one afternoon, and every blueprint is printable as soon as it is researched.
3. **Nothing carries forward.** No reputation, no record of a doctor's cases, no recipe knowledge, no collection. On a round-based server that's fine. On a persistent one it removes any reason to keep playing.
4. **Lowpop starves medical.** With few players there are few real patients, and the only generated ones are corpses that a salvager has to find first.
5. **Departments don't need each other.** Science doesn't need medical, medicine needs nothing from science after its techs are bought, and chemistry needs nothing from exploration.
6. **Persistence amplifies it.** Anomaly vessels keep earning while the owner is offline, and blueprints never wear out, so a single head start compounds forever.

## What other games do

- **SS13 virology (/tg/, Baystation, Paradise).** Diseases with symptom lists, stages and transmission; a virologist identifies, splices and cures them. It works because the *problem* is generated and hidden, not because the cure is complex. Weakness: one bad outbreak ruins a round, so it needs containment.
- **Baystation and Paradise medical.** Organ damage, internal bleeding, fractures, infections, chemical overdoses and addictions. Diagnosis tools (body scanner, blood analysis) matter because symptoms don't name the cause. Long-term interest comes from *case variety*, not treatment complexity.
- **EVE Online research and invention.** Blueprint originals (BPO) are researched for efficiency over days. Blueprint copies (BPC) with limited runs are what gets traded. *Invention* combines a copy with datacores for a *chance* at a better (tech 2) blueprint. Research takes slots and real time. This is the model for a market in blueprints that stays alive: originals rarely change hands, and copies are consumed.
- **Roguelike unlocks (Hades, Slay the Spire, Dead Cells).** A codex of things discovered, with first-discovery rewards. Collection gives repeat runs a reason ("I haven't seen that one yet").
- **MMO crafting specialisation (Star Wars Galaxies, Albion, Wurm).** Crafters specialise because skill caps or costs prevent knowing everything. Resource *quality* varies by spawn, so the same recipe yields better or worse items and good crafters and good resource spots are known by name. SWG's player doctors and entertainers had a real economy because NPC and player demand was steady.
- **Kenshi and Rimworld medicine.** Injuries have a cause, location and treatment quality. Infections and prosthetics give long tails. Specialised doctors are a colony asset.
- **Project Zomboid and Escape from Tarkov.** Specific injuries need specific items, and rare medical items come from dangerous places. Medical supply is a reason to explore.

Lessons for Kessler: generate problems, hide causes, let quality vary, make knowledge earned, keep traded goods consumable, keep a codex, and make rare inputs come from somewhere dangerous.

## Design principles

1. **Basic care is always available.** A lone player can always patch themselves up with cheap, plentiful T0 medicine and self-surgery (`surgery.can_operate_on_self`). Depth is in harder cases and better outcomes, not in withholding bandages.
2. **Licences gate contracts and supplies, not mechanics.** Anyone can attempt any surgery or mix any reaction. Licences decide who gets paid NPC work, who can buy restricted inputs, and whose blueprints a lathe accepts for restricted items. SS14 players dislike hard skill locks, and at lowpop hard locks strand people.
3. **Generated work scales inversely with population.** At lowpop the generator fills the gap; at highpop real patients take over.
4. **Traded goods wear out.** Blueprint copies, reagent batches and organs are consumed, so trade continues.
5. **Each department needs inputs from others** for its upper tiers.
6. **Presence matters.** Idle income (anomaly vessels while offline) is capped, so progress tracks play rather than uptime.
7. **Every piece of state has a home that survives a restart**: entity state in `[DataField]`s, character state in the database, sector state on the sector map. See [Persistence approach](#persistence-approach).

## Medical proposals

### M1. Living patients (patient generator)

**What.** A server rule periodically generates living NPC patients with a condition (M2). Patients arrive in three ways:

- **Distress beacons** in space or at POIs: a stranded NPC ship or a pod that a paramedic must reach in time. It shows on the sector map like other waypoints.
- **Clinic referrals**: a patient arrives by shuttle or pod at a registered clinic (a medical ship or POI clinic, M5).
- **Wreck pods**, as today, but with living or newly dead patients as well as corpses.

Patients deteriorate on a schedule (the condition advances stage by stage). Discharge happens at the redemption machine as now, but the payout depends on outcome (alive, stable, organs intact, time taken), and the patient walks away or is collected afterwards (a small NPC exit flow: walk to a pod, despawn after a delay).

**Rate.** Target "one case per active medic per 20 to 40 minutes", inversely weighted by how many real players have needed medical care recently. Cap per clinic and per sector so it can't be farmed by stacking medic alts.

**Why.** It is the single biggest fix for "lowpop doctors only do bounties". Living patients with time pressure turn medical into triage instead of a damage-zeroing chore.

- **Effort:** large (the rule and the delivery are medium; NPC patient behaviour is the long tail).
- **Code:** a new `Content.Server/_Kessler/Medical/PatientGenerator/` rule system. Extend or wrap `MedicalBountySystem` (`_NF`, keep upstream edits small; prefer a `_Kessler` system that reuses `MedicalBountyRedemptionComponent` via an event). Spawn points via existing POI and shuttle spawn code (`NfAdventureRuleSystem`). Patients are humanoid NPCs with a stasis-like idle HTN (`Resources/Prototypes/NPCs/`).
- **Depends on:** M2 (conditions). Reputation and licences from the framework for routing and pay. The sector map waypoint system.
- **Persistence:** patients are mobs, saved by `PersistenceSaveSystem` like any player body. The condition state is `[DataField]`s on the patient (M2). The "deadline" is stored as *remaining duration* that only ticks while the server runs, not as an absolute `CurTime`, because game time restarts after a sector load. The generator's per-clinic cooldowns live on the clinic entity, the sector-wide rate on the sector map entity. Covered by a round-trip test: save a patient mid-condition, load, and check stage and remaining time.

### M2. Conditions: a case library

**What.** A data-driven `medicalCondition` prototype: a named problem with stages, symptoms and required treatment. Symptoms are what tools show; the cause is hidden until diagnosed. Examples built on what exists:

| Condition | Symptoms | Diagnosis | Treatment | Uses |
|---|---|---|---|---|
| Internal bleeding | blood loss with no visible wound, pallor | body scanner shows chest/abdomen | open incision, cauterise (new step), blood transfusion | Shitmed steps, bloodstream |
| Organ failure (liver, lungs, heart, eyes) | toxin buildup, low oxygen, arrhythmia, blindness | health analyzer per-organ status (new) | organ transplant from biofab, donor or market | Shitmed organ surgery, biofabricator |
| Unknown poison | toxin damage that keeps rising | blood sample in a chemical analyzer names the reagent | the specific antidote (not generic Dylovene) | bloodstream, reagent metabolism |
| Parasite / foreign object | stomach pain, periodic damage | scanner shows a foreign body | "remove item" surgery | Shitmed `SurgeryInsertItem` in reverse |
| Cybernetic rejection | random limb failure, shock damage | scanner flags the implant | immunosuppressant (new reagent) or replace the part | Shitmed cybernetics |
| Radiation sickness | hair loss, cellular damage over days | Geiger, blood sample | staged: Hyronalin, then Arithrazine, then rest | existing reagents |
| Xeno infection | odd emotes, mutations | blood sample *plus* science analysis | experimental cure from science (S1 biological data) | inner-body anomaly, artifacts |
| Addiction / withdrawal | tremors, craving | patient history (examine) | tapering doses over real time | status effects |

Each condition has tiers (common to exotic, from the framework's rarity scale). Exotic ones come from exotic places: deep POIs, anomalies, artifacts. Player characters can catch them too (from artifacts, anomalies, xeno mobs), so real patients appear even at lowpop.

**Why.** Turns treatment into a diagnosis puzzle with variety. A doctor who has seen 50 cases still meets one they haven't. This is what Baystation and Paradise medicine gets right.

- **Effort:** large overall, but it can ship in slices (3 to 4 conditions first, then more as YAML).
- **Code:** `Content.Shared/_Kessler/Medical/Conditions/` (condition component and prototype, staging in shared so symptoms predict on the client; validation and payouts on the server). Hooks into status effects, `BloodstreamSystem`, Shitmed `OrganComponent` (an `Enabled` flag already exists; add an organ health value in a `_Kessler` component rather than editing Shitmed), health analyzer UI (already per-part; add per-organ and condition hints). One new Shitmed surgery step or two (cauterise, remove foreign body).
- **Depends on:** nothing for the first slice. Framework rarity tiers for weighting. S1 for xeno conditions.
- **Persistence:** condition component with `[DataField]` stage, remaining stage time and diagnosed flag. Organ health in a `[DataField]`. Avoid `ComponentInit` side effects (see the deep fryer and construction-action notes in KESSLER.md). Round-trip tests per condition.
- **Note on Woundmed:** this overlaps Goob's Woundmed (wounds, bones, pain). M2 is deliberately smaller: a handful of authored conditions, not a simulation. If Woundmed is ever ported, conditions become a layer on top of it. If the owner wants Woundmed, it should land *before* M2 and before the server opens, as KESSLER.md says.

### M3. Outbreaks (virology-lite)

**What.** Occasional sector events: a pathogen starts on an NPC colony or POI and spreads by contact or air between NPCs and players. A pathogen is a condition (M2) with a transmission profile and a randomized symptom set. Curing it is a cross-department arc: medical samples it, science analyses it into biological data (S1), chemistry synthesises the cure (C2). Vaccines are produced and sold. The orphaned `DiseaseDiagnoser`, `DiseaseSwab` and `Vaccine` prototypes can be repurposed.

**Containment.** Outbreaks are rare (every one or two weeks), start away from player ships, can only spread to players who enter the affected area or touch carriers, and have a non-lethal ceiling at lowpop (they debilitate, they don't kill). Admins can start and end them, so they double as a staff story arc.

- **Effort:** large.
- **Code:** `Content.Server/_Kessler/Medical/Outbreaks/` (event rule, transmission), condition prototypes, repurposed disease prototypes.
- **Depends on:** M2. S1 biological data. C2 recipe seeding for the cure.
- **Persistence:** outbreak state (pathogen seed, stage, affected POIs, cure discovered) on the sector map entity, written before save (`BeforeSectorSaveEvent`, as cryo storage does). Infection state on each body via M2.

### M4. Medical licences and specialisation

**What.** Medical uses the framework's licence system with these tracks:

- **First Responder** (paramedic): distress beacons, field stabilisation, rescue fees.
- **General Practitioner**: clinic referrals, common conditions.
- **Surgeon**: organ transplants, limb reattachment, cybernetics installation, brain transfer cases.
- **Pharmacist** (shared with chemistry, C4): sells T2+ medicine to NPCs, buys restricted inputs.
- **Xenomedicine** (late): xeno infections, outbreak work, anomaly hosts.

Licences are earned by *completed cases* of the right kind (logged per character), plus a fee. They unlock contract types, higher payouts and restricted supplies. They do not unlock surgery steps or reactions.

Optional, small: an experience bonus (a modest speed bonus per surgery type after N successful cases, using Shitmed's existing step speed multipliers). Recommend shipping without it first.

- **Effort:** medium (most of it is the framework's licence system; the medical part is case logging and gating contracts).
- **Code:** a case log written by M1/M2 discharge, licence checks in the patient generator and the C4 contract board.
- **Depends on:** the framework's licence and reputation system, which needs per-character storage.
- **Persistence:** per character in the database (keyed by profile ID, like `Profile.BankBalance`), so it survives cryo storage, ship storage and character absence. Never on the body entity alone.

### M5. Clinics and reputation

**What.** A medical ship or POI clinic can register as a clinic at a medical console (owner only, using the ownership records). Clinics get referrals from M1, a rating from outcomes (survival, time, complications), and a counter where patients, NPC or player, are billed automatically. Referrals favour higher-rated clinics nearby. Rating decays slowly without activity, so a clinic has to stay staffed.

- **Effort:** medium.
- **Code:** `Content.Server/_Kessler/Medical/Clinics/`, reusing ownership records (`_Kessler` ownership) and the bank.
- **Depends on:** M1, the ownership system (built), framework reputation.
- **Persistence:** clinic component on the ship's grid entity with `[DataField]` rating and counters, saved with the sector. Ship storage moves the grid, so the rating travels with the ship.

### M6. Better medical bounties (quick wins)

**What.** Before M1/M2, extend the existing corpse bounties so they use Shitmed:

- missing limbs that must be reattached (corpse spawns without a part, part provided or bought),
- a failed organ to transplant,
- a cybernetic to install or remove,
- reagent clearance (bloodstream must be free of the toxin, not just damage zero),
- rare "VIP" bounties at deeper wrecks with higher pay.

Also move bounty corpses into the framework's tier system (deeper wreck = higher tier).

- **Effort:** small to medium (mostly YAML and a few new `MedicalBountyPrototype` fields).
- **Code:** `MedicalBountyPrototype.cs` and `MedicalBountySystem.cs` (upstream `_NF` files: mark edits `// Kessler`, or move the new requirement checks into a `_Kessler` system that listens to redemption), `medical_bounties.yml`.
- **Depends on:** nothing. Do the persistence fix first.
- **Persistence:** see the bounty bug below. New fields must be `[DataField]`s.

### M7. Search and rescue

**What.** The paramedic loop: distress beacons (M1) at hazardous places (spaced, burning, hostile mobs, radiation) with a timer. Pays a rescue fee plus a share of the discharge. Gives paramedics a reason to own fast medical shuttles and rescue gear. Ties to salvage and security (escort) in the other-departments design.

- **Effort:** medium (on top of M1).
- **Depends on:** M1.
- **Persistence:** as M1.

### M8. Organs and cybernetics as goods

**What.** Organs become a commodity with quality and rarity:

- grown in the biofabricator (common quality),
- harvested from mobs (Shitmed already has carp, goliath, dragon and other animal bodies), with exotic organs that give small, specific bonuses,
- cybernetics in tiers (basic printed, advanced from science S3/S4, exotic from loot).

Organs decay unless preserved (stasis bags, freezers, cryo). That creates logistics and a reason for a chilled storage room.

- **Effort:** medium.
- **Code:** organ quality and decay component (`_Kessler`), new organ and cybernetic prototypes, biofab recipes.
- **Depends on:** framework rarity tiers, M2 for transplant demand, S3 for cybernetic blueprints.
- **Persistence:** organs are entities with `[DataField]` quality and remaining freshness. Decay only ticks while the server runs.

## Chemistry proposals

### C1. Reagent tiers

**What.** Sort medicine into tiers by what it needs, not by a lock:

- **T0 basic:** from dispenser elements only (Bicaridine, Kelotane, Dylovene, Dexalin, Inaprovaline, Tricordrazine, ...). Cheap, always available. Keeps principle 1.
- **T1 advanced:** needs intermediates, heat or catalysts, and possibly upgraded machines (centrifuge, electrolysis, heater). Stronger or more specific.
- **T2 rare:** needs at least one input from outside chemistry: carpotoxin, zombie blood, plasma, anomaly cores, artifact by-products (artifexium), xeno organ extracts, rare plants. `Ambuzol` and `Cognizine` already work this way. Move the strongest heals and the condition-specific cures (M2) here.
- **T3 exotic:** needs sector-limited inputs from deep POIs, events or outbreaks (M3), plus a per-sector formula (C2).

**Rebalance to go with it:** keep T0 healing good enough to survive, and give T2 and T3 qualitatively different effects (cure a condition, regrow tissue, reverse organ failure) rather than just bigger numbers.

- **Effort:** small to medium (YAML: new reactions and reagents, retuning a few existing ones; a rare-input source list per tier).
- **Code:** `Resources/Prototypes/_Kessler/Recipes/Reactions/`, `Resources/Prototypes/_Kessler/Reagents/`, and `# Kessler`-marked edits where an existing reaction must change. New harvestable sources may need new mob or plant prototypes.
- **Depends on:** framework rarity tiers (names and colours). The other-departments design for where rare inputs come from (salvage, botany, mining).
- **Persistence:** reagents in solutions are already saved. Nothing new.

### C2. Earned recipes and per-sector formulas

**What.** Players read the wiki, so hiding vanilla recipes doesn't create knowledge. Instead:

- **T0 and T1 stay public** (guidebook and wiki).
- **T2 and T3 are "Kessler formulations":** their exact recipe (which catalyst, which ratio, what temperature band) is *seeded per sector*. The base reagents are known; the variant is not.
- **Ways to learn a formula:**
  1. Research: an NFMedical/NFScience tech produces a formula data disk (S1 biological data).
  2. Analysis: put a sample of the product into a chemical analyzer for a long, costly analysis to reverse-engineer its recipe.
  3. Experimentation: a reaction notebook logs what a chemist has mixed, and near-misses (right reagents, wrong ratio or temperature) are hinted ("the mixture fizzes but doesn't settle").
  4. Purchase: NPC traders or the black market sell formula manuscripts.
- **Knowledge is a per-character record** (the chemist knows it) *and* can be written down as a paper or disk that can be sold or stolen. The guidebook shows a formula only once the viewer knows it.

**Why.** It gives chemists something to find and own, and makes a skilled chemist with a full notebook a real asset. Seeding per sector means a wiki can't spoil it, and keeping it fixed for the life of the sector means knowledge keeps its value.

- **Effort:** medium to large. The seeded reactions are server-side and medium; the per-player guidebook filtering touches shared and client code (sandbox: check new BCL types against `Sandbox.yml`, run `SandboxTest`).
- **Code:** a `_Kessler` reaction-variant system that rewrites the T2/T3 `ReactionPrototype` requirements at load from the sector seed (or a reaction condition that checks the seeded catalyst and temperature band). The reagent guidebook (`Content.Client/Guidebook/`) learns to hide unknown formulas. A chem analyzer machine (or extend an existing one).
- **Depends on:** framework per-character storage (knowledge). S1 (data disks).
- **Persistence:** the sector seed and the seeded variants on the sector map entity (or in `sector.meta.json`). A restart must never re-roll them. Per-character formula knowledge in the database. Formula papers and disks are entities with `[DataField]` content.

### C3. Purity (later)

**What.** Reactions yield a purity based on temperature control, ratio accuracy and machine upgrades. Purity scales effect and sale price, and impure batches have side effects. This is the strongest mastery mechanic (TG SS13 has one) but it changes `Solution`/`ReagentQuantity`, which is shared engine-adjacent code used everywhere.

- **Effort:** large, with high risk.
- **Recommendation:** defer. If wanted, a cheaper version is a batch "grade" stored on the container after a reaction completes (a component on the beaker), read by contracts and the payout, and lost when mixed. It captures most of the market value without touching the solution model.
- **Persistence:** a `[DataField]` on the container.

### C4. Pharmacy contracts

**What.** A pharmacy contract board: NPC orders for reagent batches with quantity, tier, deadline and (with C3) grade. Common orders keep a steady medium income; rare orders need T2/T3 reagents and pay well. Pharmacist licence (M4) unlocks the upper rows. Contracts deliver through the existing cargo or bounty redemption flow.

- **Effort:** small to medium (reuse the cargo bounty system, `Resources/Prototypes/Catalog/Bounties/`, with reagent entries).
- **Depends on:** framework licences and economy rates, C1.
- **Persistence:** the contract board's state on its console or the sector entity (cargo bounty state already lives on the sector service entity, which `PersistenceLoadSystem` relinks).

### C5. Supply

**What.** Keep T0 inputs plentiful (ChemVend restocks as now). Scarcity comes only from T2/T3 inputs. Optionally sell rare elements (gold, silver, uranium, plasma) through cargo at higher prices, so a chemist can trade money for time instead of exploring.

- **Effort:** small (prices and vendor inventories).

## Science proposals

### S1. Research data: more than one currency

**What.** Points stay the general currency, but tier 2 and higher techs also cost **research data** of a specific kind:

| Data type | Earned by | Department link |
|---|---|---|
| Anomalous | stabilising and studying anomalies, harvesting cores by type | science |
| Xenoarchaeological | unlocking artifact nodes at depth 3 or more, rare trigger/effect pairs | science, salvage |
| Biological | autopsies of exotic mobs, rare organs, outbreak samples, completed medical conditions | medical |
| Material | analysing rare ores, alloys and anomaly rock cores | mining |
| Field | surveying POIs, planets and space phenomena with a scanner | exploration |

Data is produced as items (data disks or sample canisters) that are fed into the research server. That makes it tradeable, so a scientist can buy biological data from a doctor or material data from a miner.

**Why.** It makes the tree long without just multiplying costs, which would only lengthen the same grind. It forces several activities, and it links science to the rest of the sector.

- **Effort:** medium.
- **Code:** a `_Kessler` partial or component next to `TechnologyPrototype` with `requiredData` (prototype ID to amount); a data balance on the server (`ResearchServerComponent` partial or a `_Kessler` component on the server entity); research console UI shows the requirement; new data-producing interactions in each source system (anomaly harvest, artifact extraction, autopsy surgery step, scanner).
- **Depends on:** the other-departments design for mining and exploration sources. M2 for biological data from cases.
- **Persistence:** data balances as `[DataField]` on the server entity. Data disks as entities.

### S2. A longer tree: tiers, specialisation and cost

**What.** Use the tier and lockout machinery that is already in the engine:

1. **Re-tier the tree.** Move the strong techs into tiers 2 to 4 with `tierPrerequisites` (for example 0.6 of tier 1 before tier 2). Tier 3 and 4 need S1 data.
2. **Specialisation.** Set `lockoutTier` to 3: a server that picks a main discipline can go to tier 4 in it and up to tier 2 elsewhere. A ship that wants everything needs several servers (several ships), and specialised ships trade blueprint copies with each other (S3).
3. **Add new top-tier techs** where Kessler content needs them: energy surgical tools (owner already wants them behind science), advanced cybernetics, T3 chem formula disks, anomaly containment, artifact-derived equipment.
4. **Scale costs moderately**, about 2 to 3 times for tier 1 and higher for upper tiers, *after* S1 and S9 are in, so the length comes from variety more than from waiting.

**Target length.** A dedicated player playing about 2 hours a day should master one discipline in **3 to 5 weeks** and get most of the useful mid-tier techs in **about 1 week**. Every discipline to tier 4 should be beyond one ship.

- **Effort:** medium (YAML for tiers and costs, a few new techs; balance passes).
- **Code:** `Resources/Prototypes/_NF/Research/techtree.yml` and `disciplines.yml` (upstream files; heavy edits there will conflict on merge, so put re-tiered copies or overrides under `Resources/Prototypes/_Kessler/Research/` where possible). New techs under `_Kessler`.
- **Depends on:** S1, S9, framework stages (so tech tiers line up with progression stages).
- **Persistence:** `TechnologyDatabaseComponent.UnlockedTechnologies` and `MainDiscipline` are already `[DataField]`s. **Migration:** existing servers that have already unlocked a re-tiered tech keep it (don't revoke); new techs need a save migration only if IDs change.

### S3. Blueprint originals and copies (the EVE model)

**What.**

- **Master blueprints (originals).** Researching a tech gives the researching ship's lithograph the ability to print *copies*. A master blueprint item exists only from rare loot or late-tier rewards, has unlimited runs, and is the most valuable item science can own.
- **Blueprint copies.** Everything the lithograph prints is a copy with a number of **runs** (for example 20 for tier 1, 10 for tier 2, 5 for tier 3). Each item the lathe prints uses one run. At zero runs the blueprint stops working (stays as a trophy or recyclable).
- **Copy cost.** Paper plus **research points** per copy, scaled by tier, and S1 data for tier 3 and up. Points are spent by the seller, so selling copies is a business, not free money.
- **Copy research (optional).** Techs that raise runs per copy or lower cost, giving a reason to keep researching after a discipline is complete.
- **Licences.** For restricted items (arsenal, NFSD, high-tier cybernetics) the lathe only accepts the blueprint if the ship owner holds the right licence (framework).

Per-ship research stays as it is (each ship has its own server).

**Why.** It removes the "one ship sells everything once" collapse. The finished science ship becomes a steady supplier whose output is consumed, other ships still have a reason to research for themselves (to avoid buying copies forever), and the blueprint economy keeps moving.

- **Effort:** medium.
- **Code:** `BlueprintComponent` (Wizden, `Content.Shared/Research/Components/`) and `BlueprintLatheSystem` (`_NF`): add runs and a "master" flag in a `_Kessler` component on the blueprint rather than editing `BlueprintComponent`. Hook lathe production (`LatheSystem` finishing an item) to consume a run from the blueprint that provided the recipe, and remove the recipe when runs reach zero (`BlueprintSystem.GetBlueprintRecipes` already rebuilds from contained blueprints). Lithograph cost in `lathe.yml` plus a points cost via `ResearchSystem.ModifyServerPoints`.
- **Depends on:** framework licences and item rarity. S2 for tiers.
- **Persistence:** runs and master flag as `[DataField]`s on the blueprint entity, which sits in the lathe's container and is saved with it. **Migration for existing blueprints:** existing saves have permanent blueprints. Recommend converting them on first load to masters only if few exist (they are a grandfathered reward), otherwise to copies with a generous run count. See the open decisions.

### S4. Invention

**What.** Combine a blueprint copy with research data and a rare component for a *chance* at an improved blueprint: a better variant of the item (a higher rarity tier per the framework, for example more efficient stock parts, better cybernetics, stronger tools). Failure consumes the inputs. Invention techs raise the chance.

- **Effort:** medium to large (needs item variants with rarity; the machine and roll are medium).
- **Code:** an invention machine under `_Kessler/Research/`, improved recipe prototypes.
- **Depends on:** S1, S3, framework rarity tiers (what "better" means for an item).
- **Persistence:** outputs are blueprint entities. An invention in progress stores remaining duration in a `[DataField]`.

### S5. Anomaly variety and escalation

**What.**

- **Traits.** Each anomaly rolls 1 to 3 traits from a pool (volatile: grows faster, pays more; shielded: ignores one particle type; mimic: changes its stabilising particle once; symbiotic: pays more next to another anomaly; leeching: drains APC power). Same 21 types, many more puzzles.
- **Wild anomalies.** The generator makes common anomalies. Higher-tier "wild" anomalies appear as sector events at deep POIs and in open space, with better yields, unique cores and S1 anomalous data. Studying them in place is an expedition.
- **Containment.** Keeping a stable anomaly for days in a containment chamber yields anomalous data over time, with occasional events if containment is neglected. On a persistent server a collection of contained anomalies is a lab's pride and a risk.
- **Codex.** A sector-wide codex records the first discovery of each type and trait combination, crediting the discoverer, with a one-time bonus.
- **Escalation chain.** Supercritical cores are refined into anomaly injections and anomaly-derived equipment; inner-body anomaly hosts become a xenomedicine case type (M2).

- **Effort:** medium.
- **Code:** a trait component and system under `Content.Server/_Kessler/Anomaly/`, wild-anomaly event rule, containment machine, codex on the sector entity.
- **Depends on:** S1, the codex (shared with S6), framework reputation for discovery credit.
- **Persistence:** traits as `[DataField]`s on the anomaly. The codex on the sector map entity. **Requires the anomaly vessel fix below** first.

### S6. Artifact variety and collection

**What.**

- **Origins and rarity.** An artifact's origin (wreck, deep POI, planet, event) sets its graph size, depth and effect pool. Deep-origin artifacts are rarer and worth more.
- **Named artifacts.** A small set of unique artifacts with authored effect chains and lore, one of each per sector, that can be traded, displayed and stolen.
- **Collectors and museums.** NPC collectors buy fully catalogued artifacts. A player museum POI or ship can display them for reputation.
- **Codex.** First discovery of each trigger and effect combination, as in S5.
- **Degradation as a feature.** Durability already exists; tie it to use so a powerful artifact is a consumable asset, not a permanent buff.

- **Effort:** medium.
- **Code:** artifact generation (`Content.Server/Xenoarchaeology/Artifact/XenoArtifactSystem.ProcGen.cs`) gains origin-weighted pools (prefer a `_Kessler` partial), named artifact prototypes, collector NPC and buy flow.
- **Depends on:** S1, framework rarity and reputation, the artifact persistence fix.
- **Persistence:** requires the artifact `NetEntity` fix below. Codex on the sector entity.

### S7. Field research and research projects

**What.**

- **Field scanner.** Survey POIs, planets and space phenomena for field data (S1). Each location gives data once per owner per period, so it rewards travelling rather than camping.
- **Research projects.** Long timed projects in a lab machine ("sequence the carp genome", "map bluespace drift") that need inputs over several days and return data, formula disks or rare blueprints. Projects are what a scientist does between active sessions, but only advance while the scientist feeds them, so they don't run idle.

- **Effort:** medium.
- **Depends on:** S1, the other-departments design (exploration, POIs).
- **Persistence:** project state (inputs delivered, remaining time) in `[DataField]`s on the machine.

### S8. Points as a trade good (keep, but price it)

Mono's point disk printer already lets points become sellable disks. Keep it: it lets a scientist sell research output to ships that do their own blueprint work. Price disks through the framework's economy rates so points don't become a cheaper path than research.

- **Effort:** small (prices).

### S9. Limit idle income

**What.** On a persistent server, anomaly vessels keep earning while their owner is offline, which turns research into uptime rather than play. Two options:

1. **Raw data buffer.** Vessels write into a buffer on the server with a cap (for example 2 hours of output). A scientist has to process it at the console to turn it into points. The buffer fills while you're away, then stops.
2. **Diminishing returns** per additional vessel on one server (for example 100%, 70%, 50%, 35%).

Recommend both: the buffer handles idle time, diminishing returns stop vessel-stacking.

- **Effort:** small.
- **Code:** `ResearchServerGetPointsPerSecondEvent` handling (`Content.Server/Anomaly/AnomalySystem.Vessel.cs`) and a buffer field on the server (`_Kessler` component).
- **Persistence:** buffer as a `[DataField]`.

## How the three feed each other and the economy

```
           rare inputs (cores, artifexium, xeno organs)
   SCIENCE ───────────────────────────────────────────▶ CHEMISTRY
     ▲  │  formula disks, analysis                        │
     │  │                                                 │ T2/T3 medicine, cures,
     │  │ cybernetics, energy tools, organ blueprints     │ immunosuppressants
     │  ▼                                                 ▼
 biological data  ◀──────────────────────────────────  MEDICAL
 (autopsies, samples, completed conditions)              │
                                                         │ healed players, rescues,
   mining: material data, rare elements                  │ outbreak response
   salvage/exploration: artifacts, field data, wrecks    ▼
   botany: medicinal plants                         the sector
```

- **Money in:** NPC patients (M1), rescues (M7), pharmacy contracts (C4), artifact collectors (S6), medical bounties (M6). Rates come from the framework.
- **Money out (sinks):** licences (M4), blueprint copy costs (S3), rare inputs bought from other players and NPCs, invention failures (S4), clinic and lab upkeep through ship upkeep (1% a day of purchase price, already built).
- **Player-to-player trade:** blueprint copies, data disks, formula papers, organs, T2/T3 medicine, artifacts.
- **Rule of thumb for the framework:** at lowpop, a medic, a chemist or a scientist should each earn a living comparable to a salvager from NPC demand alone; at highpop most of their income should come from players.

## Persistence approach

Every proposal above puts its state in one of three places:

| State | Where | Examples |
|---|---|---|
| Entity state | `[DataField]` on the entity, saved with the sector | patient conditions, organ freshness, blueprint runs, anomaly traits, research data balance, project progress |
| Character state | database, keyed by profile ID (via the framework) | licences, case log, formula knowledge, reputation |
| Sector state | component on the sector map entity, written before save | per-sector formula seed, codex, outbreak state, generator rates |

Rules that apply throughout:

- **Timers as remaining duration**, ticking only while the server runs, or as real UTC time where real time is the point (as upkeep does). Never an absolute `CurTime`, which restarts with each loaded round.
- **No `NetEntity` in `[DataField]`s.** Use `EntityUid` (the serializer remaps it on load).
- **No side effects in `ComponentStartup` or `ComponentInit`** that would run again on load. Guard with a saved "initialized" flag.
- **Every new component gets a round-trip test** in `Content.IntegrationTests/Tests/_Kessler/Persistence/`.
- **Seeded content is never re-rolled** on load.

## Persistence bugs found during the survey

Not part of this design, but they affect live data and should be fixed before medical or science work builds on these systems. Each is my reading of the code and needs a round-trip test to confirm.

1. **Medical bounty corpses take their damage twice on load.** `MedicalBountyComponent.BountyInitialized` and `MaxBountyValue` (`Content.Shared/_NF/Medical/MedicalBountyComponent.cs`) are not `[DataField]`s. On load `ComponentStartup` sees `BountyInitialized == false`, picks the saved bounty and applies its damage and reagents again on top of the saved damage. `Bounty` is a `[DataField]` holding a whole `MedicalBountyPrototype` rather than a `ProtoId`, which may not serialize cleanly either. Fix: make both fields `[DataField]`s and store the bounty as a `ProtoId<MedicalBountyPrototype>`.
2. **Artifact graphs store `NetEntity` ids.** `XenoArtifactComponent.CachedActiveNodes`, `CachedSegments` and `NodeVertices`, and `XenoArtifactNodeComponent.Attached`, are `NetEntity` `[DataField]`s. I found no `NetEntity` type serializer in RobustToolbox, so either the component fails to serialize (and `PersistenceSaveSystem` drops the artifact) or it writes ids that point at other entities after load. Fix: store `EntityUid`s and rebuild caches after load.
3. **Anomaly vessels forget their anomaly.** `AnomalyVesselComponent.Anomaly` is `[ViewVariables]` only, not a `[DataField]`, and `AnomalyComponent.ConnectedVessel` likewise. After a load, vessels stop producing points until re-linked with a scanner. Fix: make the vessel's link a `[DataField]` and relink the anomaly side after load (in `PersistenceLoadSystem`).

## What this needs from the progression framework

1. **Per-character progression storage** in the database (profile ID): licences, case and contract history, formula knowledge, reputation. M4, C2, M5 and S5/S6 credit all need it.
2. **Licence system** with tracks, requirements (counts of logged activities plus a fee), and a check API usable from server systems (patient routing, contract boards, lathe blueprint acceptance).
3. **Item rarity tiers** with names, colours and a way to tag an item, reagent, organ, condition, artifact or blueprint with a tier.
4. **Reputation** per character and per ship or organisation (clinic rating, discovery credit).
5. **Economy rates:** target hourly income per activity at low and high population, so NPC patient pay, pharmacy contracts, collector prices, blueprint copy costs, licence fees and point disk prices can be set against salvage and cargo.
6. **Stages:** which tech tiers, reagent tiers and condition tiers belong to which progression stage, so a new player and a veteran doctor see different work.
7. **A sector-wide codex or achievement record** (first discoveries), if the framework wants one shared across departments; otherwise S5/S6 keep their own on the sector entity.

## Build order

**Phase 0: fixes (small, now).**
Fix the three persistence bugs with round-trip tests. Fix nothing else in these systems until they pass.

**Phase 1: quick depth with existing pieces (small to medium, about 2 to 3 weeks of work).**
- M6 Shitmed-based bounty requirements.
- C1 reagent tiers (YAML), C5 supply.
- S9 idle-income limits.
- S2 re-tiering with the existing tier and lockout machinery (without S1 data yet).
- Energy surgical tools moved behind a science tech (the owner's existing plan).

**Phase 2: generated work and blueprint economy (medium to large).**
- M2 first slice: 3 to 4 conditions (internal bleeding, organ failure, unknown poison, foreign body) and the health analyzer changes.
- M1 patient generator (clinic referrals and distress beacons).
- S3 blueprint copies with runs (plus the migration decision for existing blueprints).
- C4 pharmacy contracts.

**Phase 3: framework integration (needs licences, reputation and per-character storage).**
- M4 licences, M5 clinics, M7 search and rescue.
- S1 research data, and S2 tier 3 and 4 with data costs.
- C2 per-sector formulas and earned recipes.

**Phase 4: long tail.**
- S5 anomaly traits, wild anomalies and containment; S6 artifact origins, named artifacts and collectors; shared codex.
- M8 organ and cybernetic goods; S4 invention; S7 field research and projects.
- M3 outbreaks.
- C3 purity, if still wanted.

Phases 1 and 2 don't need the framework and can start as soon as Phase 0 lands. Woundmed, if the owner wants it, belongs before Phase 2's M2 and before the server opens.

## Open decisions

1. **Should living NPC patients exist at all, or only corpses?**
   Recommendation: yes, living patients (M1). It's the main fix for lowpop medical. Start with clinic referrals (simplest), add distress beacons next.

2. **Licences: gate mechanics or only contracts and supplies?**
   Recommendation: contracts and supplies only. Hard skill locks break lowpop self-help and go against SS14 norms. Revisit a small experience speed bonus only if specialisation still feels flat.

3. **Port Woundmed before opening, or go with authored conditions (M2)?**
   Recommendation: authored conditions. They give most of the case variety for a fraction of the persistence work. If Woundmed is wanted, decide before opening, because converting saved bodies later is expensive.

4. **Hide recipes, or seed them per sector?**
   Recommendation: seed T2/T3 per sector and keep T0/T1 public. Hiding vanilla recipes doesn't work against a wiki; seeded formulas do, and they make chemist knowledge valuable.

5. **Re-roll seeded formulas ever (for example each season)?**
   Recommendation: never re-roll during a sector's life. Re-rolling wipes the value of what players learned. A new sector (a full wipe) is the only reset.

6. **What happens to existing permanent blueprints when copies arrive (S3)?**
   Recommendation: convert them on first load to copies with a generous run count (for example 100), and grant one master per discipline the ship has fully researched. Players keep what they earned and the market still starts moving.

7. **Target length for the tech tree.**
   Recommendation: one discipline to the top in 3 to 5 weeks at about 2 hours a day; useful mid-tier in a week; all disciplines beyond one ship (lockout tier 3).

8. **Anomaly income while the owner is offline.**
   Recommendation: a raw-data buffer capped at about 2 hours of output, plus diminishing returns per extra vessel (S9).

9. **Outbreaks: how often, and can they kill?**
   Recommendation: every one to two weeks at most, starting away from player ships, debilitating but not lethal at lowpop, with admin controls so staff can run them as arcs.

10. **Wizden's unused disciplines (Industrial, Arsenal, Experimental, Civilian Services; 57 techs).**
    Recommendation: leave them disabled and mine them for tier 2 to 4 content in the Frontier disciplines, rather than re-enabling four more trees.

11. **Should research points stay tradeable through point disks?**
    Recommendation: yes, priced through the framework's economy rates. Tradeable points let scientists sell output to ships without a science department.
