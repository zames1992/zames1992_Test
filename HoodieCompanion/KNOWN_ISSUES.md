# Known issues and limitations

Honest list of what is missing, rough or unverified in this vertical slice.

## Verification

* **Built and tested without a physical Windows PC.** The release executable was cross-built with the .NET 8 SDK
  and exercised end-to-end under Wine (automated QA scenario: 16/16 checks passed, see
  `docs/qa/qa-report-wine.txt`), plus 38 unit/simulation tests of the platform-independent core.
  The GitHub Actions workflow `.github/workflows/hoodie-companion.yml` runs the same clean build, the unit tests,
  the automated QA scenario and a single-instance check on a real `windows-latest` machine.
* Not verified on real hardware: per-monitor **DPI transitions between monitors with different scaling**, real
  **multi-monitor** setups (the logic is covered by simulation tests with side-by-side, stacked and negative-coordinate
  layouts), tray icon behaviour on Windows 11, drag-and-drop from Explorer/browsers onto the character,
  `Ctrl+Alt+H` hotkey, fullscreen detection with real games/videos.
* Performance numbers from Wine are not representative (software rendering, no GPU): there the idle process uses
  ~4 % of all cores while calm and ~0.14 ms/frame of simulation. Expect far less on Windows; the CI QA report
  records the real numbers.

## Features

* **Application windows are not platforms yet.** Hoodie walks on monitor floors (working-area bottoms) and uses
  monitor edges; it does not stand on or climb windows.
* **GPU usage is not measured** (no reliable dependency-free source) and temperatures are out of scope.
  Disk activity uses the `PhysicalDisk` performance counter and shows "—" where counters are unavailable.
* **Obstruction avoidance is heuristic**: Hoodie steps aside when the pointer rests on it or when you click right
  next to it twice; it does not detect menus, dialogs or text carets under it.
* **Focus mode "avoid active work area"** = go Home / to a Quiet area / to the monitor corner farthest from the
  pointer, preferring a monitor without the foreground window. It does not track the exact caret.
* Monitor above/below travel uses a jump (up) or a drop (down) and needs ≥ 2 body-widths of horizontal overlap;
  otherwise Hoodie uses an explicit shrink-out / grow-in transition ("poof") instead of walking.
* Reminders support "in N minutes" and "at HH:mm" (next occurrence). No recurring reminders.
* The shell icon of a `.lnk` is shown, but the shortcut target is not resolved for display.
* Territory regions are stored relative to their monitor's working area; if a monitor is disconnected its
  regions are kept but ignored until it returns.
* The first-run welcome card and all UI text are English only.

## Visual

* Arms are single rigid sleeves (no elbows): big arm poses (stretch, wave, fall) look slightly "noodly".
* Sitting is drawn with legs stretched forward (3/4 view); the far leg overlaps the near one.
* Turning is a quick horizontal squash through the middle rather than a drawn 3/4 turn.
* The Quick Panel does not follow Hoodie if it walks away while the panel is open (it stays where it opened).
* Sound cues are synthesized sine tones (soft, optional).

## Packaging

* The self-contained single-file executable is ~66 MB (compressed .NET 8 + WPF runtime). WPF does not support
  trimming, so it cannot be made much smaller without requiring an installed .NET Desktop Runtime.
* The executable is not code-signed; Windows SmartScreen may warn on first launch ("More info → Run anyway").
