# Hoodie Companion — Animation Catalog

Generated from `AnimationCatalog.cs` (the table the runtime uses). All clips are procedural poses of the cutout rig
in `Assets/rig.json` — no frame-by-frame raster art, so the silhouette, limb count and clothing can never drift.

## Movement

| Name | Narrative meaning | Trigger | System cause | Loop | Duration | Priority | Interruptible | Cooldown | Entry | Exit |
|---|---|---|---|---|---|---|---|---|---|---|
| IdleBreathing | Hoodie simply exists: breathing, small weight shifts. | Nothing else to do | None | Loop | loop | 10 | Yes | — | Any settled pose | Any clip |
| Blink | Alive and awake. | Random 2.5-6 s timer (overlay) | None | One-shot | 0.14 s | 0 | Yes | 2.5 s | Overlay on any awake clip | Eyes reopen |
| Walk | Going somewhere on its own feet. | Walk target chosen / user command | Behavior target | Loop | loop | 40 | Yes | — | Lean forward, first step | Slow, settle |
| Run | Hurrying: playing or answering a call. | Play mode chase, Come here from far away | User command / Play mode | Loop | loop | 40 | Yes | — | Lean, quicker steps | Decelerate to walk/idle |
| Turn | Changing its mind about direction. | New target behind Hoodie | Behavior target | One-shot | 0.26 s | 40 | No | — | Squash narrow | Face new direction |
| Jump | Crouch before leaving the ground. | Monitor above/beside is higher, Play hop | Monitor geometry | One-shot | 0.2 s | 60 | No | — | Crouch | Airborne |
| Airborne | In the air, going up. | Physics: not on ground, moving up | Physics | Loop | loop | 100 | No | — | Takeoff/throw | Fall |
| Fall | Falling down: arms up, eyes wide. | Physics: not on ground, moving down | Physics | Loop | loop | 100 | No | — | Apex | Landing |
| LandSoft | Touches down comfortably. | Landing impact below threshold | Physics | One-shot | 0.36 s | 80 | No | — | Ground contact squash | Idle |
| LandHard | Oof - a heavy landing, but no harm done. | Landing impact above threshold | Physics (strong throw) | One-shot | 0.8 s | 80 | No | — | Deep squash + dust | Recover |
| Recover | Regains balance. | After LandHard | Physics | One-shot | 0.6 s | 70 | No | — | Stand up | Idle / RecoverFromThrow |

## Rest

| Name | Narrative meaning | Trigger | System cause | Loop | Duration | Priority | Interruptible | Cooldown | Entry | Exit |
|---|---|---|---|---|---|---|---|---|---|---|
| SitDown | Decides to rest here for a while. | Low energy, Quiet/Focus mode, Company mode near cursor | Internal drive / presence mode | One-shot | 0.55 s | 20 | No | — | Slow, bend | SitIdle |
| SitIdle | Resting, watching the world. | After SitDown | Internal drive | Loop | loop | 12 | Yes | — | SitDown | StandUp / SleepStart |
| StandUp | Done resting. | Any activity that needs standing | Behavior | One-shot | 0.45 s | 30 | No | — | SitIdle | Idle |
| SleepStart | Getting drowsy, eyes closing. | Very low energy, long user inactivity, PC sleep | User inactivity / drive | One-shot | 1.2 s | 15 | Yes | — | SitIdle | SleepLoop |
| SleepLoop | Asleep. The world is quiet. | After SleepStart | User inactivity / drive | Loop | loop | 12 | Yes | — | SleepStart | WakeUp |
| WakeUp | Wakes up, small stretch. | User returns, click, drive | User input | One-shot | 1.1 s | 30 | No | — | SleepLoop | SitIdle / StandUp |
| Stretch | Stretches after staying still. | Long idle | Internal drive | One-shot | 1.6 s | 20 | Yes | 45 s | Idle | Idle |
| Yawn | Getting a bit sleepy. | Low energy | Internal drive | One-shot | 1.4 s | 20 | Yes | 60 s | Idle | Idle |

## Cursor

| Name | Narrative meaning | Trigger | System cause | Loop | Duration | Priority | Interruptible | Cooldown | Entry | Exit |
|---|---|---|---|---|---|---|---|---|---|---|
| LookCursor | Notices the user's hand nearby. | Cursor within look radius (overlay) | Cursor position | Loop | loop | 0 | Yes | — | Overlay | Overlay fades |
| FollowCursor | Chasing the user's hand for fun. | Play mode, cursor near the ground | Cursor position | Loop | loop | 40 | Yes | — | Run | Idle |
| Curious | What is that hand doing here? | Cursor hovers close for >1.5 s | Cursor position | One-shot | 1.3 s | 20 | Yes | 12 s | Head tilt | Idle |
| Wave | Friendly hello. | Left click, greeting, goodbye | User input | One-shot | 1.1 s | 25 | Yes | 2 s | Raise arm | Lower arm |
| Surprised | Startled by a fast approaching hand. | Cursor rushes towards Hoodie | Cursor velocity | One-shot | 0.7 s | 30 | Yes | 15 s | Small hop | Idle |

## Physical

