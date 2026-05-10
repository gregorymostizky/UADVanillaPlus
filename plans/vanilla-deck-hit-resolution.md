# Vanilla Deck Hit Resolution

## Scope

This note summarizes the current local research into vanilla Ultimate Admiral:
Dreadnoughts deck hits and deck armor resolution for UAD:VP planning.

The short answer is: vanilla deck armor is not just visual. The damage
calculation has a horizontal/deck armor path. The uncertain part is whether the
game has a true physical "the shell ray hit this exact deck collider" contract,
or whether it derives deck-vs-side armor from the simplified `HitPrecalc.sideHit`
classification.

Important local sources:

- `E:\Codex\cpp2il_uad_diffable\DiffableCs\Assembly-CSharp\Ship.cs`
- `E:\Codex\cpp2il_uad_isil\IsilDump\Assembly-CSharp\Ship.txt`
- `E:\Codex\cpp2il_uad_diffable\DiffableCs\Assembly-CSharp\Shell.cs`
- `E:\Codex\cpp2il_uad_isil\IsilDump\Assembly-CSharp\Shell.txt`
- `E:\Codex\cpp2il_uad_diffable\DiffableCs\Assembly-CSharp\PenetrationData.cs`
- `E:\Codex\cpp2il_uad_diffable\DiffableCs\Assembly-CSharp\Effect.cs`
- `E:\Codex\UADRealismDIP\TweaksAndFixes\Harmony\Ship.cs`

## What Vanilla Clearly Has

- `Ship.A` includes `Deck`, `DeckBow`, `DeckStern`, and
  `InnerDeck_1st` through `InnerDeck_3rd` armor zones.
- `PenetrationData` carries separate `vert_*` and `horiz_*` penetration curves.
- `Ship.HitPrecalc` stores `sideHit`, `section`, `part`,
  `willHitConningTower`, and `willHitFireControl`.
- `Ship.GetPenetration(..., sideHit, ...)` switches between the vertical and
  horizontal penetration datasets. In the inspected ISIL, `sideHit == true`
  follows the vertical path, while `sideHit == false` follows the horizontal
  path.
- `Ship.TakeHitRaw(...)` uses `HitPrecalc.sideHit` when resolving armor. When
  `sideHit == false`, the code enters the deck branch: it evaluates
  `A.Deck`, then can continue through `InnerDeck_1st`, `InnerDeck_2nd`, and
  `InnerDeck_3rd` if citadel armor layers exist. When `sideHit == true`, the
  analogous path uses `A.Belt` and the inner belt layers.
- `Effect.Dec` includes `PenDeck` and `NonPenDeck`, so vanilla also has
  presentation categories for deck penetration and deck non-penetration.

## How Vanilla Calculates `sideHit`

The key decision is not made by `FindDeckAtPoint(...)`. The shell damage path
computes an `angleSideUp`, converts that into a side-hit chance, then rolls that
chance with `Util.Chance(...)` and stores the boolean in `HitPrecalc.sideHit`.

`Ship.TakeHitRaw(...)` does the relevant setup:

1. Reads the target ship transform's `up` vector.
2. Calls `Shell.ImpactForwardToAngleSideUp(shellImpactForward, targetUp)`.
3. Passes the result into
   `Ship.CalcSideHitChance(angleSideUp, targetShip, firingPartData)`.
4. Calls `Util.Chance(sideHitChance)` and writes the result to
   `HitPrecalc.sideHit`.

`Shell.ImpactForwardToAngleSideUp(...)` is effectively:

```text
rawAngle = Vector3.Angle(-targetUp, shellImpactForward)
rawAngle = Clamp(rawAngle, 0, 90)
angleSideUp = Clamp((90 - rawAngle) * 3, 0, 89)
```

So near-horizontal impacts produce an `angleSideUp` near `0`, while steeply
descending impacts get pushed toward `89`. The `* 3` is important: vanilla
exaggerates the verticalness of a descending shell before deciding side vs deck.

`Ship.CalcSideHitChance(...)` then behaves like this:

```text
sideComponent = Clamp(Cos(angleSideUp * Deg2Rad), 0.01, 1)
deckComponent = Clamp(2 * Sin(angleSideUp * Deg2Rad) * deckHitRatio, 0.01, 1)

sideHitChancePercent = sideComponent * 100 / (sideComponent + deckComponent)
```

If the target ship has matching turret-caliber data for the firing part,
`deckHitRatio` is derived from gun length:

```text
deckHitRatio = Remap(
    currentGunLengthMod,
    minGunLengthMod,
    techMaxGunLengthMod,
    gun_length_deck_hit_ratio_min,
    gun_length_deck_hit_ratio_max,
    clamp: false)
```

The code selects `min_casemate_length_mod` / `tech_gun_length_limit_casemates`
for casemates, `min_gun_length_mod` / `tech_gun_length_limit_small` for guns up
to 2 inches, and `min_gun_length_mod` / `tech_gun_length_limit` for larger guns.
If the ship or matching turret-caliber data is unavailable, `deckHitRatio`
stays at `1`.

In practical terms, vanilla's damage-side decision is probabilistic:

- Flat incoming fire is overwhelmingly a side hit.
- Steep incoming fire is overwhelmingly a deck/horizontal hit.
- Gun length and tech limits shift the deck-vs-side chance.
- The result is still a chance roll, so similar-looking impacts can resolve
  differently.

## Is `CalcSideHitChance` Dead Code?

The inspected local vanilla build does call `CalcSideHitChance(...)` from the
live shell-hit path. The static chain is:

1. `Shell.Update(...)` updates the projectile transform from
   `Trajectory3D.VelocityAtTime(...)`.
2. On impact, `Shell.Update(...)` calls `Ship.TakeHitRaw(...)`.
3. `Ship.TakeHitRaw(...)` calls `Shell.ImpactForwardToAngleSideUp(...)`, then
   `Ship.CalcSideHitChance(...)`, then `Util.Chance(...)`.
4. The rolled boolean is written to `HitPrecalc.sideHit`.
5. The same `sideHit` value is passed into `Ship.GetArmorZoneForPart(...)` and
   `Ship.GetPenetration(...)`.

So the best current read is not "vanilla never calls the function." It is
"vanilla calls the function, but that function is only one layer of deck-hit
behavior."

There is an important second step after the early probability roll. For hull
hits where no specific part is selected, `TakeHitRaw(...)` can:

1. Use the rolled `sideHit` value to call
   `ChooseSectionToDamage(sideHit, angleSideForward, angleSideFrontBack, ...)`.
2. Later recompute the section from the actual impact position with
   `GetSectionFromPositions(...)`.
3. If that recomputed section exists, overwrite `HitPrecalc.sideHit` with
   `section.y != 3`.

In that later pass, section row `y == 3` is treated as a deck row and produces
`sideHit == false`. Any other row produces `sideHit == true`. This means a
valid early deck/horizontal probability roll can be lost if the physical impact
position is later mapped to a non-deck section.

This matters because recent community reports describe "no deck hits" in terms
of battle messages and observed armor hits, especially in vanilla 1.7-era custom
battles. Those reports can still be true if a different layer is broken:

- The shell's chosen impact position may no longer land on, or be distributed
  across, the physical deck area in a useful way.
- The impact vector fed into the side/deck chance may be wrong or too flat at
  runtime, even though the function is called.
- `sideHit == false` may be happening, but the selected part/section or report
  text may still present the result as belt, superstructure, turret top, or
  another non-deck category.

The old TAF/DIP code supports this interpretation. It contains a commented
"Fix for broken deck hits" that adjusts the calculated hit position
(`tempPos.y`) based on range, gun grade, and hull bounds before the vanilla hit
logic resolves the impact. That is a hit-position/distribution fix, not evidence
that `CalcSideHitChance(...)` itself is unreachable. In this local DIP checkout,
that old remapper is disabled.

## Performance-Safe Fix Shape

This should be fixable without a massive performance hit if VP avoids per-frame
projectile tracking.

The risky version is the old style of tracking every shell through
`Shell.Update(...)`. That is a hot path because every active projectile runs it
every frame. The local DIP checkout explicitly disables the old TAF helper that
fed the deck-hit remapper from `Shell.Update(...)`.

Safer options are impact-time or precalc-time fixes:

- Patch `GetCalcHitPoint(...)` / `PrecalcHit(...)` so the calculated impact
  position can actually land in the deck section row when the ballistic
  side/deck model says a deck hit is plausible. This is closest to the old TAF
  intent and should also improve battle reports, because vanilla's later
  section remap would see a deck-row impact.
- Patch `TakeHitRaw(...)` around the section remap so an early deck roll is not
  blindly overwritten by a non-deck section when the impact geometry is suspect.
  This is cheap because it runs only on resolved impacts, but it is more likely
  to require a Harmony transpiler or a tightly scoped IL hook.
