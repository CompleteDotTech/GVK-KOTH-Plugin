# GVK KOTH test plan

In-game tests on the dev Torch instance (`C:\SE_GVK_S10`, join with direct connect to `localhost`), using the published Workshop mods. A Torch plugin can't run in an offline world. Each stage adds its tests here. Run the rule tests first: `dotnet test GVK_KOTH.Tests`.

No NPC mod changes are needed: NPC zone and linked-block tags can be added in game. With admin "use terminals" on, edit the NPC block's CustomData, then run `!koth reload` (it rescans every grid).

## Setup
1. `dotnet build -c Release` (deploys `Plugins\GVK-KOTH.zip`).
2. Remove `Plugins\SenX-KOTH-Plugin.zip` and `Plugins\Koth-Plugin.zip` from the test instance, so only one KOTH plugin runs.
3. Copy `docs\GVK_KOTH.sample.cfg` to `Instance\GVK_KOTH.cfg`.
4. At one site, name a cargo container on the KOTH structure `KOTH Loot`.
5. Start Torch. The log should show `GVK KOTH started with 4 zones and 3 slots.` and no config warnings.

## Stage 3: fixed zones
| # | Do | Pass when |
|---|---|---|
| 1 | `!koth reload` twice | Both reply "4 zones, 3 slots, no problems", and `GVK_KOTH.cfg` is unchanged on disk (comments and `SlotWarningMinutes` intact). Break a value (e.g. `Radius` -1), reload: the problem is listed. Fix it. |
| 2 | `!koth list` as a player | Lists KharToba, Kalash, CrashedStarship, Scraptown with next times; open zones get a GPS added to your GPS list. |
| 3 | `!koth start KharToba`, then `!koth status` | KharToba shows OPEN; chat shows "KOTH KharToba is open! GPS added." and a "KOTH KharToba" GPS appears in the GPS list (terminal GPS tab). `!koth stop KharToba` removes it. |
| 4 | Fly a powered small grid of 50+ blocks into the zone and sit in its cockpit | `!koth status` shows counted 1 and the bar rising; after about 5 minutes chat says your faction scored 5 points and 1,000,000 SC; faction balance goes up. |
| 5 | Repeat with a grid under 50 blocks, an unpowered grid, a station, and on foot | Counted stays 0. |
| 6 | Second faction joins | Bar fills faster (about 4 minutes for two players); both factions score; one loot drop lands in `KOTH Loot` and a "KOTH KharToba Loot" GPS is added for players within 15 km. |
| 7a | Build your own container named `KOTH Loot` in the zone (unowned or yours), earn a drop | Loot goes only to the NPC-owned `KOTH Loot` container, never yours. With two NPC-owned ones, it goes to the one nearest the centre, never both. |
| 7 | Rename the container, earn a drop | `!koth status` shows a queued drop; rename it back: the drop arrives within 10 seconds. |
| 8 | Leave the zone mid-bar | The bar drains (about 3.5 minutes from full). |
| 9 | Restart Torch mid-bar (zone forced open) | `!koth status` shows the zone still forced open, with its bar and queued drops kept. |
| 10 | `!koth score`, `!koth points <tag> -3 test` | Standings shown; the change is announced and appears in `Instance\GVK_KOTH\ScoreLog.csv`. |
| 11 | `!koth stop KharToba`, `!koth auto KharToba` | Zone closes with a chat message, then returns to its schedule. |
| 12 | Set a slot to start 12 minutes from now, reload | A 10-minute warning appears in chat, then the zone opens at the slot time and closes after its duration. |

## Stage 4: NPC zones
Setup: add this zone to `GVK_KOTH.cfg`. For each test, spawn a wreck with `/MES.Spawn...`, add a line `GVK-KOTH=TestWreck;` to its remote control's CustomData, and run `!koth reload`. (In production the line goes in the MES behavior profile, which MES copies into the remote control's CustomData at spawn.)
```xml
<ZoneProfile>
  <Name>TestWreck</Name>
  <Attach>NpcTag</Attach>
  <Activation>Lifetime</Activation>
  <LifetimeMinutes>10</LifetimeMinutes>
  <Radius>500</Radius>
  <CaptureSeconds>60</CaptureSeconds>
  <PointsPerLoot>0</PointsPerLoot>
</ZoneProfile>
```

