---
name: wf-porting
description: Port a feature, fix, or asset from another SS14 fork into Wayfarer with correct licensing, attribution, and folder placement. Use when the task mentions porting, cherry-picking, or copying code or sprites from another repository.
license: AGPL-3.0-only
metadata:
  author: Wayfarer 14 contributors
  website: https://wayfarer14.com
  repository: https://gitlab.com/projectwayfarer/wayfarer14
  version: "1.0"
---

# Porting from other repositories

Wayfarer already contains code from many forks. [LEGAL.md](../../../LEGAL.md) lists every source folder, its repository, and its license. Porting is welcome, but licensing and credit must be correct.

## 1. Check the license before copying anything

- Find the source repository's license, and the license header or `meta.json` / `attributions.yml` of the specific files. Some forks mix licenses by folder.
- Compatible with this AGPLv3 project: AGPLv3, GPLv3, and MIT (MIT code must keep its license notice).
- Assets carry their own licenses, typically CC-BY-SA 3.0. Some are non-commercial (CC-BY-NC-SA); keep the license field exactly as the source has it.
- If the license is missing, unclear, or says anything like "restricted", "all rights reserved", or "no permission": stop and ask the contributor. Do not port it.

## 2. Preserve authorship

- When the source change is a clean commit, use `git cherry-pick -x <sha>`. This keeps the original author and records the source commit.
- If you must copy by hand, credit the original author and link the source PR in the commit message and MR.
- Never strip copyright lines, SPDX headers, or attribution metadata.

## 3. Keep ported code in its source folder

- Use the fork's existing folder if LEGAL.md already lists one (`_Mono`, `_HL`, `_DV`, `_Goobstation`, ...).
- If the fork is new to Wayfarer, create `_<ForkName>` folders and add a row to the LEGAL.md attributions table with the repository URL and license.
- Wayfarer-specific adjustments to ported code go in `_WF` or are marked as edits (see `wf-upstream-edits`), so the next port from the same fork stays easy.

## 4. Adapt, do not dump

- Ported code often depends on systems that exist under different names here. Search for the Wayfarer or Frontier equivalent before porting a dependency.
- Remove or translate locale for languages Wayfarer does not ship; `en-US` is required.
- Check the feature fits Wayfarer's setting and balance; mention balance impact in the MR.

## 5. Verify

Build, run the tests and YAML linter, validate any RSIs, and test in-game. In the MR, link the source PR(s), name the original author(s), and state the license.
