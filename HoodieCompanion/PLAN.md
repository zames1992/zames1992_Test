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
  Companion/Behavior   PetController (+Movement, Life, Presence), PetStateMachine, BehaviorController, CharacterDrives
  Companion/Interaction CursorInteractionService
  Presence/            TerritoryModels, TerritoryService
  Features/            Backpack (InventoryItem, InventoryService), Notes, Reminders, Timers, SystemMonitor (SystemStatus, EnvironmentInterpreter)
  Settings/, Storage/  AppSettings, AppStorage
src/HoodieCompanion
  Platform/            NativeMethods, MonitorService, DpiService, MouseService, WindowInterop, FrameClock,
                       HotkeyService, ShellIconService, StartupService, SoundService, Log
  Presence/            FullscreenService (fullscreen + foreground app rules)
  Features/            SystemMonitorService, ShortcutService, DropHandler
  Companion/Rendering  CharacterRig (rig.json → WPF paths), EffectLayer
  UI/                  PetWindow, QuickPanel (+pages), AlertCard, SettingsWindow, TerritoryEditor, TrayIcon, Ui, PoseSheet
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