| # | Do | Pass when |
|---|---|---|
| 1 | Tag a wreck and `!koth reload` | The log shows "NPC zone 'TestWreck' found"; chat says "KOTH TestWreck is open for 10 minutes! GPS added." and a GPS at the wreck appears in the GPS list. |
| 2 | `!koth list` and `!koth status` | TestWreck is listed as OPEN while it runs. |
| 3 | Capture it with a valid grid | Points and SC as for fixed zones. |
| 4 | Wait out the lifetime | Chat says "KOTH TestWreck has closed"; it doesn't reopen. |
| 5 | Tag another, then grind or destroy its remote control | Chat says "has ended: its control was destroyed"; the zone ends at once. |
| 6 | Tag another, fly away until the concealment plugin hides it, come back | No new "open" message; the zone is back with its remaining time. |
| 7 | Tag another, then `/MES.Clear` (or let it despawn) | The zone disappears quietly. |
| 8 | Tag a wreck with an unknown name and `!koth reload` | The admin alerts webhook (or log) reports the unknown profile once. |

## Stage 5: KOTH rules and linked blocks
The old GVK_Settings rule scripts still run until cutover, so test at a spot without old rule beacons, or with a zone set to a new test location.

| # | Do | Pass when |
|---|---|---|
| 1 | Fly a player grid with thrusters inside a zone's `NoThrusterRange` | Its thrusters switch off within a second; you get one chat notice per minute ("thrusters are switched off within 3 km of ..."). Turning them on again switches them off again. |
| 2 | Bring a large grid inside `NoLargeGridPowerRange` | Its batteries, reactors, solar, wind and hydrogen engines switch off. A small grid is unaffected. |
| 3 | Place a safe zone block inside `NoSafeZoneRange`, and a projector inside `NoProjectorRange` | Each switches off; with one range set to 0, that block type is left alone. |
| 4 | Same with an NPC grid | Nothing changes on NPC grids. |
| 5 | NPC zone: destroy its remote control | Its rules stop applying at once. |
| 6 | Set a zone's `LinkRange`, add `[GVK-KOTH-Link]` to the CustomData of a store block on an NPC grid near it, `!koth reload` | The store is off while the zone is closed and on while it's open (`!koth start` / `!koth stop`). |
| 7 | Same with `[GVK-KOTH-Link:KharToba]` far from Khar Toba | It follows Khar Toba only, regardless of distance. |
| 8 | Conceal and reveal the camp | The store comes back in the right state. |

## Stage 6: mod API and MES sandbox booleans
| # | Do | Pass when |
|---|---|---|
| 1 | `!koth start KharToba`, then check an MES condition or `/MES.Debug...` for `GVK_KOTH_KharToba_Active` | It is true while open and false after `!koth stop KharToba`. |
| 2 | Restart Torch while a zone is open, then stop it | The boolean is false again after the zone closes (no stale true). |
| 3 | A test mod with the `GvkKothApi` class from `docs/API.md` calls `AddPoints(factionId, 3, "Test", "API test")` | Chat and Discord say "[TAG] gained 3 KOTH points: API test"; `ScoreLog.csv` has a line with source "API: Test". This also confirms mods reach the plugin through mod messages. |
| 4 | Same with -100 on a faction with fewer points | The total stops at 0. |
| 5 | The test mod registers `AddZoneListener` and you open and close a zone | The callback fires with the zone name and true, then false. |

## Stage 7: Flying KOTH spawn (plugin side)
Setup: a zone with `Attach` NpcTag, `Activation` Slot, `SpawnGroup` set to the airship's spawn group (with `[RivalAiSpawn:true]`), X/Y/Z at the spawn point, its behavior carrying `GVK-KOTH=<Name>;`, and a slot whose Pool contains only it, starting in a few minutes.

| # | Do | Pass when |
|---|---|---|
| 1 | Wait for the slot | The log shows "spawned '<SpawnGroup>' for slot"; the airship appears upright at X/Y/Z; the zone opens on it once MES writes the behavior. This also confirms the plugin can read the MES API. |
| 2 | Restart Torch during the slot | No second airship spawns. |
| 3 | Slot ends | MES despawns the airship (its own behavior timer); the zone ends quietly. |

### Planned: r005 slot and capture persistence regressions

All cases below are planned and unexecuted. Written regressions or a mocked source-extraction harness do not establish real Torch, MES, game, or multiplayer acceptance. Execute the rule/XML and runtime-wiring checks separately from the dedicated-server cases when authorized; record the source and environment used for each result.

