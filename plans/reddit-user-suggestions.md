# Reddit User Suggestions

Sources reviewed:
- https://www.reddit.com/r/ultimateadmiral/comments/1t53aql/uadvp_vanilla_plus_03_is_ready_with_battle_ai/
- https://www.reddit.com/r/ultimateadmiral/comments/1t98tvi/uadvp_04_teaser_and_request_for_feedback/
- https://www.reddit.com/r/ultimateadmiral/comments/1t32fbf/uadvp_vanilla_plus_02x_is_ready_to_go_with/
- https://www.reddit.com/r/ultimateadmiral/comments/1t28iuw/hey_all_i_am_building_a_new_mod_uadvp_vanilla/

Reviewed against current VP checkout on 2026-05-13. Current README version is
`0.4.4`. This note captures suggestions from UAD:VP Reddit feedback threads
that do not appear implemented in the current README or Harmony patch surface.

## Candidate Backlog

### Minor Nation Interaction Pass

Source: CodeX57 asked whether VP does anything with minor nations, noting that
fighting and interacting with them feels clunky.

Current VP coverage: no obvious minor-nation-focused feature in README or source
search. Existing direct diplomacy actions are major campaign politics UI work,
not a broader minor-nation interaction model.

Potential directions:
- Identify exactly which minor-nation interactions feel worst: wars, ports,
  relations, invasions, diplomacy rows, or battle generation.
- Audit vanilla data structures for minor/province players before assuming they
  can be handled like major nations.
- Keep any first pass read-only/UI-oriented unless campaign simulation ownership
  is clearly safe.

### Campaign Design Test Battles / Empty Battles

Source: prreich asked for a way to open campaign designs in Custom Battle or
create an empty campaign battle to test new designs, cruise around, and capture
screenshots while not at war.

Current VP coverage: the current source tree has no
`CampaignDesignTestBattlePatch.cs`, and README does not list a campaign
test-battle or campaign-to-Custom-Battle feature. The existing
`plans/vanilla-battle-flow.md` records previous investigation and warnings
around direct generated battles and shared-design/custom-battle state.

Potential directions:
- Revisit the vanilla battle-flow note before implementing anything here.
- Safer first target may be a campaign Designs-tab "test selected design" flow
  that builds a transient `RealBattleSave`, not a persistent Custom Battle edit.
- The "empty battle/photo cruise" variant may be a separate scenario generator
  with no enemy force and no campaign persistence.

### Separate Research Speed Controls

Source: user asked for options to change research speed for player and AI
separately; another reply noted save-file `TechMods` can be changed per player
and category.

Current VP coverage: Technology Spread has `Vanilla`, `Gradual`, `Swift`, and
`Unrestricted`, but it is a catch-up mechanic for trailing major nations rather
than independent player/AI research multipliers.

Potential directions:
- Decide whether this fits VP philosophy or leans too far into cheat/debug
  controls.
- If accepted, prefer explicit labels such as `Player Research Pace` and `AI
  Research Pace`, with vanilla defaults and clear campaign-only scope.
- Investigate whether runtime changes should edit `TechMods`, hook research
  speed calculation, or both.

### Paint Scheme Reference And Era Variants

Source: the 0.4 teaser asked for representative nation paint references, and
commenters gave several specific corrections and era ideas.

Current VP coverage: Experimental Nation Ship Paints exists and supports
editable per-nation `hull`, `super`, and `gun` color strings. The current
implementation is global per nation, not year/era-specific, and it does not
support pattern geometry such as stripes or dazzle shapes.

Potential directions:
- US pre-dreadnoughts: keep the white hull direction, but make the
  superstructure a matte buff rather than shiny gold; USS Olympia was suggested
  as a practical reference.
- US era variants: consider Great White Fleet styling early, Measure 1 / haze
  gray later, and decide whether dazzle/interwar schemes are feasible.
