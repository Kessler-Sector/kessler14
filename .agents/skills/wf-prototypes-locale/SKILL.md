---
name: wf-prototypes-locale
description: Add or change YAML prototypes and Fluent (.ftl) localization in Wayfarer — entities, parents, IDs, file placement, and player-facing strings. Use for new items, structures, reagents, loadouts, traits, or any change under Resources/Prototypes or Resources/Locale.
license: AGPL-3.0-only
metadata:
  author: Wayfarer 14 contributors
  website: https://wayfarer14.com
  repository: https://gitlab.com/projectwayfarer/wayfarer14
  version: "1.0"
---

# Prototypes and localization

## Where files go

- New Wayfarer content: `Resources/Prototypes/_WF/...`, mirroring the upstream folder structure (`Entities/Objects/Devices/`, `Entities/Structures/Machines/`, `Loadouts/`, `Traits/`, ...).
- Strings: `Resources/Locale/en-US/_WF/...`. Locale folders use lowercase or kebab-case names and are organised by concept, not always by prototype path, so search for nearby `.ftl` files before creating a new one.
- Only `en-US` is required. `nl-NL` and `pt-BR` exist but are maintained separately.
- Changing an existing prototype outside `_WF`: mark the change (see `wf-upstream-edits`).

Real pairs to copy from:

- `Resources/Prototypes/_WF/Entities/Structures/Machines/safety_deposit_box.yml` with `Resources/Locale/en-US/_WF/safety-deposit-box.ftl`
- `Resources/Prototypes/_WF/Loadouts/wayfarer_loadout_groups.yml` with `Resources/Locale/en-US/_WF/preferences/loadout-groups.ftl`
- `Resources/Prototypes/_WF/Traits/speech.yml` with `Resources/Locale/en-US/_WF/traits/traits.ftl`

## Writing entity prototypes

```yaml
- type: entity
  parent: BaseItem
  id: ExampleGadget
  name: example gadget
  description: A short, in-universe description.
  components:
  - type: Sprite
    sprite: _WF/Objects/Devices/example_gadget.rsi
    state: icon
  - type: Example
    charges: 3
```

- IDs are PascalCase and unique across the whole repo. Search before choosing one.
- Inherit from an existing parent (`BaseItem`, `BaseMachinePowered`, `BaseStructure`, ...) and only list components that differ. Use `abstract: true` for new shared parents.
- Names are lowercase unless they are proper nouns, matching existing entities.
- Component names drop the `Component` suffix (`ExampleComponent` → `type: Example`).
- Prefer adding a variant next to the related file over creating a new file per entity.

## Strings used from code

- Every popup, examine line, UI label, and chat message goes through `Loc.GetString("key", ("arg", value))`. No hardcoded English.
- Keys are kebab-case and prefixed by feature: `safety-deposit-console-title`.
- Fluent arguments use `{$name}`. Use `{ THE($target) }`, `{ CAPITALIZE(...) }`, and the gender/plural selectors already used in nearby files instead of building sentences in C#.
- Fluent does not allow a comment on the same line as a value. Put comments on the line above.
- Before adding a key, check it does not already exist in `_WF`, `_NF`, or upstream locale; duplicate keys fail at load.

## Renames and removals

Renaming a prototype ID or `DataField` breaks maps, saved data, and YAML in other folders. Search everything under `Resources/` (including `Resources/Maps/`) and list the rename under "Breaking changes" in the MR. Removed prototypes may need a migration entry; search for `migration.yml` usage before deleting.

## Checking your work

```sh
dotnet run --project Content.YAMLLinter/Content.YAMLLinter.csproj --configuration Release
```

Then spawn the entity in-game and check its name, description, sprite, and any popups.