| # | Do | Pass when |
|---|---|---|
| F1 | Start a fresh slot occurrence with no saved pick and an enabled NPC-only pool, before its grid exists. Observe selection and the spawn path; then run a mixed fixed/NPC pool over controlled selections. Include missing and disabled profile names, a saved occurrence pick, and a fixed-only pool. | The NPC profile is eligible and reaches one spawn attempt without a pre-existing runtime; both enabled fixed and NPC profiles are eligible in the mixed pool. Missing and disabled names are excluded, a saved pick is reused, and fixed-only behavior remains intact. A separate real MES run confirms the airship actually spawns and attaches as in Stage 7. |
| F2 | With a still-present active NPC zone and a fixed zone both progressing, set distinguishable nonzero bars, partial loot counters and pending drops. Capture the periodic serialized state and reload it; repeat through graceful Stop, observing serialization before NPC tracker cleanup. | Both runtimes' current bar and partial loot counter are serialized and restored, and their pending drops remain intact. Graceful Stop retains the same state before cleanup and queued writes complete. |
| F3a | For both fixed and NPC runtimes, use `CaptureSeconds=100` and `MinPresenceShare=0.25`. Let faction A contribute 90 bar seconds, save to XML, reconstruct/reload the runtime, then let A contribute the last 10. Compare with an uninterrupted control, including completion eligibility, points, SC and loot. Repeat through configuration reload. | The saved bar and detached per-faction credit survive XML and runtime reconstruction; A is eligible and rewards and loot match the uninterrupted control. Partial loot counters and pending drops survive. |
| F3b | Repeat the completion with a genuine late arrival contributing less than 25% of the bar; separately have a previously credited faction leave before completion. Include multiple factions and check drain and reset, plus detached snapshot and restore collections. | The late arrival fails the minimum share; a faction absent at completion does not score. Multiple faction credits, drain and reset retain their normal behavior, and changing a snapshot or restore input cannot mutate live credit. |
| F3c | Deserialize and round-trip legacy XML with a positive saved bar and no `FactionProgress` element at positive `MinPresenceShare` and at share zero, including when the reader initializes an empty list. Compare an explicit empty element, nil/null list cases, and a new complete snapshot produced by `StoreState`, including an empty snapshot. Preserve nonzero partial loot and pending drops in each fixture. | Absent XML leaves `FactionProgressSpecified=false` independently of list initialization and remains absent on round-trip. An explicit empty element yields `FactionProgressSpecified=true` and an empty list, a known empty snapshot. Nil/null is unknown credit, even if specified. For unknown credit with positive share, only the incomplete bar resets to zero once, with an operator warning; no historical presence is invented. With share zero the legacy bar remains. `StoreState` marks new snapshots `FactionProgressSpecified=true`, including empty ones; new complete XML round-trips the bar and all faction credit across subsequent saves and reloads. Partial loot, pending drops and other state remain intact in every case. |