| Name | Narrative meaning | Trigger | System cause | Loop | Duration | Priority | Interruptible | Cooldown | Entry | Exit |
|---|---|---|---|---|---|---|---|---|---|---|
| Grabbed | Being held by the hood: hood first, body follows, legs dangle. | User drags Hoodie | Direct manipulation | Loop | loop | 100 | No | — | GrabReaction | Thrown / Fall |
| GrabReaction | Whoa - picked up! | Drag begins | Direct manipulation | One-shot | 0.3 s | 100 | No | — | Any | Grabbed |
| Swinging | Swinging while held: limbs follow momentum. | Held and cursor moves fast | Direct manipulation | Loop | loop | 100 | No | — | Grabbed | Grabbed / Thrown |
| Thrown | Flying through its world. | Released with velocity | Direct manipulation + physics | Loop | loop | 100 | No | — | Release | Fall / Landing |
| RecoverFromThrow | Dusts itself off and looks at the user - no hard feelings. | After a hard landing | Physics | One-shot | 1.4 s | 70 | No | — | LandHard / Recover | Idle |

## Inventory

| Name | Narrative meaning | Trigger | System cause | Loop | Duration | Priority | Interruptible | Cooldown | Entry | Exit |
|---|---|---|---|---|---|---|---|---|---|---|
| NoticeItem | Something is coming towards it! | Drag enters Hoodie | OLE drag-enter | One-shot | 0.35 s | 60 | Yes | — | Look up | CatchItem |
| CatchItem | Catches the object the user gives it. | Drop on Hoodie | OLE drop | One-shot | 0.35 s | 60 | No | — | Arms up | InspectItem |
| InspectItem | Looks at the new object. | After catch | Inventory add | One-shot | 0.5 s | 60 | No | — | Holding item | PutInBackpack |
| PutInBackpack | Tucks it into its hoodie pocket for safe keeping. | After inspect | Inventory stored | One-shot | 0.45 s | 60 | No | — | Item to pocket | Idle (sparkle) |
| OpenBackpack | Opens its pocket to show what it keeps. | Backpack panel opens | User opened Backpack | One-shot | 0.5 s | 55 | Yes | — | Hands to pocket | SearchBackpack |
| SearchBackpack | Rummages in its pocket. | Backpack open / idle inventory check | Backpack UI / drive | Loop | loop | 50 | Yes | — | OpenBackpack | PresentItem / Idle |
| PresentItem | Hands the requested object back to the user. | User opens a Backpack item | Shell launch | One-shot | 0.7 s | 60 | No | — | Item from pocket | Idle |
| MissingItem | Searches and shrugs: the object is gone. | Stored target no longer exists | File missing | One-shot | 1.3 s | 60 | No | — | Search | Shrug |

## Utility

| Name | Narrative meaning | Trigger | System cause | Loop | Duration | Priority | Interruptible | Cooldown | Entry | Exit |
|---|---|---|---|---|---|---|---|---|---|---|
| ReminderAlert | Holds up the note it promised to remember. | Reminder due | Reminder service | Loop | loop | 80 | No | — | Raise note | Done/Snooze |
| TimerAlert | The clock it watched has run out - hops to tell you. | Timer finished | Timer service | Loop | loop | 80 | No | — | Hop | Acknowledge |
| Thinking | Considering / remembering. | Note being written | Notes | One-shot | 1 s | 25 | Yes | — | Hand to chin | Idle |
| Success | Got it! | Item stored, note saved, reminder set | Utility action | One-shot | 0.6 s | 25 | Yes | — | Small hop | Idle |
| Error | That did not work. | Launch failed | Utility action | One-shot | 0.8 s | 25 | Yes | — | Head shake | Idle |

## System

| Name | Narrative meaning | Trigger | System cause | Loop | Duration | Priority | Interruptible | Cooldown | Entry | Exit |
|---|---|---|---|---|---|---|---|---|---|---|
| PCBusy | The world is working hard and getting warm: fans itself. | CPU > 85% sustained | System monitor | One-shot | 2.6 s | 20 | Yes | 600 s | Wipe brow | Idle |
| PCIdle | Everything calmed down; relaxes. | Load drops after busy | System monitor | One-shot | 1.4 s | 15 | Yes | 300 s | Exhale | Idle |
| DownloadWatching | Watches something big arriving. | Network download > 2 MB/s sustained | System monitor | One-shot | 3 s | 15 | Yes | 600 s | Look up | Idle |

## World

| Name | Narrative meaning | Trigger | System cause | Loop | Duration | Priority | Interruptible | Cooldown | Entry | Exit |
|---|---|---|---|---|---|---|---|---|---|---|
| PeekEdge | Curiously peeks over the edge of its world. | Reaches a monitor edge | Monitor geometry | One-shot | 2 s | 20 | Yes | 30 s | Lean | Turn back |
| JumpMonitor | Leaps to another part of its world. | Target on a higher monitor | Monitor geometry | One-shot | 0.22 s | 60 | No | — | Crouch | Airborne |
| LandMonitor | Arrives on another monitor. | Landing after monitor jump | Monitor geometry | One-shot | 0.36 s | 80 | No | — | Squash | Idle |
| LeaveScreen | Politely leaves: little wave, walks out. | Leave me alone / fullscreen app | Presence mode | One-shot | 0.9 s | 70 | No | — | Wave | Walk off-screen |
| ReturnToScreen | Comes back into view. | Come back | Presence mode | One-shot | 0.9 s | 70 | No | — | Walk in | Wave / Idle |