- UK: evaluate Razzle Dazzle as an alternative to the current Victorian-style
  direction, while recognizing that true dazzle needs pattern support rather
  than just three color fields.
- Italy: investigate red-and-white bow identification stripes for WW2-era
  ships. This likely requires localized hull-region painting, not just a whole
  hull color.
- China: compare the current placeholder to the Dingyuan replica reference:
  black hull, white superstructure, gold upperworks/details.
- Spain: refine the 1898-style scheme around black hull, possible thin white
  belt/deck-edge stripe, white superstructure, ochre/yellow guns/funnels/masts,
  black funnel caps, and gold trim where feasible.

### Custom Paint Pattern Tooling

Source: several 0.4 teaser comments asked whether paint is hard-baked, whether
players could customize it, or whether players could create unique fleet paint
patterns.

Current VP coverage: players can edit per-nation color strings for hull,
superstructure, and gun colors. There is no per-ship, per-fleet, per-era, or
pattern-authoring UI.

Potential directions:
- First clarify whether "custom paint" means per-nation colors, per-player
  overrides, per-ship saved schemes, or actual pattern geometry.
- A low-risk first expansion could be optional player-country override strings
  before any per-ship save data is attempted.
- Pattern support would need a separate rendering plan; editable color strings
  cannot express dazzle blocks, bow stripes, camouflage bands, or hull trim.

### Selectable Turret Models

Source: user requested selectable turret styles/models, later seconded as a top
ask. An earlier 0.2.x commenter also noted that weapon upgrades can change gun
models enough that turrets must be removed or moved, sometimes blocking otherwise
competitive refits.

Current VP coverage: no selectable turret-style feature. Experimental Nation
Ship Paints changes material colors, not part models.

Potential directions:
- Research how vanilla chooses turret model variants for a gun mark, nation,
  caliber, mount, and year.
- Determine whether model selection can be exposed without breaking armor,
  barrel, reload, placement, save/load, or AI design assumptions.
- Investigate a narrower "preserve refit footprint" option where upgraded gun
  marks keep a compatible older model or placement envelope when possible.
- Keep distinct from paint schemes and superstructure compatibility.

### Blast Bags / Gun Visual Detail

Source: a 0.4 teaser commenter asked whether blast bags could be added to some
guns, specifically mentioning British 15-inch guns.

Current VP coverage: no gun-detail mesh feature. Nation Ship Paints can recolor
some gun materials, but does not add missing model parts.

Potential directions:
- Research whether blast bags already exist as alternate vanilla models,
  hidden meshes, or separate material regions.
- If no mesh exists, this likely becomes asset/model injection rather than a
  simple Harmony/UI patch.
- Keep scoped to visual-only changes unless gun part data must change.

### Ship-Class Restrictions / Scenario Rules

Source: user suggested restrictions on what ships players and AI can build,
such as "Battlecruisers Only" from an 1890 start.

Current VP coverage: CA+ Torpedoes restricts one equipment category on major
combatants, but there is no general ship-class restriction system.

Potential directions:
- Treat as a campaign ruleset feature rather than a one-off hull filter.
- Start with player-only or opt-in restrictions unless AI design generation can
  be proven to handle the same constraints.
- Consider whether restrictions should apply to new construction only, generated
  AI designs, refits, existing ships, and battle generation.

### Campaign Balance Presets

Source: user suggested start-of-campaign balance presets for things like weight,
accuracy, and ship balance across historical game-version styles.

Current VP coverage: VP has individual options for several balance areas, but no
campaign preset profiles.

Potential directions:
- A preset system could be UI-only sugar over existing toggles first.
- Historical-version presets would need exact data provenance before changing
  weights, accuracy, army strength, or ship balance.
- Avoid hidden one-click mixes that are hard to inspect or reverse.

### Auto-Resolve Damage Balance

Source: an origin-thread commenter reported badly skewed auto-battle outcomes,
such as heavily armored battleships taking large damage from a light cruiser
that should not plausibly be able to hurt them.