## Stage 8: player HUD (GVK_Settings branch `feat/koth-client`)
The test server needs a GVK_Settings build from that branch (published to a test Workshop item, or the server's local mod copy).

| # | Do | Pass when |
|---|---|---|
| 1 | Open a zone (`!koth start KharToba`), open the satellite map (M) | A flashing orange KOTH marker labelled "KOTH KharToba 0%" with a dotted ring; the % updates while capturing. |
| 2 | Check the corner minimap and the compass ribbon | The same flashing marker on both. |
| 3 | F2 > each settings submenu > KOTH toggle | Each hides KOTH markers in that view only; the setting survives a relog. |
| 4 | With a slot scheduled later | The next slot's zone shows as a steady, dimmed marker with its local start time. |
| 5 | Fly into the open zone | The capture panel appears on the left (name, %, bar, counted); it hides when you leave. |
| 6 | Approach within 1 km of the zone edge | A faint orange wireframe sphere marks the capture radius. |
| 7 | Complete a bar | Firework bursts above the zone for players within 5 km. |
| 7b | Flying KOTH: fly near the moving airship | The boundary sphere moves smoothly with it (no once-a-second jumps); the panel appears as soon as you're inside. |
| 8 | Stop the plugin (or wait with no zones) | Markers and the panel disappear within 30 seconds. |
| 9 | Profiler with the map open near an open KOTH | No noticeable cost from GVK_Settings compared with no KOTH. |

### Planned: dedicated-server faction HUD

All cases in this subsection are planned and unexecuted. Target client: `GVK_KothClient.cs`, SHA256 `fe238f333c30d6abf6457f9f422b507d411f20c8663b089db9b88b2a0036d7a6`. The separate `core59` core-test evidence does not establish dedicated-server runtime acceptance or execution of these cases.

Use a dedicated Torch server and clients with the faction HUD build, with an open zone and eligible powered grids as in Stage 3. Observe the panel while inside the zone and, where needed, capture the channel 47410 state messages to distinguish an absent faction field from an empty one. Allow for the capture scan and normal state broadcast; do not wait for a bar completion to check current factions.

| # | Do | Pass when |
|---|---|---|
| 1 | Put eligible players from factions AAA and BBB in the zone. | The panel shows both current tags once, in stable ordinal order, alongside the counted participants and capture bar. The `O` record has `[AAA] [BBB]` in its tenth field. Capture and scoring still follow Stage 3. |
| 2 | Add another eligible player from AAA. | The participant count increases, but AAA appears only once; BBB remains present. |
| 3 | Before the bar completes, have BBB leave, re-enter, and then become ineligible (for example, leave the cockpit); restore eligibility. | The count and tags track each scan: BBB disappears when its last eligible participant leaves or becomes ineligible, and returns on re-entry or restored eligibility. No completion event is needed. |
| 4 | Remove all eligible participants, then bring one eligible faction back. | The extended `O` record contains an empty tenth field when none are eligible and the panel clears the old tags; the returning faction appears on the next update. The count and bar continue to behave normally. |
| 5 | While viewing page 2/2 of the five-tag state in case 6a, replace EEE with FFF in otherwise identical full-state messages, keeping the same zone, participant count and capture fraction (and therefore displayed percentage). | At the next panel refresh, the panel resets to page 1/2 despite the unchanged count and percentage. After another 300 simulation ticks, page 2/2 shows `[FFF]` with no stale `[EEE]`; the count and bar remain visible. A subsequent full-state message omitting the zone removes its panel and faction state. |
| 6 | Use test tags containing field delimiters, line breaks or angle brackets where the test environment permits, and populate a long list of distinct tags. | Records remain single, correctly separated lines; tags cannot inject HUD markup. Tags are nonblank, distinct and sorted, and the panel remains readable without overflowing its bounds. The count and bar remain visible. |
| 6a | Represent at least five eligible factions using normal tags; for an exact two-page scenario use only AAA, BBB, CCC, DDD and EEE. Keep `[AAA] [BBB] [CCC] [DDD] [EEE]` unchanged in ordinary state broadcasts and observe at least 600 simulation ticks from the panel first appearing. | The page/total indicator starts at `factions 1/2:` with `[AAA] [BBB] [CCC] [DDD]`. After 300 simulation ticks it shows `factions 2/2:` with `[EEE]`; after the next 300 ticks it wraps to page 1/2. Rotation continues every 300 simulation ticks (about five seconds at 60 ticks/second), even with repeated state broadcasts. Every complete tag is eventually visible across the pages, without discarding or truncating names; additional tags remain reachable on further pages. The participant count and capture bar remain visible on every page. |
| 6b | While on page 2/2 of the unchanged five-tag state, leave the zone until the panel hides, then re-enter. Separately, while on page 2/2, move to a different open zone with the same five tags, participant count and displayed percentage. | Re-entry and changing zones each restart at page 1/2, with a fresh 300-simulation-tick interval before page 2/2. The panel shows the current zone, retains its count and bar, and does not carry over the previous page. |
| 6c | Planned, unexecuted regression: use two NPC zones with the same profile/name (`TestWreck`), distinct nonzero grid entity IDs A and B, and identical five-tag lists from case 6a, participant counts and capture fractions (hence percentages). With A selected and page 2/2 displayed, transition directly to B without an intervening hidden-panel update. Then send repeated full snapshots for B and change its coordinates while remaining inside B for at least 600 simulation ticks. | Selecting entity B resets to page 1/2 at the next panel refresh despite the matching name, tags, count and percentage. Repeated full snapshots and coordinate movement for the same entity B do not reset the page or its timer: page 2/2 appears 300 simulation ticks after selecting B and wraps to page 1/2 at 600 ticks, with no premature reset while page 2/2 is displayed. All tags remain reachable, and the count and bar remain visible. |
| 7 | Send legacy eight-field and nine-field `O` records to the new client, then an extended record with an empty tenth field. | Both legacy records are accepted with faction information unavailable; the extended empty field means no eligible factions. No tags from a prior state or a `C` completion event are presented as current. Existing marker, navigation, count and bar behavior remains intact. |
| 8 | Connect a legacy client to the extended sender and exercise an open zone, next zone and completion. | The legacy receiver continues to use the original fields, markers, navigation and completion/fireworks behavior while ignoring the appended faction field. Header version and `N`/`C` semantics remain unchanged. |
| 9 | With current tags visible, stop the plugin or otherwise stop state messages; also test joining a server without the plugin. | Stale markers, panel and faction tags clear within 30 seconds; a no-plugin client does not show old faction state. Existing fallback behavior remains available. |
