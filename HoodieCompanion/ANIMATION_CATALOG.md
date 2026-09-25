# Hoodie Companion — Animation Catalog

Generated from `AnimationCatalog.cs` (the table the runtime uses). All clips are procedural poses of the cutout rig
in `Assets/rig.json` — no frame-by-frame raster art, so the silhouette, limb count and clothing can never drift.

**132 clips** in 19 behaviour states. See PLAN.md for the state machine, the event→reaction system and the idle director.

## Idle basic

| Name | Narrative meaning | Trigger | System cause | Loop | Duration | Priority | Interruptible | Cooldown | Frequency | Entry | Exit |
|---|---|---|---|---|---|---|---|---|---|---|---|
| IdleBreathing | Hoodie simply exists: breathing, small weight shifts. | Nothing else to do | None | Loop | loop | 10 | Yes | — | Frequent | Any settled pose | Any clip |
| Blink | Alive and awake. | Random 2.5-6 s timer (overlay) | None | One-shot | 0.14 s | 0 | Yes | 2.5 s | Frequent | Overlay on any awake clip | Eyes reopen |
| Stretch | Stretches after staying still. | Long idle | Internal drive | One-shot | 1.3 s | 20 | Yes | 45 s | Occasional | Idle | Idle |
| Yawn | Getting a bit sleepy. | Low energy | Internal drive | One-shot | 1.4 s | 20 | Yes | 60 s | Occasional | Idle | Idle |
| IdleBreathing2 | Relaxed stance, weight on one leg. | Idle base loop variant | Idle director | Loop | loop | 10 | Yes | — | Frequent | Blend | Blend |
| LookLeft | Glances to one side. | Idle gesture | Idle director | One-shot | 1.6 s | 12 | Yes | 6 s | Frequent | Blend | Blend |
| LookRight | Glances to the other side. | Idle gesture | Idle director | One-shot | 1.6 s | 12 | Yes | 6 s | Frequent | Blend | Blend |
| LookUp | Looks up at the screen above. | Idle gesture | Idle director | One-shot | 1.8 s | 12 | Yes | 8 s | Frequent | Blend | Blend |
| LookDown | Looks at its feet. | Idle gesture | Idle director | One-shot | 1.6 s | 12 | Yes | 8 s | Frequent | Blend | Blend |
| HeadTilt | Tilts its head, thinking about something. | Idle gesture | Idle director | One-shot | 1.8 s | 12 | Yes | 8 s | Frequent | Blend | Blend |
| WeightShift | Shifts its weight from foot to foot. | Idle gesture | Idle director | One-shot | 1.6 s | 12 | Yes | 6 s | Frequent | Blend | Blend |
| StretchBody | Leans back and stretches the whole body. | Rare idle | Idle director / morning | One-shot | 1.5 s | 20 | Yes | 60 s | Occasional | Blend | Blend |
| Scratch | Scratches its hood. | Rare idle | Idle director | One-shot | 1.4 s | 20 | Yes | 50 s | Occasional | Blend | Blend |
| InspectSelf | Looks at its hands and sleeves. | Rare idle | Idle director | One-shot | 2 s | 20 | Yes | 70 s | Occasional | Blend | Blend |

## Idle curious

| Name | Narrative meaning | Trigger | System cause | Loop | Duration | Priority | Interruptible | Cooldown | Frequency | Entry | Exit |
|---|---|---|---|---|---|---|---|---|---|---|---|
| LookCursor | Notices the user's hand nearby. | Cursor within look radius (overlay) | Cursor position | Loop | loop | 0 | Yes | — | Frequent | Overlay | Overlay fades |
| Curious | What is that hand doing here? | Cursor hovers close for >1.5 s | Cursor position | One-shot | 1.3 s | 20 | Yes | 12 s | Contextual | Head tilt | Idle |
| PeekEdge | Curiously peeks over the edge of its world. | Reaches a monitor edge | Monitor geometry | One-shot | 2 s | 20 | Yes | 30 s | Contextual | Lean | Turn back |
| Listen | Listens to something only it can hear. | Idle gesture | Idle director | One-shot | 2 s | 15 | Yes | 30 s | Occasional | Blend | Blend |
| NoticeMovement | Something moved! A quick glance. | Window moved / big cursor move | Desktop events | One-shot | 0.8 s | 18 | Yes | 10 s | Contextual | Blend | Blend |
| WatchWindow | Watches you work on the active window. | User typing / Company mode | User activity | One-shot | 3 s | 15 | Yes | 20 s | Occasional | Blend | Blend |