Current VP coverage: Campaign battle auto-resolve odds are displayed in the
battle popup, but VP does not appear to rebalance the auto-resolve damage model.

Potential directions:
- Research vanilla auto-resolve damage calculation before changing outcomes.
- Compare armor, gun caliber, torpedoes, ship class, and battle tonnage inputs
  against the displayed odds so the UI and result model do not disagree.
- A first feature could be diagnostic logging or a preview of expected damage
  rather than immediate balance changes.

### Shell And Fire Balance Review

Source: origin-thread feedback raised several battle-balance issues: AI gun
caliber escalation too early, HE fire chance being too dominant, very large AP
shells feeling weak or inconsistent, and fore/aft belt or superstructure hits
making capped HE too universally useful.

Current VP coverage: Accuracy Penalties adjust smoke/stability/instability
accuracy modifiers, but there is no shell/fire/damage-model rebalance listed.

Potential directions:
- Split into separate research notes for fire chance, AP overpen/bounce behavior,
  HE penetration damage, and AI gun-caliber progression.
- Keep any tuning configurable, because these are high-impact battle balance
  changes.
- Use vanilla battle logs or controlled test battles before changing global
  shell behavior.

### National Flavor / AI Design Priorities

Source: thread discussion suggested national flavor such as Italy prioritizing
speed, the UK big guns, and Germany armor.

Current VP coverage: no AI national-design-priority feature. Nation Ship Paints
adds visual identity only.

Potential directions:
- Research vanilla AI design scoring and country/personality data.
- Keep first experiments measurable: preferred speed, armor, gun caliber, or
  torpedo emphasis by country.
- Watch performance and AI validity closely; bad design weights could degrade
  campaign ship generation.

### AI Fleet Rebuild After Collapse

Source: origin-thread feedback asked about AI nations that stop building ships
after their navy has been badly beaten.

Current VP coverage: no AI post-defeat rebuild or recovery feature.

Potential directions:
- Research whether the cause is economy, shipyard capacity, unrest, peace state,
  design-generation failure, or AI budget priorities.
- A first pass should be diagnostic: detect a major power with low active fleet,
  available budget/capacity, and no meaningful construction.
- Avoid simple free ships unless the campaign economy and AI design path are
  clearly broken.

### Player-Designed Armed Transports

Source: user asked about player-designed armed transports, especially for convoy
raid selection.

Current VP coverage: no transport design feature. Existing transport work is
campaign status display and port-strike transport-loss balance.

Potential directions:
- Identify whether armed transports are real ship stores, generated battle-only
  entities, or special mission placeholders.
- Determine whether player designs could be selected as templates without making
  transports directly buildable.
- Keep convoy raid generation and campaign economy consequences separate.

### All Hulls Unlocked

Source: user suggested an `All Hulls Unlocked` option like Custom Battle.

Current VP coverage: Obsolete Tech & Hulls can retain already researched
obsolete hulls/components for the player, but it does not unlock every hull
regardless of research/year.

Potential directions:
- This is likely sharper than the current retention option and should default to
  vanilla/off if implemented.
- Decide whether it should be player-only, constructor-only, or campaign-wide.
- Check whether parts, towers, funnels, and unlock prerequisites also need to be
  relaxed to make all hulls usable.

### Army Command Hooks

Source: users suggested being able to command the army to do something, and the
0.4 teaser also suggested spending Naval Prestige like political power to
influence the army, government, coups, or elections.

Current VP coverage: no army-command feature.

Potential directions:
- Research what campaign army operations are actually exposed in code.
- Define concrete commands before implementation: accelerate invasion, defend
  province, request offensive, suppress unrest, or similar.
- Treat Naval Prestige cost, coups, elections, and government influence as
  balancing concepts, not implementation plans, until the vanilla campaign
  state model is understood.

### Peace Treaty Influence

