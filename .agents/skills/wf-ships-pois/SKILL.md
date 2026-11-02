---
name: wf-ships-pois
description: Add or change purchasable ships (vessel prototypes) and points of interest in Wayfarer — prototype fields, map paths, pricing rules, and submission requirements. Use when working under Resources/Prototypes/_WF/Shipyard, Resources/Prototypes/_WF/PointsOfInterest, or Resources/Maps/_WF.
license: AGPL-3.0-only
metadata:
  author: Wayfarer 14 contributors
  website: https://wayfarer14.com
  repository: https://gitlab.com/projectwayfarer/wayfarer14
  version: "1.0"
---

# Ships and points of interest

Maps themselves are built in the in-game mapping tools, not written by hand. Agents should help with the prototypes, pricing math, and requirement checks around a map, and must not hand-generate or reformat map YAML.

## Ships

Each ship is a `vessel` prototype plus a `gameMap`, usually in one file under `Resources/Prototypes/_WF/Shipyard/<Group>/`, with the map in `Resources/Maps/_WF/Shuttles/<Group>/`. Copy an existing ship in the same group, for example `Resources/Prototypes/_WF/Shipyard/Cgp/bucklervox.yml`:

```yaml
- type: vessel
  id: BucklerV
  parent: BaseVessel
  name: UCA Buckler-V
  description: ...
  price: 26590
  category: Small
  group: Security
  access: Security
  shuttlePath: /Maps/_WF/Shuttles/Cgp/bucklervox.yml
  guidebookPage: ShipyardBucklerV
  class: [ Pursuit, Fighter ]
  engine: [ APU ]

- type: gameMap
  id: BucklerV
  mapName: 'Buckler-V'
  mapPath: /Maps/_WF/Shuttles/Cgp/bucklervox.yml
  ...
```

- `shuttlePath` and `mapPath` must point at the same existing map file.
- The `id` is shared by the vessel and the gameMap.
- Use `group`, `category`, `class`, and `engine` values that already appear in other vessels.

### Submission rules

`SHIP_SUBMISSION_REQUIREMENTS.md` is the source of truth; read it before reviewing or preparing a ship. Key points:

- One primary and at most one secondary role; at most four subdepartments.
- Hard requirements include being airtight, a two-stage docking airlock, shuttle console, fax, holopad, gyroscope, gravity generator, full power, safety equipment, LateJoin spawn and warp point, and vacuum `AtmosFixMarker` on space-exposed tiles.
- The mapper should run `fixrotations` and `fixgridatmos`, and test buying and selling at a shipyard.

### Pricing

From `SHIP_SUBMISSION_REQUIREMENTS.md`, on an initialized ship:

1. `gridtc <gridId>` tile count × 50
2. plus `appraisegrid <gridId>` value
3. × 1.15 for each secondary role and each subdepartment (stacked additively: two subdepartments plus a secondary role is × 1.45)
4. round to the nearest thousand

Agents cannot run these commands; ask the contributor for the two numbers, then do the arithmetic.

## Points of interest

POIs use `pointOfInterest` prototypes under `Resources/Prototypes/_WF/PointsOfInterest/`, with maps in `Resources/Maps/_WF/POI/`. Copy an existing one such as `array.yml`: it sets `parent: BasePOI`, distance range, `gridPath`, and a matching `gameMap`. Keep the author-info comment header used by existing POI files.

## Coordination

Map conflicts cannot be merged. Before changing an existing ship or POI map, the contributor should check with the map's maintainer or author, and avoid having two open MRs touching the same map.

## Checking your work

```sh
python Tools/validate_mapfiles.py
dotnet test --no-build --configuration DebugOpt Content.IntegrationTests/Content.IntegrationTests.csproj --filter "FullyQualifiedName~ShipyardTest" -- NUnit.ConsoleOut=0 NUnit.MapWarningTo=Failed
```