## Idle bored

| Name | Narrative meaning | Trigger | System cause | Loop | Duration | Priority | Interruptible | Cooldown | Frequency | Entry | Exit |
|---|---|---|---|---|---|---|---|---|---|---|---|
| SitDown | Decides to rest here for a while. | Low energy, Quiet/Focus mode, Company mode near cursor | Internal drive / presence mode | One-shot | 0.55 s | 20 | No | — | Contextual | Slow, bend | SitIdle |
| SitIdle | Resting, watching the world. | After SitDown | Internal drive | Loop | loop | 12 | Yes | — | Contextual | SitDown | StandUp / SleepStart |
| StandUp | Done resting. | Any activity that needs standing | Behavior | One-shot | 0.45 s | 30 | No | — | Contextual | SitIdle | Idle |
| SitEdge | Sits on an edge and swings its legs. | Bored / at an edge | Idle director | Loop | loop | 12 | Yes | — | Contextual | Sit on edge | Hop down |
| ChinRest | Rests its chin on a hand, bored. | Sitting + boredom | Mind: boredom | One-shot | 3.5 s | 12 | Yes | 20 s | Occasional | Blend | Blend |
| PickSurface | Pokes at the floor. | Sitting + boredom | Mind: boredom | One-shot | 2.6 s | 12 | Yes | 25 s | Occasional | Blend | Blend |
| Sigh | A long sigh. | Boredom | Mind: boredom | One-shot | 1.6 s | 15 | Yes | 60 s | Occasional | Blend | Blend |
| CountFingers | Counts something on its fingers. | Boredom | Mind: boredom | One-shot | 2.4 s | 15 | Yes | 80 s | Rare | Blend | Blend |
| StareVoid | Stares into nothing. | Very bored / AFK | Mind: boredom | One-shot | 4 s | 12 | Yes | 40 s | Occasional | Blend | Blend |
| LieDown | Lies down on the floor. | Sleepy / very bored | Mind: sleepiness | One-shot | 1.1 s | 15 | No | — | Contextual | Sit or stand | LieIdle / SleepLying |
| LieIdle | Lies on its back, looking at the sky, kicking a foot. | Bored, lying | Mind: boredom | Loop | loop | 12 | Yes | — | Contextual | Blend | Blend |

## Idle playful

| Name | Narrative meaning | Trigger | System cause | Loop | Duration | Priority | Interruptible | Cooldown | Frequency | Entry | Exit |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Dance | A happy little dance. | Play mode / good mood | Internal drive | One-shot | 2.4 s | 20 | Yes | 25 s | Rare | Bounce | Idle |
| JumpForJoy | Jumps for joy. | Success, greeting in Play mode | Utility / drive | One-shot | 0.8 s | 25 | Yes | 8 s | Contextual | Crouch | Idle |
| Spin | Twirls around once. | Playful | Mind: playfulness | One-shot | 0.9 s | 20 | Yes | 25 s | Rare | Blend | Blend |
| CatchCursor | Jumps to swipe at your cursor. | Play mode, cursor close above | Cursor | One-shot | 0.9 s | 25 | Yes | 6 s | Rare | Blend | Blend |

## Movement

