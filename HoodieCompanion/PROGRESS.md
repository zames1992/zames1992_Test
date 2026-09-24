# Progress

## DONE

- Toolchain: .NET 8 WPF app cross-built from Linux (no WindowsDesktop SDK / XAML compiler needed); runs under Wine for QA.
- Character: measured reference → `CHARACTER_BIBLE.md`; vector cutout rig `rig.json` (IoU 0.96 vs reference);
  `tools/render_rig.py` renders it without Windows.
- Animation: 51 semantic clips (`ANIMATION_CATALOG.md`, generated from code), priorities, cross-fades,
  breath/blink/look overlays, distance-driven walk (no foot sliding); pose contact sheet renderer.
- Rendering: transparent, never-activating, per-pixel hit-tested window; physical-pixel placement; sub-pixel motion;
  effect marks (Zzz, !, ?, dust, sparkles, heat, arrow).
- Physics: gravity, sub-stepped collisions (floors = working areas, walls, ceilings), hard/soft landings, slide.
- Interaction: grab anywhere (hood is the natural handle), spring-follow + pendulum swing, throw from recent pointer
  velocity, throws continue across monitors; click → Quick Panel; right click → commands card; cursor look, curiosity,
  surprise, face-the-user turns; obstruction step-aside + temporary shyness.
- World: multi-monitor walking, hop up, drop down, explicit poof fallback; DPI-scaled perceived size; display-change
  handling; respawn safety.
- Presence: Normal / Company / Play / Focus / Quiet / Alone, Stay here / You're free, Home, Go home, per-monitor rules,
  drawn regions (Allowed / Quiet / Pass through / Never enter), fullscreen hide/step-aside, per-app rules (Quiet/Avoid/Hide).
- Backpack: drag-and-drop of files/folders/apps/shortcuts/links, catch→inspect→store sequence, shell icons cached,
  search, pin, rename, remove (reference only), show in folder, missing-item flow (Locate / Remove / Cancel), persistence.
- Utilities: timers (presets + custom, persisted), reminders (in N min / at HH:mm, snooze, done), quick notes.
- PC Status: CPU, RAM, disk, network, uptime, Hoodie's own CPU/RAM, 1 Hz; rare cooled-down PC-busy / download reactions.
- UI: attached Quick Panel (animated), alert cards, settings window, territory editor overlays, tray menu,
  emergency hide (tray + Ctrl+Alt+H), single instance (second launch summons Hoodie), start with Windows,
  reduced motion, sounds toggle, first-run hello card.
- Persistence: settings, territory, inventory, notes, reminders, timers — atomic writes + .bak + schemaVersion.
- Quality: 38 unit/simulation tests; automated end-to-end QA scenario (`--qa`) on the packaged exe: 16/16 PASS under Wine
  and 16/16 PASS on a real Windows CI runner (plus single-instance check).
- Release: `build-release.bat` (Windows) / `build-release.sh` (cross-build) → `publish/win-x64/HoodieCompanion.exe`
  (self-contained single file) + `publish/HoodieCompanion-win-x64.zip`; Windows CI workflow.

- v1.1 after user feedback: stuck-grab fix, working throws, inventory grid with drop target, accessories and
  activity animations (backpack, laptop for PC Status, notebook for Notes/Reminder, book, dance, jump for joy),
  ladder/rope climbing between monitors, livelier behaviour (faster walking, shorter decision gaps), richer PC Status
  (bars + 60 s graphs), fixed timer/reminder inputs, Russian language. 40 tests; QA scenario 20/20 PASS under Wine.

## FAILED (and what replaced it)

- Building with the WindowsDesktop SDK on Linux: not shipped in the Ubuntu SDK → switched to a plain
  `Microsoft.NET.Sdk` project with a `Microsoft.WindowsDesktop.App` framework reference and code-built views.
- First Wine QA run crashed in WPF font fallback (no Segoe UI in Wine) → font fallback lists in the app,
  Segoe UI stand-in font in the test prefix.
- "Leave me alone" took >14 s from mid-screen → exit speed now scales with distance (≤ ~5 s).
- QA snapshots were stretched by a VisualBrush → render element directly.

## NEXT

- Measure idle CPU/RAM on a desktop PC with a GPU (CI VM is GPU-less).
- Windows as platforms (stand on window title bars), richer obstruction detection.
- Elbow joints for nicer arm poses.

## BLOCKER

- No physical Windows machine in this environment: real-hardware DPI/multi-monitor/drag-and-drop checks rely on the
  CI runner and on the user's own test.
