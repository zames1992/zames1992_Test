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
  Companion/Behavior   PetController (+Movement, Life, Presence, Activities, Climb, Mind), PetStateMachine,
                       BehaviorController, CharacterDrives, Mind, ReactionSystem, IdleDirector
  Companion/Interaction CursorInteractionService
  Presence/            TerritoryModels, TerritoryService
  Features/            Backpack (InventoryItem, InventoryService), Notes, Reminders, Timers, SystemMonitor (SystemStatus, EnvironmentInterpreter)
  Settings/, Storage/  AppSettings, AppStorage
src/HoodieCompanion
  Platform/            NativeMethods, MonitorService, DpiService, MouseService, WindowInterop, FrameClock,
                       HotkeyService, ShellIconService, ShellInterop (shell items, thumbnails, Apps folder),
                       DesktopMenuService, CommandChannel, StartupService, SoundService, Log
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
(head, sleeves, strings) react to body acceleration; runs end with a skid.