| Name | Narrative meaning | Trigger | System cause | Loop | Duration | Priority | Interruptible | Cooldown | Frequency | Entry | Exit |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Walk | Going somewhere on its own feet. | Walk target chosen / user command | Behavior target | Loop | loop | 40 | Yes | — | Contextual | Lean forward, first step | Slow, settle |
| Run | Hurrying: playing or answering a call. | Play mode chase, Come here from far away | User command / Play mode | Loop | loop | 40 | Yes | — | Contextual | Lean, quicker steps | Decelerate to walk/idle |
| Turn | Changing its mind about direction. | New target behind Hoodie | Behavior target | One-shot | 0.26 s | 40 | No | — | Contextual | Squash narrow | Face new direction |
| Jump | Crouch before leaving the ground. | Monitor above/beside is higher, Play hop | Monitor geometry | One-shot | 0.2 s | 60 | No | — | Contextual | Crouch | Airborne |
| Airborne | In the air, going up. | Physics: not on ground, moving up | Physics | Loop | loop | 100 | No | — | Contextual | Takeoff/throw | Fall |
| Fall | Falling down: arms up, eyes wide. | Physics: not on ground, moving down | Physics | Loop | loop | 100 | No | — | Contextual | Apex | Landing |
| LandSoft | Touches down comfortably. | Landing impact below threshold | Physics | One-shot | 0.36 s | 80 | No | — | Contextual | Ground contact squash | Idle |
| LandHard | Oof - a heavy landing, but no harm done. | Landing impact above threshold | Physics (strong throw) | One-shot | 0.8 s | 80 | No | — | Contextual | Deep squash + dust | Recover |
| Recover | Regains balance. | After LandHard | Physics | One-shot | 0.6 s | 70 | No | — | Contextual | Stand up | Idle / RecoverFromThrow |
| FollowCursor | Chasing the user's hand for fun. | Play mode, cursor near the ground | Cursor position | Loop | loop | 40 | Yes | — | Contextual | Run | Idle |
| Stop | Stops with a little skid and overshoot. | End of a fast walk/run | Behaviour | One-shot | 0.35 s | 35 | Yes | — | Contextual | Blend | Blend |
| Slip | Slips, flails, recovers. | Rare while running | Physics flavour | One-shot | 1 s | 45 | No | 90 s | Contextual | Blend | Blend |
| Balance | Balances on a narrow edge. | At a platform edge | Platforms | One-shot | 1.8 s | 20 | Yes | 30 s | Rare | Blend | Blend |

## Screen traversal

| Name | Narrative meaning | Trigger | System cause | Loop | Duration | Priority | Interruptible | Cooldown | Frequency | Entry | Exit |
|---|---|---|---|---|---|---|---|---|---|---|---|
| JumpMonitor | Leaps to another part of its world. | Target on a higher monitor | Monitor geometry | One-shot | 0.22 s | 60 | No | — | Contextual | Crouch | Airborne |
| LandMonitor | Arrives on another monitor. | Landing after monitor jump | Monitor geometry | One-shot | 0.36 s | 80 | No | — | Contextual | Squash | Idle |
| LeaveScreen | Politely leaves: little wave, walks out. | Leave me alone / fullscreen app | Presence mode | One-shot | 0.9 s | 70 | No | — | Contextual | Wave | Walk off-screen |
| ReturnToScreen | Comes back into view. | Come back | Presence mode | One-shot | 0.9 s | 70 | No | — | Contextual | Walk in | Wave / Idle |
| PlaceLadder | Pulls out a ladder and props it against the higher part of its world. | Target monitor above / higher neighbour | Monitor geometry | One-shot | 0.8 s | 60 | No | — | Contextual | Reach up | ClimbLadder |
| ClimbLadder | Climbs rung by rung. | On the ladder | Monitor geometry | Loop | loop | 60 | No | — | Contextual | PlaceLadder | Step off at the top |
| TieRope | Ties a rope at the edge and lets it unroll downwards. | Target monitor below / lower neighbour | Monitor geometry | One-shot | 0.7 s | 60 | No | — | Contextual | Crouch at edge | ClimbRope |
| ClimbRope | Slides down the rope hand over hand. | On the rope | Monitor geometry | Loop | loop | 60 | No | — | Contextual | TieRope | Land at the bottom |
| PeekIn | Peeks in from the edge of the screen before coming in. | Come back | Presence | One-shot | 1.2 s | 70 | No | — | Rare | Blend | Blend |
| HangEdge | Hangs from an edge by its hands, legs kicking. | Dropped below the taskbar edge | Physics | Loop | loop | 90 | No | — | Contextual | Blend | Blend |
| ClimbEdge | Pulls itself up over the edge. | After HangEdge | Physics | One-shot | 0.9 s | 90 | No | — | Contextual | Blend | Blend |