Source: origin-thread feedback asked for more player impact over peace treaties:
what the AI takes when the player loses, and when the AI accepts peace after the
player is clearly winning.

Current VP coverage: Direct diplomacy includes Force Peace, with vanilla-style
reparations based on victory points when there is a clear winner, but there is
no broader treaty-negotiation or surrender-acceptance system.

Potential directions:
- Research vanilla peace acceptance gates, invasion blockers, victory points,
  reparations, and province/ship transfer decisions.
- Decide whether VP should add better information, stronger player commands, or
  adjusted AI willingness.
- Keep the existing warning in mind: bypassing vanilla blockers may break
  campaign state.

### Intelligence / Spying Budget For Tech Knowledge

Source: user suggested that tech knowledge could be tied to an intelligence or
spying budget line.

Current VP coverage: Research standing markers and Technology Spread assume
major-nation tech standing is knowable enough for gameplay; no intelligence
budget exists.

Potential directions:
- If pursued, first design the player-facing information model: hidden,
  approximate, delayed, or budget-improved tech standings.
- Adding a real budget line may require broader finance UI and save-state work
  than a research-window display patch.

### Visible Smoke Before Spotting

Source: user requested visible smoke on the horizon before enemy ships are fully
spotted, because vanilla rough bearings can be misleading.

Current VP coverage: Accuracy Penalties can rebalance smoke-related accuracy
modifiers, but VP does not add pre-spotting visual smoke cues.

Potential directions:
- Research battle spotting state and smoke particle ownership.
- A first pass could be a low-detail bearing marker rather than actual smoke
  rendering.
- Avoid revealing exact ship positions if the intent is only approximate
  lookout information.

### Permanent Task Forces

Source: user requested permanent task forces with assigned ships so ships are
not randomly added or task forces merged when close together.

Current VP coverage: VP has task-force tonnage indicators and a return-to-port
shortcut, but no task-force composition persistence or merge-prevention feature.

Potential directions:
- Audit vanilla task-force merge/split rules and where ship assignment changes.
- Consider a "lock task force composition" flag before attempting a full
  permanent-formation system.
- Watch save compatibility and campaign AI behavior.

### Task Force Auto-Return Damage Thresholds

Source: origin-thread feedback suggested configurable health thresholds for when
ships automatically return to port, or whether they should do so at all.

Current VP coverage: Task force return shortcut adds manual return-to-origin,
but no automatic damage threshold or policy control.

Potential directions:
- Research where vanilla marks a ship/task force as damaged enough to leave a
  mission or seek repairs.
- Expose clear settings such as `Vanilla`, `Never Auto-Return`, or threshold
  percentages only after verifying campaign AI and player fleets use separable
  paths.
- Be careful with automation that can strand missions or break battle generation.

### Persistent Battle Order Defaults

Source: origin-thread feedback asked for default fleet-combat settings such as
auto-rotate leader. A later comment also asked for fleet-wide automated threat
responses such as avoid torpedoes, collision prevention, and rotate when damaged
so the player does not set them at the start of every fight.

Current VP coverage: Battle division AI control and battle speed QoL exist, but
there is no persistent player-default battle-order profile.

Potential directions:
- Research which battle toggles are per-division, per-ship, or global and when
  vanilla initializes them.
- Store player preferences outside the save if possible, then apply them only at
  battle start or when a new division spawns.
- Keep manual battle commands authoritative after the default is applied.

### Enemy Fleet Interception Orders

Source: origin-thread feedback asked for assigning a task group to intercept a
specific enemy force that is moving around nearby sea zones.

Current VP coverage: VP has map task-force indicators and return-to-port, but no
targeted intercept/order-follow feature.

Potential directions:
- Research task-force route and target state in campaign map code.
- A safer first pass might add UI affordances and routing to the current known
  enemy position, not continuous pursuit.
- Continuous pursuit needs rules for lost contact, fog of war, war state, and
  enemy teleport/mission movement.

