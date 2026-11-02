---
name: wf-upstream-edits
description: Change files Wayfarer did not create (upstream SS14, Frontier _NF, or other forks) in a way that survives future upstream merges — partial classes, edit-marker comments, and minimal diffs. Use whenever a change touches C#, YAML, or FTL outside a _WF folder, or when resolving upstream merge conflicts.
license: AGPL-3.0-only
metadata:
  author: Wayfarer 14 contributors
  website: https://wayfarer14.com
  repository: https://gitlab.com/projectwayfarer/wayfarer14
  version: "1.0"
---

# Editing upstream and other forks' files

Wayfarer regularly merges from Frontier. Every line changed in a file we do not own is a future merge conflict, so the goal is to change as little of those files as possible and to make every change easy to find.

## Choose the least invasive option

1. **Add new files in `_WF`** that subscribe to existing events. Many features need no upstream edits at all.
2. **Use a partial class** when you need to add members to an upstream system. Name the file `<Original>.Wayfarer.cs`, place it under `_WF`, and keep the **original namespace** so it compiles into the same class. Existing examples:
   - `Content.Server/_WF/Explosion/ExplosionSystem.Wayfarer.cs` (namespace `Content.Server.Explosion.EntitySystems`)
   - `Content.Server/_WF/Shipyard/Systems/ShipyardSystem.Wayfarer.cs` (namespace `Content.Server._NF.Shipyard.Systems`)
   The original class must already be `partial`; if it is not, adding `partial` is a one-word marked edit.
3. **Edit in place** only when the first two will not work, and keep the edit as small as possible.

## Marking in-place edits

Use the style from `CONTRIBUTING.md`. A reviewer should be able to find every Wayfarer change by searching for `Wayfarer`.

| Situation | Marker |
|---|---|
| One changed or added line | `// Wayfarer` (C#) or `# Wayfarer` (YAML) at the end of the line |
| Changed value | `fireRate: 4 # Wayfarer: 3<4` (old`<`new) |
| Block of added code | `// Wayfarer: reason` before, `// End Wayfarer` after |
| Commented-out line | `# - Bucket # Wayfarer` |
| FTL | Comment on the line above: `# Wayfarer: "Old text"<"New text"` |
| Ported from another repo | Include the source PR in the comment |

```cs
using Content.Client._WF.Emp.Overlays; // Wayfarer

// Wayfarer: ensure signature colour is consistent
if (TryComp<StampComponent>(uid, out var stamp))
    stamp.StampedColor = state.Color;
// End Wayfarer
```

For YAML, mark added components or groups of fields with a block comment, and individual field changes with inline comments.

Existing Frontier markers (`// Frontier`, `# Frontier`) belong to Frontier. Leave them in place, and do not reuse them for Wayfarer changes.

## Rules

- Do not reformat, reorder, or rename anything in an upstream file beyond what the task needs. Whitespace and line-ending churn creates conflicts.
- Never modify `RobustToolbox/`. If `RobustToolbox` appears in a diff, revert it: `git checkout master -- RobustToolbox` (or the matching upstream ref).
- Do not move upstream files into `_WF`.

## Resolving merge conflicts from upstream

- Take upstream's version of the surrounding code, then reapply the marked Wayfarer lines on top.
- If upstream now provides what a Wayfarer edit added, remove our edit rather than keeping both.
- After resolving, search the file for `Wayfarer` to confirm every marked change survived, then build and run tests.
