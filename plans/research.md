# Research Formula Notes

## Scope

These notes summarize the vanilla campaign research behavior as inspected while discussing UAD:VP.

VP currently does not replace the core research formula. The VP research-status UI estimates months remaining by calling vanilla `CampaignController.GetResearchSpeed(player, tech)` and applying that to the remaining progress.

Important local sources:

- `E:\Codex\UADVanillaPlus\UADVanillaPlus\Harmony\CampaignTechnologyStatusPatch.cs`
- `E:\Codex\cpp2il_uad_isil\IsilDump\Assembly-CSharp\CampaignController.txt`
- `E:\Codex\cpp2il_uad_isil\IsilDump\Assembly-CSharp\Player.txt`
- `E:\Codex\cpp2il_uad_isil\IsilDump\Assembly-CSharp\CampaignFinancesWindow.txt`
- `E:\Codex\UADRealismDIP\TweaksAndFixes\Default_Files\UAD_Files\players.csv`
- `E:\Codex\UADRealismDIP\TweaksAndFixes\Default_Files\UAD_Files\params.csv`

The `params.csv` values listed here are from the local data copy found under `UADRealismDIP`. VP does not appear to ship its own params override, so treat these as the available local vanilla-style data unless verified against the live installed game data.

## Research Progress

Research progress is updated in `CampaignController.UpdatePlayerTech`. It calls:

```csharp
CampaignController.GetResearchSpeed(player, tech)
```

and adds that result to the current `Technology.progress`, clamped to `0..100`.

The reconstructed speed formula is approximately:

```text
speed =
    research_progress_base
  * 100 / tech.difficulty
  * timeMult
  * budgetMult
  * techBudgetMult
  * player.techMods[tech.type]
  * priorityMult
  * aiDifficultyMultiplierIfAI
```

The finance-tab research slider affects `techBudgetMult`, not the base GDP comparison directly.

## Time Rubber Band

The time component compares the campaign year to the tech's historical year.

```text
yearsDelta = campaignYear - techYear

if yearsDelta > 0:
    raw = 1 + yearsDelta / research_late_years_to_double
else:
    raw = 1 + yearsDelta / research_early_years_to_stall

timeMult = clamp(raw, research_time_mod_min, research_time_mod_max)
```

Using the local params:

```text
research_late_years_to_double = 27.5
research_early_years_to_stall = 9
research_time_mod_min = 0.4
research_time_mod_max = 1.35
```

Practical effect:

- On-time tech: `1.00x`
- 2 years late: about `1.07x`
- 5 years late: about `1.18x`
- 10+ years late: capped at `1.35x`
- 1 year early: about `0.89x`
- 3 years early: about `0.67x`
- 5.4+ years early: capped at `0.40x`

This is the main catch-up mechanic. It is based on calendar lateness, not on another country already having the tech.

## Budget / GDP Modifier

The budget modifier compares the player's `StateBudget()` to the richest major player's `StateBudget()`.

```text
budgetRatio = player.StateBudget() / (richestMajor.StateBudget() + 1)
budgetMult = lerp(research_budget_mod_min, research_budget_mod_max, budgetRatio)
```

Using the local params:

```text
research_budget_mod_min = 0.67
research_budget_mod_max = 1.125
```

So a country at the bottom floor gets `0.67x`, while the richest gets about `1.125x`. The absolute worst poor-vs-rich speed ratio from this term alone is:

```text
0.67 / 1.125 = 0.596
```

That means the poorest possible country would be about 40% slower than the richest before time rubber band, slider, priority, tech type modifiers, and difficulty modifiers.

For an 1890 start, using starting nation income as a rough proxy for `StateBudget()`:

- Germany: `6.0B * 1.05 = 6.3B`
- Italy: `4.5B * 0.8 = 3.6B`

Italy's income ratio is about `0.571`. Applying the budget curve:

```text
Italy budgetMult ~= 0.67 + (1.125 - 0.67) * 0.571
                 ~= 0.93

Germany budgetMult ~= 1.125

Italy/Germany ~= 0.93 / 1.125
              ~= 0.83
```

So, all else equal, the poorest 1890 major starts at roughly 83% of the richest 1890 major's research speed from the GDP/budget term. That is a real advantage for rich nations, but not an overwhelming one by itself.

## Finance Slider Speed Effect

The finance-tab `Tech Budget` slider writes to `Player.techBudget`.

The speed formula normalizes it by `tech_budget_max` and applies an arctangent curve:

```text
normalized = player.techBudget / tech_budget_max

curve =
    atan((normalized - 0.5) * tech_budget_curver)
  / atan(0.5 * tech_budget_curver)

if normalized >= 0.5:
    techBudgetMult = 1 + curve * tech_budget_boost / 100
else:
    techBudgetMult = 1 + curve * tech_budget_reduce / 100
```