### Searchable Campaign Lists

Source: origin-thread feedback asked why port-selection lists and other campaign
menus are not searchable or sortable, especially when building a task force at a
specific port.

Current VP coverage: no general list search/sort feature.

Potential directions:
- Inventory the worst list UIs first: port picker, fleet/design lists, politics
  rows, and any task-force assignment windows.
- Start with a single port-selection search field if the UI hooks are stable.
- Keep keyboard focus and vanilla controller/mouse behavior in mind.

### Port Capacity-Based Repair And Supply

Source: origin-thread feedback requested meaningful port capacity again: repair
and supply rates should depend on whether the ship or task-force tonnage exceeds
the port's capacity.

Current VP coverage: Suspend Dock Overcapacity manages monthly shipyard work,
and port ship counts improve map visibility, but VP does not tie repair/supply
speed to each port's local capacity.

Potential directions:
- Research vanilla port capacity, repair location, maintenance cost, and supply
  calculations before deciding whether this is a display fix or balance system.
- Consider first showing over-capacity repair/supply warnings on task forces or
  port tooltips.
- If implemented, make it configurable; this could be a significant campaign
  difficulty increase.

### Shipyard Expansion Cost Mismatch

Source: user reported that the total shipyard expansion cost shown when placing
the order did not match the actual monthly cost shown in expenses and balance.

Current VP coverage: Campaign maintenance indicators show dock expansion status,
but do not reconcile or fix expansion cost math.

Potential directions:
- Reproduce with the user's example shape: Germany 1900-style campaign,
  expansion dialog total, duration, expenses tab monthly line, and balance delta.
- Determine whether the bug is display-only, monthly-cost calculation, or hidden
  multipliers/taxes.
- A display fix may be safer than changing campaign finances.

### Design Count Readability

Source: a 0.4 teaser commenter asked whether the design-screen ship count could
be formatted as something like `n/n(n);n` because the current parentheses were
hard to read.

Current VP coverage: Design ship counts exist, and the author reply described
the current meaning as active / building (building for allies) / in refit or
repair. There is no alternate compact delimiter format in the current README.

Potential directions:
- Revisit the visible count format and tooltip together so the compact text and
  explanation stay synchronized.
- Consider whether a delimiter such as `active / building (allies) ; refit`
  scans better than the current string at multiple UI scales.
- Keep the row width stable; this count is dense and easy to make ugly.

### Fleet/Sub Crew And Tonnage Capacity Displays

Source: user requested available crew and used/available tonnage on fleet and
submarine screens.

Current VP coverage: no fleet/sub-screen crew capacity feature. Current tonnage
work is task-force map icon fill and dock-overcapacity balancing.

Potential directions:
- Identify vanilla fields for available crew, used crew, dock tonnage, and sub
  capacity.
- Add compact indicators to Fleet and Submarine windows, likely as status text
  rather than new controls.
- Avoid duplicating country-info text unless the target screen has better
  context.

### Auto-Enable Crew Fill Toggles

Source: origin-thread feedback asked to auto-enable the existing fill-crew
options for ships and submarines.

Current VP coverage: current source search shows no crew-related VP Harmony
patch. Prior investigation found vanilla already has persisted
`Player.AutoAddCrewToShips` and `Player.AutoAddCrewToSub` booleans.

Potential directions:
- Treat this as defaulting vanilla's existing toggles on for new campaigns or
  first load, not as a new crew system.
- Do not repeatedly force the setting if the player later turns it off.
- Wire both ship and submarine paths together so the behavior is easy to explain.

### Standardization System

Source: user suggested standard gun/torpedo calibers, production benefits from
building many similar ships, cheaper or better repeated torpedoes, and defects
improvement from class familiarity.

Current VP coverage: no standardization or production-learning feature.

Potential directions:
- Split into smaller systems: standard caliber selection, class production
  experience, component cost modifiers, and defect reduction.
