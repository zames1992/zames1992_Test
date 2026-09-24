# Known issues and limitations

Honest list of what is missing, rough or unverified in this vertical slice.

## Verification

* **Built and tested without a physical Windows PC.** The release executable was cross-built with the .NET 8 SDK
  and exercised end-to-end under Wine (automated QA scenario: 16/16 checks passed, see
  `docs/qa/qa-report-wine.txt`), plus 38 unit/simulation tests of the platform-independent core.
  The GitHub Actions workflow `.github/workflows/hoodie-companion.yml` runs the same clean build, the unit tests,
  the automated QA scenario and a single-instance check on a real `windows-latest` machine — first run on
  Windows 10.0.26100: **16/16 QA checks PASS, single instance OK**, simulation 0.085 ms/frame, calm CPU 5.5 % of all
  cores and 307 MB peak working set on the GPU-less CI VM (during the QA run, which also renders snapshots).
* Not verified on real hardware: per-monitor **DPI transitions between monitors with different scaling**, real
  **multi-monitor** setups (the logic is covered by simulation tests with side-by-side, stacked and negative-coordinate
  layouts), tray icon behaviour on Windows 11, drag-and-drop from Explorer/browsers onto the character,
  `Ctrl+Alt+H` hotkey, fullscreen detection with real games/videos.
* Performance numbers from Wine are not representative (software rendering, no GPU): there the idle process uses
  ~4 % of all cores while calm and ~0.14 ms/frame of simulation. Expect far less on Windows; the CI QA report
  records the real numbers.

## Features

* **Windows and desktop icons as platforms** are read-only snapshots taken ~3× per second: Hoodie can stand on the
  visible part of a window's top edge (not on maximised windows) or on a desktop icon, rides along when the window moves
  and falls when it closes. Icon positions come from the desktop's shell view; with "Show desktop icons" off, or a
  third-party desktop replacement, icons are simply not used. Turn it off in Settings if you prefer.
* **GPU usage is not measured** (no reliable dependency-free source) and temperatures are out of scope.
  Disk activity uses the `PhysicalDisk` performance counter and shows "—" where counters are unavailable.
* **Obstruction avoidance is heuristic**: Hoodie steps aside when the pointer rests on it or when you click right
  next to it twice; it does not detect menus, dialogs or text carets under it.
* **Focus mode "avoid active work area"** = go Home / to a Quiet area / to the monitor corner farthest from the
  pointer, preferring a monitor without the foreground window. It does not track the exact caret.
* Monitors above / higher: Hoodie props up a ladder and climbs; below / lower: it ties a rope and climbs down.
  Monitors that only touch diagonally (no shared edge, < 2 body-widths of overlap) still use an explicit
  shrink-out / grow-in transition ("poof").
* Reminders support "in N minutes" and "at HH:mm" (next occurrence). No recurring reminders.
* The shell icon of a `.lnk` is shown, but the shortcut target is not resolved for display.
* Territory regions are stored relative to their monitor's working area; if a monitor is disconnected its
  regions are kept but ignored until it returns.
* The desktop right-click entry is a classic shell verb: on Windows 11 it is under "Show more options" (or Shift+F10).
* App search lists what Windows shows in Start (the Apps folder); if that cannot be read it falls back to Start-menu shortcuts.

## Fixed after the first user test (v1.1)

* Hoodie could stay stuck to the cursor (a lost mouse-button-up kept mouse capture, so nothing else could be clicked,
  settings sliders did not work, and after "Come back" it seemed to fly in circles). The real button state is now polled
  every frame; releasing the button always drops Hoodie. Covered by an automated QA check.
* Throwing onto another monitor did not work for the same reason (release not seen → no throw velocity).
* Timer / reminder inputs were reset every second → pages are built once; only countdowns refresh.
* The Backpack page closed when you switched to Explorer to drag files → working pages no longer auto-close, and the
  panel itself accepts drops.
* Monitors above/below: jump + teleport replaced by ladder / rope climbing.

## Fixed after the second user test (v1.2)

* Recycle Bin (and other shell objects) could not be put in the Backpack; folders opened slowly; no image previews; apps
  such as Calculator could not be added; slots could not be arranged.
* Sitting / walking showed no pelvis ("cut plates"); sleeping upright looked eerie → lies down.
* After a throw Hoodie jumped sideways on landing; grabbing always hung it from the hood; dropping it below the taskbar
  made it vanish and respawn.
* Territory editor: after choosing a tool the others were covered until Esc; the area names were unclear.

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