Using the local params:

```text
tech_budget_max = 100
tech_budget_boost = 110
tech_budget_reduce = 100
tech_budget_curver = 3.8
```

Practical effect:

- `0%` slider: near `0x` research from slider
- `50%` slider: `1.0x`
- `100%` slider: about `2.1x`

This is nonlinear. The finance slider can dominate GDP differences if one country can afford high funding and another cannot.

## Finance Slider Cost

The monthly money cost is separate from the research-speed benefit.

`Player.ExpensesTechBudget()` reconstructs approximately as:

```text
techCost =
    Player.Budget()
  * (player.techBudget / 100)
  * Mathf.Lerp(1.0, 0.35, player.NationYearIncome() / 1_000_000_000_000)
```

Decoded constants from the local `GameAssembly.dll`:

```text
0x1820B0F04 = 1.0
0x1820B0ECC = 0.35
0x1820B0F8C = 100
0x1820E9DE8 = 1,000,000,000,000
```

Implications:

- Rich countries pay more absolute money at the same slider percent because `Player.Budget()` is larger.
- The cost is mostly a percent of that nation's monthly usable budget, so a rich country is not inherently punished as a share of its budget.
- The `NationYearIncome()` term is a small rich-country discount, sliding from `1.0` toward `0.35` as income approaches 1 trillion.
- At 1890 major-country income values, that discount is tiny. Germany at about 6.3B is around `0.996x`; Italy at about 3.6B is around `0.998x`.
- The direct slider cost is linear in slider percent for a given country and month. The nonlinear part is the research-speed return from the slider.

## Does Research Spread?

No normal country-to-country research spread was found in the inspected formula.

`UpdatePlayerTech` checks the same player's existing researched technologies and prerequisites. It does not appear to query other nations' completed techs to share progress.

The catch-up system is therefore not "spread." It is calendar-based lateness:

- Being behind the tech's historical year speeds it up.
- Being ahead of the tech's historical year slows it down.
- Other nations having the tech does not appear to directly help.

## How Strong Is The Rubber Band?

The rubber band is meaningful, but it is not a full equalizer in every case.

Worst-case floor comparison:

```text
poor budget floor = 0.67
rich budget cap = 1.125
poor/rich = 0.596
```

At maximum late-tech catch-up:

```text
poor late max = 0.67 * 1.35 = 0.9045
rich on-time = 1.125
poor/rich = 0.804
```

So an extremely poor country researching very late tech can recover much of the budget penalty, but still remains about 20% slower than the richest country on on-time tech if slider and priorities are equal.

For the actual 1890 Italy-vs-Germany estimate:

```text
Italy/Germany budget speed ~= 0.83
```

The time rubber band would roughly cancel that at about 5.7 years behind:

```text
0.83 * (1 + lag / 27.5) = 1.0
lag ~= 5.7 years
```

That means GDP alone should not make the poorest 1890 major fall 10-20 tech-years behind if slider and priorities are comparable.

## 20-Year Back-Of-Envelope

Assuming:

- 1890 start
- Italy is the poorest major, Germany is the richest major
- same slider percent
- same priority behavior
- no major wars, unrest, event shocks, or AI-specific quirks
- starting from equal tech baseline

The simple continuous model is:

```text
lagGrowth = 1 - 0.83 * (1 + lag / 27.5)
```

After 20 years, this estimates Italy at roughly:

```text
2.5 to 3 tech-years behind
```

This is not a full campaign simulation. It is a static formula estimate. Actual campaigns can diverge through:

- finance slider differences
- AI tech budget behavior
- wars and economic damage
- unrest, revolution, and events
- priorities
- per-tech-type `TechMod(...)` values from AI personalities
- ahead-of-time penalties on richer countries pushing too far forward

If the rich country can afford max research funding while the poor country sits near mid funding, the slider can dominate. A rich `100%` slider gets about `2.1x` from the slider while a `50%` slider gets `1.0x`, which can make the poor nation fall much farther behind than GDP alone would predict.

## Takeaways

- Rich nations do get a real research-speed advantage through the `StateBudget()` comparison.
- The finance slider does cost rich nations more absolute money, but mostly as a percent of their own larger monthly budget.
- The cost side is mostly linear in slider percent; the research-speed side is nonlinear.
- There is no obvious country-to-country research spread.
- Poor countries catch up mainly because late techs get faster.
- Under equal funding, the 1890 poorest major should drift only a few tech-years behind over 20 years.
- Under unequal funding, especially rich max slider vs poor mid slider, the gap can become much larger.