- Needs careful balancing because it touches economy, design incentives, and
  campaign ship quality.
- First research target should be where vanilla computes construction cost,
  component cost, and defects.

### Shared Design Import / Export Tools

Source: the 0.4 teaser thread asked for a way to save Custom Battle and campaign
ships to Shared Designs so favorite designs can be preserved or ported back out.

Current VP coverage: the current README does not list a Shared Designs export
tool, and current source search did not find a live export command. The
`vanilla-battle-flow.md` notes document prior caution around campaign design
export, custom-battle cached designs, and shared-design state.

Potential directions:
- Split this from direct campaign test battles. Exporting a design and launching
  a generated battle have different failure modes.
- Campaign-to-Shared export should be idempotent and avoid duplicate
  `*.bindesign` files for the same design.
- Custom Battle-to-Shared export needs separate research because Custom Battle
  can cache temporary/generated ships outside the normal Shared Designs store.

### Expanded Ship Name Pools

Source: a 0.4 teaser commenter suggested that the USA should get more possible
ship names after conquering regions such as the British Isles.

Current VP coverage: no ship-name-pool expansion feature.

Potential directions:
- Research how vanilla chooses names by country, conquered territory, ship type,
  and existing used-name history.
- Decide whether conquest-based names fit VP, or whether broader optional
  expanded name pools are cleaner.
- Avoid mutating existing ship names unless the player explicitly requests it.

### Designer Placement / Clipping Flexibility

Source: users asked whether funnel/tower/barbette clipping and hardpoint
placement restrictions could be loosened. Origin-thread feedback also called out
refitting enemy prizes where parts fail because they are "too far from original
place" and asked whether refit restrictions could be removed.

Current VP coverage: Superstructure Compatibility can expose more tower and
funnel options, but it does not change mesh collision, hardpoints, or placement
validation.

Potential directions:
- Treat separately from superstructure availability.
- Research constructor placement checks and model mount data before attempting
  edits.
- Separate normal design placement, refit placement windows, and captured-prize
  refit restrictions; they may have different vanilla checks.
- Barbettes and hardpoints are especially balance-sensitive and likely need
  player-only/default-off gating if feasible.

## Out Of Scope Or Already Covered

- Superstructure refits with newer towers/funnels: implemented as
  `Superstructure Compatibility`.
- Wrap-around map request: implemented as experimental `Map Geometry` /
  `Disc World`, though known issues remain in `plans/campaign-map-wrap.md`.
- Obsolete tech and hull retention: implemented as `Obsolete Tech & Hulls`.
- Mine and submarine removal: implemented as `Mine Warfare` and `Submarine
  Warfare` settings.
- Auto-battle odds display: implemented as Campaign battle auto-resolve odds.
- Return-to-home-port request: implemented as Task force return shortcut.
- Map port ship-count indicators: implemented as Campaign map port ship counts.
- Late UK BC/BB tower availability: implemented as British late-hull tower
  availability and partly superseded by `Superstructure Compatibility`.
- Basic nation color schemes/paint jobs: implemented as `Experimental Nation
  Ship Paints`, though era-specific schemes, local pattern geometry, and
  historical tuning remain separate backlog items above.
- Research bonus off switch: Technology Spread has `Vanilla` mode and defaults
  to vanilla in current README.
- Game-ending campaign UI stuck report from the thread: author reply says this
  was found and fixed in 0.4; current checkout is 0.4.4.
- Install/setup confusion and outdated MelonLoader replies: support issues, not
  feature backlog.
- Fresh-campaign and save-compatibility questions from the 0.4 teaser are
  support/docs topics unless the compatibility promise changes.
- Province shape/new-province editing: technically interesting, but likely
  outside current VP scope unless the project intentionally takes on map-asset
  editing.
- DIP compatibility: not currently a promised VP feature; keep as "best effort"
  unless explicitly scoped.