## Cursor interaction

| Name | Narrative meaning | Trigger | System cause | Loop | Duration | Priority | Interruptible | Cooldown | Frequency | Entry | Exit |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Wave | Friendly hello. | Left click, greeting, goodbye | User input | One-shot | 0.9 s | 25 | Yes | 2 s | Contextual | Raise arm | Lower arm |
| Surprised | Startled by a fast approaching hand. | Cursor rushes towards Hoodie | Cursor velocity | One-shot | 0.7 s | 30 | Yes | 15 s | Contextual | Small hop | Idle |
| ReachCursor | Stands on tiptoes and reaches for your cursor. | Cursor just above | Cursor | One-shot | 1.4 s | 22 | Yes | 15 s | Contextual | Blend | Blend |
| Dodge | Dodges out of the way. | Cursor rushes at it (Quiet/Focus: instead of Surprised) | Cursor | One-shot | 0.6 s | 30 | Yes | 8 s | Contextual | Blend | Blend |
| Annoyed | Crosses its arms and taps its foot - enough poking. | Poked / clicked many times | Mind: stress | One-shot | 1.8 s | 30 | Yes | 20 s | Contextual | Blend | Blend |
| Happy | Happy wiggle - it likes the attention. | Gentle attention | Mind: affection | One-shot | 1.2 s | 25 | Yes | 10 s | Contextual | Blend | Blend |
| SearchCursor | Looks around for the cursor it lost. | Cursor disappeared far away | Cursor | One-shot | 2 s | 15 | Yes | 30 s | Contextual | Blend | Blend |

## Drag interaction

| Name | Narrative meaning | Trigger | System cause | Loop | Duration | Priority | Interruptible | Cooldown | Frequency | Entry | Exit |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Grabbed | Being held by the hood: hood first, body follows, legs dangle. | User drags Hoodie | Direct manipulation | Loop | loop | 100 | No | — | Contextual | GrabReaction | Thrown / Fall |
| GrabReaction | Whoa - picked up! | Drag begins | Direct manipulation | One-shot | 0.3 s | 100 | No | — | Contextual | Any | Grabbed |
| Swinging | Swinging while held: limbs follow momentum. | Held and cursor moves fast | Direct manipulation | Loop | loop | 100 | No | — | Contextual | Grabbed | Grabbed / Thrown |
| Thrown | Flying through its world. | Released with velocity | Direct manipulation + physics | Loop | loop | 100 | No | — | Contextual | Release | Fall / Landing |
| RecoverFromThrow | Dusts itself off and looks at the user - no hard feelings. | After a hard landing | Physics | One-shot | 1.4 s | 70 | No | — | Contextual | LandHard / Recover | Idle |
| HangHandL | Held by the hand: the arm reaches up, the body hangs from it. | Grabbed by a hand | Direct manipulation | Loop | loop | 100 | No | — | Contextual | Blend | Blend |
| HangHandR | Held by the other hand. | Grabbed by a hand | Direct manipulation | Loop | loop | 100 | No | — | Contextual | Blend | Blend |
| HangFoot | Held by a foot: upside down, arms and hood dangling. | Grabbed by a leg | Direct manipulation | Loop | loop | 100 | No | — | Contextual | Blend | Blend |
| HangTorso | Held by the body: arms and legs dangle. | Grabbed by the torso | Direct manipulation | Loop | loop | 100 | No | — | Contextual | Blend | Blend |
| Struggle | Wriggles to get free. | Held for long / swung hard | Mind: stress | Loop | loop | 100 | No | — | Contextual | Blend | Blend |
| RelaxedCarry | Relaxes and lets itself be carried. | Held gently for a while | Mind: affection | Loop | loop | 100 | No | — | Contextual | Blend | Blend |
| Dizzy | Dizzy after a wild ride: wobbles, stars circle its head. | Landing after heavy swinging | Physics + mind | One-shot | 2.2 s | 75 | No | — | Contextual | Blend | Blend |

