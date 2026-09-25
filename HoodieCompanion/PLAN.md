# Plan

## Architecture decisions

| Decision | Reason |
|---|---|
| **C# / .NET 8 / WPF**, no XAML compiler (views composed in C#, one runtime-loaded theme dictionary) | Only needs `Microsoft.WindowsDesktop.App`; cross-builds from Linux with `EnableWindowsTargeting`, so the same sources build on any CI |
| **Two projects**: `HoodieCompanion.Core` (net8.0, pure logic) + `HoodieCompanion` (WPF/Win32 shell) | Behavior, physics, animation, territory, storage and features are unit-testable and simulated headless |
| **Vector cutout rig from JSON** traced from the reference | Stable identity: no disappearing limbs, no drift, DPI-independent, tiny |
| **Procedural animation** (pure functions of clip time) + cross-fades + overlays (breath, blink, look) | Smooth, deterministic, no sprite sliding; walk phase driven by distance travelled → no foot sliding |
| **World in virtual-desktop physical pixels**, speeds in DIP × monitor scale | One coordinate system for all monitors (negative coordinates, vertical stacks); perceived size constant across DPI |
| **Frame clock**: `CompositionTarget.Rendering` + Stopwatch delta time; low-rate timer when calm | Frame-synchronised motion, frame-rate independent simulation, tiny CPU when nothing moves |
| **Window placement with SetWindowPos in physical pixels**; rig drawn with a matrix including the sub-pixel remainder | No WPF Left/Top DPI rounding jitter; smooth sub-pixel motion |
| Behavior state separate from animation clip | `PetStateMachine` (what) vs `AnimationController` (how it looks); priorities per catalog |
| Local JSON with atomic replace + `.bak` + `schemaVersion` | Robust persistence, no dependencies, no cloud |

## Module map

```
src/HoodieCompanion.Core
  Geometry/            Vec2, RectD, WorldGeometry (monitors, floors, walls, ceilings)
  Companion/Animation  Pose, AnimClip, AnimationCatalog, ProceduralAnimator, AnimationController, RigTransform
  Companion/Physics    BodyMetrics, PetPhysics, GrabController, ThrowController
  Companion/Behavior   PetController (+Movement, Life, Presence, Activities, Climb, Mind, Surfaces), PetStateMachine,
                       BehaviorController, CharacterDrives, Mind, ReactionSystem, IdleDirector
  Companion/Interaction CursorInteractionService
  Presence/            TerritoryModels, TerritoryService
  Features/            Backpack (InventoryItem, InventoryService), Notes, Reminders, Timers, SystemMonitor (SystemStatus, EnvironmentInterpreter)
  Settings/, Storage/  AppSettings, AppStorage
src/HoodieCompanion
  Platform/            NativeMethods, MonitorService, DpiService, MouseService, WindowInterop, FrameClock,
                       HotkeyService, ShellIconService, ShellInterop (shell items, thumbnails, Apps folder),
                       DesktopMenuService, CommandChannel, SurfaceScanner (window tops + desktop icons),
                       StartupService, SoundService, Log
  Presence/            FullscreenService (fullscreen + foreground app rules)
  Features/            SystemMonitorService, ShortcutService, DropHandler
  Companion/Rendering  CharacterRig (rig.json → WPF paths), EffectLayer
  UI/                  PetWindow, QuickPanel (+pages), StickyNoteWindow, AlertCard, SettingsWindow, TerritoryEditor,
                       WorldPropWindow, TrayIcon, Ui, PoseSheet
  AppHost.cs           composition root, frame loop, system-event → world-event mapping
  QaRunner.cs          automated end-to-end QA scenario (--qa)
tests/HoodieCompanion.Tests  geometry, physics, controller simulation, animation/rig validation, features, storage
```

## Sprint phases (as executed)

1. Audit / plan — reference measured, architecture fixed, toolchain proven (cross-built WPF runs under Wine).
2. Character rig — traced rig.json, compared against the reference (silhouette IoU 0.945 → 0.961 after corrections).
3. Window / render foundation — transparent no-activate window, frame clock, physical-pixel placement.
4. Animation / locomotion — procedural clips, distance-driven walk, turns, jumps, landings, sit/sleep.
5. Multi-monitor / DPI — world geometry, floors/walls/ceilings, monitor transitions (walk, hop, drop, poof).
6. Physical interaction — spring-follow grab by the hood, pendulum swing, throw velocity estimator, physics.
7. Presence / territory — Home, Stay here, per-monitor rules, regions, Normal/Company/Play/Focus/Quiet/Alone.
8. Backpack — references, persistence, drag-and-drop, open/pin/rename/remove/locate.
9. Quick Panel / UI — attached panel, pages, alert cards, settings, territory editor, tray.
10. PC Status — CPU/RAM/uptime/net/disk at 1 Hz, rare cooled-down reactions.
11. Utilities — timer, reminders, notes.
12. Polish — font fallbacks, leave speed, calm frame rate, snapshots.
13. Release build — `build-release.bat` / `build-release.sh`, single-file self-contained exe + zip.
14. Final QA — automated scenario on the packaged exe (Wine) + Windows CI workflow.