- Use existing deck bounds/colliders only as an impact-time validation step, not
  as a per-frame raycast. A bounded check on actual impacts should be acceptable;
  scanning deck geometry for every shell every frame would not be.

The practical target is O(1) work per predicted hit or resolved impact: a few
angle/section checks, maybe one cached hull-bounds calculation, and no new
`Shell.Update(...)` bookkeeping.

Recommended initial VP direction:

1. Add a debug-only `PrecalcHit(...)` diagnostic first. Count the ballistic deck
   chance, vanilla final `sideHit`, and final `section.y` for resolved gun hits.
   This proves whether the suspected overwrite is actually happening at runtime.
2. If confirmed, prefer a `PrecalcHit(...)` post-process of `HitPrecalc` over a
   `TakeHitRaw(...)` transpiler. For hull hits where the ballistic model says a
   deck hit is plausible but vanilla maps the hit to a non-deck row, choose the
   nearest cached deck-row section and set `sideHit = false`.
3. Cache deck-row sections per ship so the fix does not scan section data on
   every hit. The hot path should do only simple checks and a dictionary lookup.

This is less physically pure than rewriting the impact-position solver, but it
is cleaner for VP: one private-method hook, no per-frame projectile work, no
global `GetSectionFromPositions(...)` side effects, and no large fragile
transpiler in `TakeHitRaw(...)`.

## Comparison With The Old TAF Fix

The old TAF/DIP deck-hit fix had two pieces:

- A `Shell.Update(...)` helper recorded each shell's starting position,
  maintained `Patch_Shell.updating`, and removed finished shells from a
  dictionary.
- A ship-side remapper used that live shell context to compute
  `distance / range`, scaled it by gun tech grade and
  `taf_shell_deck_hit_percent_min/max`, then adjusted the temporary impact
  position with `tempPos.y -= hullSize.min.y * deckPercent`.

That approach is more physical than a pure `HitPrecalc` post-process because it
tries to alter the actual temporary impact point before vanilla maps it to a
section. If the adjusted point lands in the deck row, vanilla's normal
downstream reporting and damage logic follow naturally.

The cost is that it depends on per-frame shell bookkeeping and global "current
shell being updated" state. That is exactly the part the local DIP checkout
disabled: `Shell.Update(...)` is a hot path in large battles.

The recommended VP approach is deliberately less invasive:

- Do not track shells through `Shell.Update(...)`.
- Let vanilla create `HitPrecalc` normally.
- At `PrecalcHit(...)` return, inspect only the completed hit result.
- If the runtime diagnostic confirms the section overwrite bug, correct only
  hull hits whose ballistic deck chance and final section disagree.

So the tradeoff is:

```text
TAF-style fix:
  More physical hit-point remap
  Better chance of visual/report consistency
  Higher hot-path cost and more global shell state

VP recommended fix:
  Less physical, more surgical final-hit correction
  Very low runtime cost
  Less fragile and easier to gate behind an option/diagnostic
```

## What Is Still Unclear

- There is no dedicated persisted "hit deck collider" flag in `HitPrecalc`.
  The main damage contract is the simplified `sideHit` boolean plus the chosen
  section/part.
- `Ship.FindDeckAtPoint(...)` and deck colliders exist, but the clearest uses
  found during this pass were for placing secondary effects such as fires or
  module-related visuals at an appropriate deck height. That is different from
  proving the armor-zone decision is a direct shell-vs-deck-mesh collision.
- The inspected damage path is therefore a derived/probabilistic
  vertical-vs-horizontal armor decision, not a clean geometric "the shell ray hit
  this exact deck surface" model.

## Practical Interpretation

- If a shell is classified as `sideHit == false`, vanilla should use horizontal
  penetration and deck armor.
- If a shell impact looks visually like a deck hit but is classified as
  `sideHit == true`, damage may still resolve against belt/side armor.
- If a visual effect is misplaced but `sideHit == false`, that is probably a
  visual placement problem rather than proof that deck armor failed.

## Working Conclusion For VP

- Do not assume vanilla ignores deck armor. It already has horizontal
  penetration, deck armor zones, inner deck layers, and deck-specific hit
  effects.
- If VP wants "actual deck hits," define that as an improvement over vanilla's
  simplified `sideHit` model: either improve the probability model, or add a
  richer hit pre-calc that records actual impact geometry before `TakeHitRaw`.
- Before changing balance, add runtime diagnostics around `PrecalcHit` /
  `TakeHitRaw` to log `sideHit`, impact position, selected section, selected
  armor zone, armor mm, penetration mm, and hit type for controlled long-range
  and short-range tests.