## Work states

| Name | Narrative meaning | Trigger | System cause | Loop | Duration | Priority | Interruptible | Cooldown | Frequency | Entry | Exit |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Thinking | Considering / remembering. | Note being written | Notes | One-shot | 1 s | 25 | Yes | — | Contextual | Hand to chin | Idle |
| LaptopOpen | Sits down, pulls out a laptop and opens it. | PC Status opened / idle activity | PC Status UI / drive | One-shot | 0.7 s | 45 | No | — | Contextual | Sitting | LaptopType |
| LaptopType | Types away, checking how the computer is doing. | PC Status open / idle activity | PC Status UI / drive | Loop | loop | 40 | Yes | — | Contextual | LaptopOpen | LaptopClose |
| LaptopClose | Closes the laptop and tucks it away. | PC Status closed / activity over | PC Status UI / drive | One-shot | 0.55 s | 45 | No | — | Contextual | LaptopType | Sitting |
| ReadBook | Reads a book, turning a page now and then. | Quiet idle activity | Internal drive | Loop | loop | 20 | Yes | — | Contextual | Sitting | Sitting |
| WriteNotes | Writes in a little notebook. | Notes / Reminder opened, idle activity | Notes UI / drive | Loop | loop | 40 | Yes | — | Contextual | Pull out notebook | Put it away |
| CheckResult | Nods at the result. | Work finished | Utility | One-shot | 1 s | 25 | Yes | 5 s | Contextual | Blend | Blend |

## PC load

| Name | Narrative meaning | Trigger | System cause | Loop | Duration | Priority | Interruptible | Cooldown | Frequency | Entry | Exit |
|---|---|---|---|---|---|---|---|---|---|---|---|
| PCBusy | The world is working hard and getting warm: fans itself. | CPU > 85% sustained | System monitor | One-shot | 2.6 s | 20 | Yes | 600 s | Contextual | Wipe brow | Idle |
| PCIdle | Everything calmed down; relaxes. | Load drops after busy | System monitor | One-shot | 1.4 s | 15 | Yes | 300 s | Contextual | Exhale | Idle |
| CarryLoad | Hauls a heavy crate: the computer is doing heavy lifting. | CPU busy for a while | System monitor | Loop | loop | 25 | Yes | — | Contextual | Lift crate | Put down |

## Download/process

| Name | Narrative meaning | Trigger | System cause | Loop | Duration | Priority | Interruptible | Cooldown | Frequency | Entry | Exit |
|---|---|---|---|---|---|---|---|---|---|---|---|
| DownloadWatching | Watches something big arriving. | Network download > 2 MB/s sustained | System monitor | One-shot | 3 s | 15 | Yes | 600 s | Contextual | Look up | Idle |
| CatchPackage | Catches a parcel of data falling from above. | Download in progress | System monitor (network) | One-shot | 0.9 s | 25 | Yes | 4 s | Contextual | Blend | Blend |

## Notification

| Name | Narrative meaning | Trigger | System cause | Loop | Duration | Priority | Interruptible | Cooldown | Frequency | Entry | Exit |
|---|---|---|---|---|---|---|---|---|---|---|---|
| ReminderAlert | Holds up the note it promised to remember. | Reminder due | Reminder service | Loop | loop | 80 | No | — | Contextual | Raise note | Done/Snooze |
| TimerAlert | The clock it watched has run out - hops to tell you. | Timer finished | Timer service | Loop | loop | 80 | No | — | Contextual | Hop | Acknowledge |
| Knock | Knocks on the glass to get your attention. | Reminder / timer not acknowledged | Reminders | One-shot | 1.2 s | 80 | Yes | 6 s | Contextual | Blend | Blend |
| Point | Points at something. | Show where something is | Utility | One-shot | 1.4 s | 25 | Yes | 5 s | Contextual | Blend | Blend |

## Success

