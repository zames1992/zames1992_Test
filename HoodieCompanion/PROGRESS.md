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

- v1.2 after the second user test: behaviour architecture first (Mind inner state, event→reaction table with priority
  tiers and interruption rule, idle director with frequency tiers, AFK timeline 5/15/30/60/90 min with wake-and-greet),
  then a 128-clip library in 19 states (emotions, cursor play, traversal, grab-anywhere hangs, lying/sleeping, diegetic
  PC load / downloads / notifications, tear-the-page). Grab by hood / hands / feet / body with a generalised pendulum;
  landing without the sideways jump; catching the taskbar edge and climbing up instead of respawning; pelvis in the rig
  (no gap when walking), hip sway and follow-through springs; sleep lying down. Backpack: Recycle Bin and other shell
  objects, background opening, thumbnails, app search (Calculator & Store apps), drag to reorder. Notes as a Sticky Notes
  board with colours, labels and desktop-pinned notes. Desktop right-click menu. Territory editor: tools always clickable,
  legend. Windows and desktop icons as platforms (jump / ladder up, ride, fall, hop down for the pointer), climbing the
  side of the screen, livelier ladder and rope (overshoot, wobble, sway). Grab detection follows the drawn pose.
  60 tests; QA scenario 34/34 PASS under Wine (3 consecutive runs).

- v1.3, the living character: Perception → Mind/Personality/Memory → Intent → Action pipeline (see PLAN.md). Event-driven
  window perception (WinEventHook), inferred typing, GPU load, long-session / away / night / game percepts; intents with
  reasons and a real "do nothing"; considerate behaviour while the user types; expressive-reaction budget; seven stable
  personality traits; bounded local memory (apps by name, places, counts, moments) with habituation; world items (fan,
  mug, ball, blanket) with new clips; passive progression by hours and days together; hoodie colours; Memories page;
  simplified Settings with Privacy and Performance/debug groups; performance watch. 81 tests (incl. a 12 h simulated
  soak); QA scenario 44/44 PASS under Wine.

## FAILED (and what replaced it)

- Building with the WindowsDesktop SDK on Linux: not shipped in the Ubuntu SDK → switched to a plain
  `Microsoft.NET.Sdk` project with a `Microsoft.WindowsDesktop.App` framework reference and code-built views.
- First Wine QA run crashed in WPF font fallback (no Segoe UI in Wine) → font fallback lists in the app,
  Segoe UI stand-in font in the test prefix.
- "Leave me alone" took >14 s from mid-screen → exit speed now scales with distance (≤ ~5 s).
- QA snapshots were stretched by a VisualBrush → render element directly.

## NEXT

- Measure idle CPU/RAM on a desktop PC with a GPU (CI VM is GPU-less), and a real 8–12 h run (see `perf` log lines).
- Next layers after v1.3, in order: deeper progression → inventory → collection → customization; lost-and-found items.
- Elbow joints for nicer arm poses.

## BLOCKER

- No physical Windows machine in this environment: real-hardware DPI/multi-monitor/drag-and-drop checks rely on the
  CI runner and on the user's own test.
