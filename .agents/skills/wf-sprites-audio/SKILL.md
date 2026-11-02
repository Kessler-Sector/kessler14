---
name: wf-sprites-audio
description: Wire up sprites (RSI) and sounds in Wayfarer — folder placement, meta.json and attributions.yml licensing, sprite states, appearance visuals, and sound specifiers. Use when adding or referencing textures or audio, or when RSI validation fails. Does not cover creating art.
license: AGPL-3.0-only
metadata:
  author: Wayfarer 14 contributors
  website: https://wayfarer14.com
  repository: https://gitlab.com/projectwayfarer/wayfarer14
  version: "1.0"
---

# Sprites and audio

**AI-generated art, sprites, and audio are not allowed in this repository.** Agents may reference, organise, and wire up assets that a person provides, fix metadata, and write the YAML and code that use them. Agents must not generate, draw, edit pixels, synthesise sounds, or fetch assets from the internet.

## Sprites (RSI)

An RSI is a folder ending in `.rsi` containing PNG states and a `meta.json`:

```json
{
    "version": 1,
    "license": "CC-BY-SA-3.0",
    "copyright": "Author and source, e.g. 'By cryowyrm, for Wayfarer-14' or a link to the original file",
    "size": { "x": 32, "y": 32 },
    "states": [
        { "name": "icon" },
        { "name": "inhand-left", "directions": 4 },
        { "name": "inhand-right", "directions": 4 }
    ]
}
```

- Wayfarer's own sprites go in `Resources/Textures/_WF/...`; ported ones stay in their source fork's folder.
- `license` and `copyright` are required. Use the license the artist or source gave; never guess or "upgrade" it.
- Every PNG must be listed in `states`, every listed state must have a PNG, and `directions` must match the image layout.
- Reference sprites from prototypes relative to `Resources/Textures/`: `sprite: _WF/Objects/Devices/example.rsi`.

Validate:

```sh
python RobustToolbox/Schemas/validate_rsis.py Resources/
```

(needs `pip install pillow jsonschema`).

## Changing appearance at runtime

Set data on the server or shared side with `SharedAppearanceSystem.SetData(uid, ExampleVisuals.State, value)` using a `[Serializable, NetSerializable]` enum key, and react on the client with a visualizer system or the YAML `GenericVisualizer`. Search for an existing `GenericVisualizer` on a similar entity before writing a client system.

## Audio

- Wayfarer sounds go in `Resources/Audio/_WF/<Category>/` as `.ogg`.
- Each folder has an `attributions.yml`. Add an entry for every new file:

  ```yaml
  - files: ["example_beep.ogg"]
    license: "CC-BY-SA-3.0"
    copyright: "Who made it, and any edits"
    source: "https://link.to/the/original"
  ```

- Reference sounds with a `SoundSpecifier` field (`SoundPathSpecifier` or `SoundCollectionSpecifier`), not hardcoded paths in C#. For several variations of a sound, add a `soundCollection` prototype.
- Play through `SharedAudioSystem` (`PlayPvs`, `PlayPredicted` for predicted actions, `PlayEntity`), with `AudioParams` for volume and variation.