| Name | Narrative meaning | Trigger | System cause | Loop | Duration | Priority | Interruptible | Cooldown | Frequency | Entry | Exit |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Success | Got it! | Item stored, note saved, reminder set | Utility action | One-shot | 0.6 s | 25 | Yes | — | Contextual | Small hop | Idle |
| ThumbsUp | Raises a fist: well done! | Something completed | Utility | One-shot | 1.1 s | 25 | Yes | 5 s | Contextual | Blend | Blend |
| Proud | Puffs up, proud of itself. | Task done / praised | Mind: mood | One-shot | 1.6 s | 25 | Yes | 20 s | Rare | Blend | Blend |

## Failure

| Name | Narrative meaning | Trigger | System cause | Loop | Duration | Priority | Interruptible | Cooldown | Frequency | Entry | Exit |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Error | That did not work. | Launch failed | Utility action | One-shot | 0.8 s | 25 | Yes | — | Contextual | Head shake | Idle |
| Confused | Confused: head tilts, scratches its hood. | Unexpected result | Utility | One-shot | 1.8 s | 25 | Yes | 8 s | Contextual | Blend | Blend |
| Frustrated | Stomps its foot. | Repeated failure | Mind: stress | One-shot | 1.3 s | 25 | Yes | 20 s | Contextual | Blend | Blend |
| Facepalm | Facepalm. | Silly mistake | Utility | One-shot | 1.5 s | 25 | Yes | 20 s | Contextual | Blend | Blend |

## Emotions

| Name | Narrative meaning | Trigger | System cause | Loop | Duration | Priority | Interruptible | Cooldown | Frequency | Entry | Exit |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Excited | Bouncing with excitement. | Something fun | Mind: mood | One-shot | 1.6 s | 25 | Yes | 15 s | Contextual | Blend | Blend |
| Suspicious | Squints suspiciously. | Odd behaviour of the cursor | Mind | One-shot | 1.8 s | 20 | Yes | 25 s | Contextual | Blend | Blend |
| Scared | Cowers, arms over its hood. | Very rough treatment | Mind: stress | One-shot | 1.5 s | 40 | Yes | 10 s | Contextual | Blend | Blend |
| Embarrassed | Hides its face in its sleeves. | Caught doing something silly | Mind | One-shot | 1.6 s | 25 | Yes | 30 s | Contextual | Blend | Blend |
| Sad | Sad, head down. | Low mood | Mind: mood | One-shot | 2.2 s | 20 | Yes | 60 s | Contextual | Blend | Blend |
| Angry | Angry, fists down, trembling. | Stress peaks | Mind: stress | One-shot | 1.6 s | 30 | Yes | 30 s | Contextual | Blend | Blend |

## Sleep/AFK

| Name | Narrative meaning | Trigger | System cause | Loop | Duration | Priority | Interruptible | Cooldown | Frequency | Entry | Exit |
|---|---|---|---|---|---|---|---|---|---|---|---|
| SleepStart | Getting drowsy, eyes closing. | Very low energy, long user inactivity, PC sleep | User inactivity / drive | One-shot | 1.2 s | 15 | Yes | — | Contextual | SitIdle | SleepLoop |
| SleepLoop | Asleep. The world is quiet. | After SleepStart | User inactivity / drive | Loop | loop | 12 | Yes | — | Contextual | SleepStart | WakeUp |
| WakeUp | Wakes up, small stretch. | User returns, click, drive | User input | One-shot | 1.1 s | 30 | No | — | Contextual | SleepLoop | SitIdle / StandUp |
| SleepLying | Sleeps lying down, curled up. | Asleep | Mind: sleepiness / AFK | Loop | loop | 12 | Yes | — | Contextual | Blend | Blend |
| DreamTwitch | Twitches in its sleep - dreaming. | Asleep for a while | Sleep | One-shot | 0.8 s | 13 | Yes | 20 s | Rare | Blend | Blend |
| WakeFromLying | Wakes gently, sits up and stretches. | User returns / rested | Mind | One-shot | 1.4 s | 30 | No | — | Contextual | Blend | Blend |
| WakeStartled | Wakes with a start and jumps up. | Poked or grabbed while asleep | Direct input | One-shot | 0.8 s | 40 | No | — | Contextual | Blend | Blend |

## Inventory