## Behaviour architecture (v1.2)

Designed first, the clip library sits on top of it.

```
 system / user events ──► ReactionSystem ──(priority check)──► PetController ──► AnimationController ──► rig
        ▲                    (event → weighted clip chain,          │   (behaviour state machine:      (crossfade, blink,
        │                     cooldowns, calm-mode filter)          │    Idle, Walking, Sitting,        look, follow-through
   Mind (inner state) ◄───────────────────────────────────────────┤    Sleeping, Grabbed, Airborne,    springs)
   energy, mood, curiosity, attention, boredom, sleepiness,        │    Climbing, Activity, Alert...)
   stress, affection, activity + context (target, zone, held       │
   item, backpack, user activity, PC load, time since interaction)│
        │                                                          │
        └──► BehaviorController (weighted activity choice) ◄──────┤ between decisions:
        └──► IdleDirector (frequent / occasional / rare tiers) ◄──┘ micro-actions keep the body alive
```

**Animation state machine.** Behaviour states (`PetStateMachine`) say *what* Hoodie does; clips say *how it looks*.
Every clip belongs to one of 19 state groups (Idle basic, Idle curious, Idle bored, Idle playful, Movement, Screen
traversal, Cursor interaction, Drag interaction, Work states, PC load, Download/process, Notification, Success, Failure,
Emotions, Sleep/AFK, Inventory, Environment, Boundaries, Spawn/despawn) and has a frequency tier. See `ANIMATION_CATALOG.md`.

**Priorities and interruption.** Tiers, highest first: Safety (hide, fullscreen, Alone) › Physical (grab, throw, fall,
landing, climbing) › Command › Alert › Utility (backpack, notes) › Contextual (cursor, PC load, downloads, clock) ›
Idle micro-actions › Base loops. A reaction starts only if its tier outranks what is running
(`ReactionSystem.MayInterrupt`); physical interaction always wins; a resting Hoodie only gets up for the user.

**Event → reaction.** `ReactionSystem.Rules` maps each `PetEvent` (cursor approached/rushed, clicked, grabbed, landed hard,
item stored/opened/missing, reminder ignored, PC busy/calm, download, task succeeded/failed, boundary hit, user returned,
night, hour chime, note saved/abandoned...) to weighted options (single clips or chains) with conditions on the Mind
and the presence mode, plus a cooldown. Focus / Quiet filter out contextual reactions.

**Idle director.** Blink every few seconds (overlay); *frequent* moments every ~6–14 s (look around, head tilt, weight
shift, breathing variant swap); *occasional* every ~25–60 s (scratch, stretch, inspect itself, listen, sigh, stare,
yawn; chin rest / picking at the floor / counting fingers while sitting); *rare* every ~3–6 min (spin, dance, swipe at
the cursor, balance, proud pose). Weighted by the Mind, never the same clip twice in a row, stretched ×2.5 in calm modes.
Contextual clips never come from the director.

**AFK timeline** (minutes without input): 5 relaxed (slower decisions) → 15 bored (sighs, sits on edges, lies around) →
30 explores the desktop (other monitors, edges) → 60 sleepy (yawns, lies down) → 90 goes to Home / a quiet spot and sleeps
lying down. When the user returns: wake up → notice the pointer → recognise → greet (wave / happy / excited) → normal.

**Diegetic system functions.** Heavy CPU load = Hoodie hauls a crate; downloads = parcels falling from above that it
catches; errors = facepalm / confused / frustrated; an ignored reminder = it knocks on the glass and points.

**Physical interaction.** Grab anywhere: hood, either hand, a foot, the body. The grab point is the pivot of a damped
pendulum whose rest angle puts the centre of mass straight below it (a foot grab hangs upside down). Landing keeps the
feet exactly where they were drawn and lets the body rotate upright around them (no sideways jump). Released below a
floor with nothing underneath (over the taskbar), Hoodie grabs the edge, hangs, and climbs up. Follow-through springs
(head, sleeves, strings) react to body acceleration; runs end with a skid. Grab regions are found on the pose as it is
drawn (`PosedRig`), so a sitting or sleeping Hoodie can be picked up by a hand or a foot too.

**Platforms.** `SurfaceScanner` (own STA thread, ~3 Hz) reports the visible parts of window top edges (z-order occlusion,
maximised/cloaked/tool windows skipped) and desktop icon tops (desktop `IFolderView`). Hoodie jumps onto low ones, props a
ladder for high ones, sits on their edge, rides a moving window, hops down when the pointer approaches a title bar, and
falls when the platform disappears. It can also climb the side of a screen that has no neighbour and slide back down.

## Living character architecture (v1.3)

Hoodie is a small autonomous creature, not a widget. Everything it does goes through one pipeline; none of it is shown
as UI.

```
 PC / user / environment ──► Perception ──► Mind + Personality + Memory ──► Intent ──► Action ──► Animation
 (window events, idle time,   (percepts)     (inner state, stable traits,    (scored     (walk,      (clips, props,
  typing yes/no, CPU/GPU,                     habituation, favourite         options     climb,      reactions)
  network, clock, monitors)                   places, moments)               + reason)   use item)       │
        ▲                                                                                              │
        └──────────────────────── reaction changes the inner state and memory ◄───────────────────────┘
```

* **Perception** (`Companion/Perception`). The host feeds a per-frame `EnvironmentSample` (input idle time, whether a
  key was pressed, foreground process name / bounds / fullscreen, CPU, GPU, network, hour) and event-driven window
  events from a `SetWinEventHook` (`Platform/WindowEvents.cs`: opened, closed, minimised, drag start/end, foreground).
  `PerceptionSystem` turns them into percepts: *window opened/closed/dragged, app switched, app seen for the first time,
  typing started/stopped, long work session (55 min without a 5 min break), user away/returned, PC hot/cooled (≥85 %
  load for 20 s), download running, game started/ended, night fell*. It never reads key values, titles or contents.
  "Typing" is inferred from input activity while the pointer is still. GPU load is the 3D engine share from the
  `GPU Engine` performance counters.
* **Mind and Personality.** `Mind` is the drifting inner state (v1.2). `Personality` holds seven stable traits
  (curiosity, energy, confidence, playfulness, attachment, comfort, caution), seeded once per install and shaped
  slowly by memory (time together grows attachment, scares grow caution, climbs grow confidence). One trait nudges
  many options: curiosity makes exploring, peeking, climbing and investigating all more likely.
* **Memory** (`Companion/Memory/CompanionMemory.cs`, `memory.json`). Bounded, local, safe facts: days and minutes
  together, active hours, app names with category and minutes in front, favourite resting spots, event counts (for
  habituation: `1 − e^(−n/6)`), interactions, memorable moments, found items and colours. Never typed text, titles,
  documents or messages. Saved at most once a minute. Settings → Privacy can switch app learning and typing
  detection off, and forget everything.
* **Intent** (`Behavior/IntentSystem.cs`). Each decision scores all options (classic activities plus *do nothing*,
  investigate a new window, cool down with the fan, suggest a break, work alongside, go to a favourite spot, play ball,
  watch the user) and keeps a human-readable reason ("the computer is hot, fetch the fan", "you're busy, so it keeps
  quiet"). A busy user (typing in the last 12 s) makes loud options ×0.2 and doing nothing +3; gaming and night calm
  things down further. The pick is weighted by score², so strong preferences win without becoming clockwork.
* **Being considerate** (priority 1). While you type, Hoodie walks off the active (non-maximised) window or steps
  aside from the pointer, at most every 20 s. Expressive contextual reactions are rationed: one per 90 s (240 s while
  you are busy); micro-reactions are exempt. The user's territory and modes always win over autonomy.
* **Contextual moments.** Rare and memorable: a new window nearby → Hoodie looks, and a curious one walks over to look
  up at it; the window it stands on is dragged → it balances and remembers the ride; the window under it closes → it
  falls and is scared (later only annoyed); the PC runs hot → it takes the fan out of its backpack and fans itself;
  after a long session, when you pause, it walks up with its mug and suggests a break; the first activity of a new
  day gets a greeting; games make it sit and watch.
* **Items and passive progression** (`Progression.cs`). Mug, ball, fan and blanket, a spin, a dance, wall climbing,
  and four hoodie colours unlock from hours spent together *and* number of different days (never streaks, never
  lost). Hoodie "finds" a new item at a calm moment: opens the backpack, rummages, shows it to you. Items are rig
  groups (`fan`, `mug`, `ball`, `blanket`) drawn in the hand or on the floor.
* **Performance budget.** Window changes are event-driven (the scanner rescans on events, 120 ms while a window is
  dragged, 1.5–3 s otherwise, and rests while Hoodie sleeps). Perception and memory run on wall time, so a slow frame
  loop never loses time together. `PerformanceWatch` samples CPU, working set, managed heap, handles, GDI and USER
  objects once a minute, logs every 10 minutes and warns on growth; a 12-hour simulated soak test checks bounds and
  memory growth.