| Name | Narrative meaning | Trigger | System cause | Loop | Duration | Priority | Interruptible | Cooldown | Frequency | Entry | Exit |
|---|---|---|---|---|---|---|---|---|---|---|---|
| NoticeItem | Something is coming towards it! | Drag enters Hoodie | OLE drag-enter | One-shot | 0.35 s | 60 | Yes | — | Contextual | Look up | CatchItem |
| CatchItem | Catches the object the user gives it. | Drop on Hoodie | OLE drop | One-shot | 0.3 s | 60 | No | — | Contextual | Arms up | InspectItem |
| InspectItem | Looks at the new object. | After catch | Inventory add | One-shot | 0.3 s | 60 | No | — | Contextual | Holding item | PutInBackpack |
| PutInBackpack | Puts it into its backpack for safe keeping. | After inspect | Inventory stored | One-shot | 0.6 s | 60 | No | — | Contextual | Item to pocket | Idle (sparkle) |
| OpenBackpack | Swings its backpack round and opens it. | Backpack panel opens | User opened Backpack | One-shot | 0.5 s | 55 | Yes | — | Contextual | Hands to pocket | SearchBackpack |
| SearchBackpack | Rummages in the open backpack. | Backpack open / idle inventory check | Backpack UI / drive | Loop | loop | 50 | Yes | — | Contextual | OpenBackpack | PresentItem / Idle |
| PresentItem | Takes the requested object out of the backpack and hands it over. | User opens a Backpack item | Shell launch | One-shot | 0.9 s | 60 | No | — | Contextual | Item from pocket | Idle |
| MissingItem | Searches and shrugs: the object is gone. | Stored target no longer exists | File missing | One-shot | 1.3 s | 60 | No | — | Contextual | Search | Shrug |
| CloseBackpack | Closes the backpack and swings it away. | Backpack panel closed / item stored | Backpack UI | One-shot | 0.35 s | 55 | No | — | Contextual | Backpack open | Idle |
| BackpackHeavy | The backpack is heavy - hauls it round with effort. | Many items in the backpack | Inventory | One-shot | 1.6 s | 55 | Yes | 60 s | Contextual | Blend | Blend |
| TearPage | Tears out the page, crumples it and tosses it away. | Note abandoned / cleared | Notes | One-shot | 1.7 s | 60 | No | — | Contextual | Blend | Blend |
| ShowItem | Holds up one of its things and shows it to you. | Found / unlocked an item, about to use it | Progression / intent | One-shot | 1.3 s | 35 | Yes | — | Contextual | Blend | Blend |
| FanSelf | Sits and fans itself (and the hot computer) with a paper fan. | CPU/GPU busy for a while | Perception: PC load | Loop | loop | 25 | Yes | — | Contextual | Sit + fetch fan | Put the fan away |
| SipMug | Sips from a warm mug, looking over at you. | You've worked a long time without a break | Perception: work session | Loop | loop | 25 | Yes | — | Contextual | Fetch mug | Wave |
| PlayBall | Kicks its ball about and chases it. | Bored and playful | Intent: play | Loop | loop | 20 | Yes | — | Contextual | Fetch ball | Pick it up |

## Environment

| Name | Narrative meaning | Trigger | System cause | Loop | Duration | Priority | Interruptible | Cooldown | Frequency | Entry | Exit |
|---|---|---|---|---|---|---|---|---|---|---|---|
| CheckTime | Looks at its wrist as if checking the time. | Timer running / on the hour | Clock | One-shot | 1.6 s | 15 | Yes | 120 s | Contextual | Blend | Blend |
| Shiver | Shivers - it is late and chilly. | Night hours | Clock | One-shot | 1.4 s | 15 | Yes | 300 s | Contextual | Blend | Blend |

## Boundaries

| Name | Narrative meaning | Trigger | System cause | Loop | Duration | Priority | Interruptible | Cooldown | Frequency | Entry | Exit |
|---|---|---|---|---|---|---|---|---|---|---|---|
| BoundaryBump | Bumps into an invisible wall, understands, turns back. | Walking into a Never-enter area | Territory | One-shot | 1.1 s | 45 | No | 5 s | Contextual | Blend | Blend |

