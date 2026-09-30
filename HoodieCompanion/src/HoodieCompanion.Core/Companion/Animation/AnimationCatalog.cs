namespace HoodieCompanion.Companion.Animation;

/// <summary>Semantic metadata for a clip. Mirrors ANIMATION_CATALOG.md (generated from this table).</summary>
public sealed record ClipInfo(
    AnimClip Clip,
    string Category,
    string NarrativeMeaning,
    string Trigger,
    string SystemCause,
    bool Loop,
    double Duration,
    int Priority,
    bool Interruptible,
    double Cooldown,
    string Entry,
    string Exit,
    double BlendIn = 0.2,
    bool AllowLook = true,
    bool AllowBlink = true,
    bool AllowBreath = true,
    string Rarity = "Contextual");

public static class AnimationCatalog
{
    public const int PhysicalPriority = 100;

    private static readonly Dictionary<AnimClip, ClipInfo> Table = Build().Concat(BuildLibrary())
        .Select(c => c with { Category = StateOf(c.Clip), Rarity = RarityOf(c.Clip) })
        .ToDictionary(c => c.Clip);

    /// <summary>Behaviour state group each clip belongs to (the animation state machine's top level).</summary>
    public static string StateOf(AnimClip c) => c switch
    {
        AnimClip.IdleBreathing or AnimClip.IdleBreathing2 or AnimClip.Blink or AnimClip.LookLeft or AnimClip.LookRight or AnimClip.LookUp
            or AnimClip.LookDown or AnimClip.HeadTilt or AnimClip.WeightShift or AnimClip.Stretch or AnimClip.StretchBody
            or AnimClip.Scratch or AnimClip.Yawn or AnimClip.InspectSelf => "Idle basic",
        AnimClip.LookCursor or AnimClip.Curious or AnimClip.Listen or AnimClip.NoticeMovement or AnimClip.PeekEdge or AnimClip.WatchWindow => "Idle curious",
        AnimClip.SitDown or AnimClip.SitIdle or AnimClip.StandUp or AnimClip.SitEdge or AnimClip.ChinRest or AnimClip.PickSurface
            or AnimClip.Sigh or AnimClip.CountFingers or AnimClip.StareVoid or AnimClip.LieDown or AnimClip.LieIdle => "Idle bored",
        AnimClip.Dance or AnimClip.JumpForJoy or AnimClip.Spin or AnimClip.CatchCursor => "Idle playful",
        AnimClip.Walk or AnimClip.Run or AnimClip.FollowCursor or AnimClip.Stop or AnimClip.Turn or AnimClip.Jump or AnimClip.Airborne
            or AnimClip.Fall or AnimClip.LandSoft or AnimClip.LandHard or AnimClip.Recover or AnimClip.Slip or AnimClip.Balance => "Movement",
        AnimClip.JumpMonitor or AnimClip.LandMonitor or AnimClip.LeaveScreen or AnimClip.ReturnToScreen or AnimClip.PeekIn
            or AnimClip.PlaceLadder or AnimClip.ClimbLadder or AnimClip.TieRope or AnimClip.ClimbRope or AnimClip.HangEdge or AnimClip.ClimbEdge => "Screen traversal",
        AnimClip.Wave or AnimClip.Surprised or AnimClip.ReachCursor or AnimClip.Dodge or AnimClip.Annoyed or AnimClip.Happy or AnimClip.SearchCursor => "Cursor interaction",
        AnimClip.GrabReaction or AnimClip.Grabbed or AnimClip.HangHandL or AnimClip.HangHandR or AnimClip.HangFoot or AnimClip.HangTorso
            or AnimClip.Swinging or AnimClip.Struggle or AnimClip.RelaxedCarry or AnimClip.Thrown or AnimClip.RecoverFromThrow or AnimClip.Dizzy => "Drag interaction",
        AnimClip.WriteNotes or AnimClip.Thinking or AnimClip.ReadBook or AnimClip.LaptopOpen or AnimClip.LaptopType or AnimClip.LaptopClose or AnimClip.CheckResult => "Work states",
        AnimClip.PCBusy or AnimClip.CarryLoad or AnimClip.PCIdle => "PC load",
        AnimClip.DownloadWatching or AnimClip.CatchPackage => "Download/process",
        AnimClip.ReminderAlert or AnimClip.TimerAlert or AnimClip.Knock or AnimClip.Point => "Notification",
        AnimClip.Success or AnimClip.ThumbsUp or AnimClip.Proud => "Success",
        AnimClip.Error or AnimClip.Confused or AnimClip.Frustrated or AnimClip.Facepalm => "Failure",
        AnimClip.Excited or AnimClip.Suspicious or AnimClip.Scared or AnimClip.Embarrassed or AnimClip.Sad or AnimClip.Angry => "Emotions",
        AnimClip.SleepStart or AnimClip.SleepLoop or AnimClip.WakeUp or AnimClip.SleepLying or AnimClip.DreamTwitch
            or AnimClip.WakeFromLying or AnimClip.WakeStartled => "Sleep/AFK",
        AnimClip.NoticeItem or AnimClip.CatchItem or AnimClip.InspectItem or AnimClip.PutInBackpack or AnimClip.OpenBackpack
            or AnimClip.SearchBackpack or AnimClip.CloseBackpack or AnimClip.PresentItem or AnimClip.MissingItem
            or AnimClip.BackpackHeavy or AnimClip.TearPage => "Inventory",
        AnimClip.CheckTime or AnimClip.Shiver => "Environment",
        AnimClip.ShowItem or AnimClip.FanSelf or AnimClip.SipMug or AnimClip.PlayBall => "Inventory",
        AnimClip.BoundaryBump => "Boundaries",
        _ => "Other",
    };

    /// <summary>
    /// How often an idle clip may appear: Frequent (seconds), Occasional (tens of seconds), Rare (minutes),
    /// Contextual (only in response to an event or activity).
    /// </summary>
    /// <summary>Idle-director tier (matches IdleDirector's lists); everything else only plays on events.</summary>
    public static string RarityOf(AnimClip c) => c switch
    {
        AnimClip.Blink or AnimClip.IdleBreathing or AnimClip.IdleBreathing2 or AnimClip.LookCursor or AnimClip.LookLeft or AnimClip.LookRight
            or AnimClip.LookUp or AnimClip.LookDown or AnimClip.HeadTilt or AnimClip.WeightShift => "Frequent",
        AnimClip.Scratch or AnimClip.StretchBody or AnimClip.InspectSelf or AnimClip.Listen or AnimClip.Sigh or AnimClip.StareVoid
            or AnimClip.Yawn or AnimClip.Stretch or AnimClip.WatchWindow or AnimClip.ChinRest or AnimClip.PickSurface => "Occasional",
        AnimClip.Spin or AnimClip.Dance or AnimClip.CatchCursor or AnimClip.Balance or AnimClip.CountFingers or AnimClip.Proud
            or AnimClip.PeekIn or AnimClip.DreamTwitch => "Rare",
        _ => "Contextual",
    };

    public static ClipInfo Get(AnimClip clip) => Table[clip];

    public static IReadOnlyCollection<ClipInfo> All => Table.Values;

    public static readonly string[] StateOrder =
    {
        "Idle basic", "Idle curious", "Idle bored", "Idle playful", "Movement", "Screen traversal", "Cursor interaction",
        "Drag interaction", "Work states", "PC load", "Download/process", "Notification", "Success", "Failure", "Emotions",
        "Sleep/AFK", "Inventory", "Environment", "Boundaries", "Other",
    };

    private static IEnumerable<ClipInfo> Build()
    {
        // ---------------- MOVEMENT ----------------
        yield return new(AnimClip.IdleBreathing, "Movement", "Hoodie simply exists: breathing, small weight shifts.", "Nothing else to do", "None", true, 0, 10, true, 0, "Any settled pose", "Any clip");
        yield return new(AnimClip.Blink, "Movement", "Alive and awake.", "Random 2.5-6 s timer (overlay)", "None", false, 0.14, 0, true, 2.5, "Overlay on any awake clip", "Eyes reopen");
        yield return new(AnimClip.Walk, "Movement", "Going somewhere on its own feet.", "Walk target chosen / user command", "Behavior target", true, 0, 40, true, 0, "Lean forward, first step", "Slow, settle", 0.18);
        yield return new(AnimClip.Run, "Movement", "Hurrying: playing or answering a call.", "Play mode chase, Come here from far away", "User command / Play mode", true, 0, 40, true, 0, "Lean, quicker steps", "Decelerate to walk/idle", 0.15);
        yield return new(AnimClip.Turn, "Movement", "Changing its mind about direction.", "New target behind Hoodie", "Behavior target", false, 0.26, 40, false, 0, "Squash narrow", "Face new direction", 0.08);
        yield return new(AnimClip.Jump, "Movement", "Crouch before leaving the ground.", "Monitor above/beside is higher, Play hop", "Monitor geometry", false, 0.2, 60, false, 0, "Crouch", "Airborne", 0.08);
        yield return new(AnimClip.Airborne, "Movement", "In the air, going up.", "Physics: not on ground, moving up", "Physics", true, 0, PhysicalPriority, false, 0, "Takeoff/throw", "Fall", 0.1, AllowBreath: false);
        yield return new(AnimClip.Fall, "Movement", "Falling down: arms up, eyes wide.", "Physics: not on ground, moving down", "Physics", true, 0, PhysicalPriority, false, 0, "Apex", "Landing", 0.15, AllowBreath: false);
        yield return new(AnimClip.LandSoft, "Movement", "Touches down comfortably.", "Landing impact below threshold", "Physics", false, 0.36, 80, false, 0, "Ground contact squash", "Idle", 0.05);
        yield return new(AnimClip.LandHard, "Movement", "Oof - a heavy landing, but no harm done.", "Landing impact above threshold", "Physics (strong throw)", false, 0.8, 80, false, 0, "Deep squash + dust", "Recover", 0.04, AllowLook: false);
        yield return new(AnimClip.Recover, "Movement", "Regains balance.", "After LandHard", "Physics", false, 0.6, 70, false, 0, "Stand up", "Idle / RecoverFromThrow", 0.1);
        // ---------------- REST ----------------
        yield return new(AnimClip.SitDown, "Rest", "Decides to rest here for a while.", "Low energy, Quiet/Focus mode, Company mode near cursor", "Internal drive / presence mode", false, 0.55, 20, false, 0, "Slow, bend", "SitIdle", 0.2);
        yield return new(AnimClip.SitIdle, "Rest", "Resting, watching the world.", "After SitDown", "Internal drive", true, 0, 12, true, 0, "SitDown", "StandUp / SleepStart");
        yield return new(AnimClip.StandUp, "Rest", "Done resting.", "Any activity that needs standing", "Behavior", false, 0.45, 30, false, 0, "SitIdle", "Idle", 0.12);
        yield return new(AnimClip.SleepStart, "Rest", "Getting drowsy, eyes closing.", "Very low energy, long user inactivity, PC sleep", "User inactivity / drive", false, 1.2, 15, true, 0, "SitIdle", "SleepLoop", 0.3);
        yield return new(AnimClip.SleepLoop, "Rest", "Asleep. The world is quiet.", "After SleepStart", "User inactivity / drive", true, 0, 12, true, 0, "SleepStart", "WakeUp", 0.3, AllowLook: false, AllowBlink: false, AllowBreath: false);
        yield return new(AnimClip.WakeUp, "Rest", "Wakes up, small stretch.", "User returns, click, drive", "User input", false, 1.1, 30, false, 0, "SleepLoop", "SitIdle / StandUp", 0.2, AllowBlink: false);
        yield return new(AnimClip.Stretch, "Rest", "Stretches after staying still.", "Long idle", "Internal drive", false, 1.3, 20, true, 45, "Idle", "Idle");
        yield return new(AnimClip.Yawn, "Rest", "Getting a bit sleepy.", "Low energy", "Internal drive", false, 1.4, 20, true, 60, "Idle", "Idle", AllowBlink: false);
        // ---------------- CURSOR ----------------
        yield return new(AnimClip.LookCursor, "Cursor", "Notices the user's hand nearby.", "Cursor within look radius (overlay)", "Cursor position", true, 0, 0, true, 0, "Overlay", "Overlay fades");
        yield return new(AnimClip.FollowCursor, "Cursor", "Chasing the user's hand for fun.", "Play mode, cursor near the ground", "Cursor position", true, 0, 40, true, 0, "Run", "Idle", 0.15);
        yield return new(AnimClip.Curious, "Cursor", "What is that hand doing here?", "Cursor hovers close for >1.5 s", "Cursor position", false, 1.3, 20, true, 12, "Head tilt", "Idle");
        yield return new(AnimClip.Wave, "Cursor", "Friendly hello.", "Left click, greeting, goodbye", "User input", false, 0.9, 25, true, 2, "Raise arm", "Lower arm");
        yield return new(AnimClip.Surprised, "Cursor", "Startled by a fast approaching hand.", "Cursor rushes towards Hoodie", "Cursor velocity", false, 0.7, 30, true, 15, "Small hop", "Idle", 0.06);
        // ---------------- PHYSICAL ----------------
        yield return new(AnimClip.Grabbed, "Physical", "Being held by the hood: hood first, body follows, legs dangle.", "User drags Hoodie", "Direct manipulation", true, 0, PhysicalPriority, false, 0, "GrabReaction", "Thrown / Fall", 0.08, AllowBreath: false);
        yield return new(AnimClip.GrabReaction, "Physical", "Whoa - picked up!", "Drag begins", "Direct manipulation", false, 0.3, PhysicalPriority, false, 0, "Any", "Grabbed", 0.05);
        yield return new(AnimClip.Swinging, "Physical", "Swinging while held: limbs follow momentum.", "Held and cursor moves fast", "Direct manipulation", true, 0, PhysicalPriority, false, 0, "Grabbed", "Grabbed / Thrown", 0.1, AllowBreath: false);
        yield return new(AnimClip.Thrown, "Physical", "Flying through its world.", "Released with velocity", "Direct manipulation + physics", true, 0, PhysicalPriority, false, 0, "Release", "Fall / Landing", 0.06, AllowBreath: false);
        yield return new(AnimClip.RecoverFromThrow, "Physical", "Dusts itself off and looks at the user - no hard feelings.", "After a hard landing", "Physics", false, 1.4, 70, false, 0, "LandHard / Recover", "Idle", 0.15);
        // ---------------- INVENTORY ----------------
        yield return new(AnimClip.NoticeItem, "Inventory", "Something is coming towards it!", "Drag enters Hoodie", "OLE drag-enter", false, 0.35, 60, true, 0, "Look up", "CatchItem", 0.08);
        yield return new(AnimClip.CatchItem, "Inventory", "Catches the object the user gives it.", "Drop on Hoodie", "OLE drop", false, 0.3, 60, false, 0, "Arms up", "InspectItem", 0.06);
        yield return new(AnimClip.InspectItem, "Inventory", "Looks at the new object.", "After catch", "Inventory add", false, 0.3, 60, false, 0, "Holding item", "PutInBackpack");
        yield return new(AnimClip.PutInBackpack, "Inventory", "Puts it into its backpack for safe keeping.", "After inspect", "Inventory stored", false, 0.6, 60, false, 0, "Item to pocket", "Idle (sparkle)");
        yield return new(AnimClip.OpenBackpack, "Inventory", "Swings its backpack round and opens it.", "Backpack panel opens", "User opened Backpack", false, 0.5, 55, true, 0, "Hands to pocket", "SearchBackpack");
        yield return new(AnimClip.SearchBackpack, "Inventory", "Rummages in the open backpack.", "Backpack open / idle inventory check", "Backpack UI / drive", true, 0, 50, true, 0, "OpenBackpack", "PresentItem / Idle");
        yield return new(AnimClip.PresentItem, "Inventory", "Takes the requested object out of the backpack and hands it over.", "User opens a Backpack item", "Shell launch", false, 0.9, 60, false, 0, "Item from pocket", "Idle");
        yield return new(AnimClip.MissingItem, "Inventory", "Searches and shrugs: the object is gone.", "Stored target no longer exists", "File missing", false, 1.3, 60, false, 0, "Search", "Shrug");
        // ---------------- UTILITY ----------------
        yield return new(AnimClip.ReminderAlert, "Utility", "Holds up the note it promised to remember.", "Reminder due", "Reminder service", true, 0, 80, false, 0, "Raise note", "Done/Snooze", 0.15);
        yield return new(AnimClip.TimerAlert, "Utility", "The clock it watched has run out - hops to tell you.", "Timer finished", "Timer service", true, 0, 80, false, 0, "Hop", "Acknowledge", 0.15);
        yield return new(AnimClip.Thinking, "Utility", "Considering / remembering.", "Note being written", "Notes", false, 1.0, 25, true, 0, "Hand to chin", "Idle");
        yield return new(AnimClip.Success, "Utility", "Got it!", "Item stored, note saved, reminder set", "Utility action", false, 0.6, 25, true, 0, "Small hop", "Idle", 0.08);
        yield return new(AnimClip.Error, "Utility", "That did not work.", "Launch failed", "Utility action", false, 0.8, 25, true, 0, "Head shake", "Idle");
        // ---------------- SYSTEM ----------------
        yield return new(AnimClip.PCBusy, "System", "The world is working hard and getting warm: fans itself.", "CPU > 85% sustained", "System monitor", false, 2.6, 20, true, 600, "Wipe brow", "Idle");
        yield return new(AnimClip.PCIdle, "System", "Everything calmed down; relaxes.", "Load drops after busy", "System monitor", false, 1.4, 15, true, 300, "Exhale", "Idle");
        yield return new(AnimClip.DownloadWatching, "System", "Watches something big arriving.", "Network download > 2 MB/s sustained", "System monitor", false, 3.0, 15, true, 600, "Look up", "Idle");
        // ---------------- WORLD ----------------
        yield return new(AnimClip.PeekEdge, "World", "Curiously peeks over the edge of its world.", "Reaches a monitor edge", "Monitor geometry", false, 2.0, 20, true, 30, "Lean", "Turn back");
        yield return new(AnimClip.JumpMonitor, "World", "Leaps to another part of its world.", "Target on a higher monitor", "Monitor geometry", false, 0.22, 60, false, 0, "Crouch", "Airborne", 0.08);
        yield return new(AnimClip.LandMonitor, "World", "Arrives on another monitor.", "Landing after monitor jump", "Monitor geometry", false, 0.36, 80, false, 0, "Squash", "Idle", 0.05);
        yield return new(AnimClip.LeaveScreen, "World", "Politely leaves: little wave, walks out.", "Leave me alone / fullscreen app", "Presence mode", false, 0.9, 70, false, 0, "Wave", "Walk off-screen", 0.15);
        yield return new(AnimClip.ReturnToScreen, "World", "Comes back into view.", "Come back", "Presence mode", false, 0.9, 70, false, 0, "Walk in", "Wave / Idle", 0.15);
        yield return new(AnimClip.PlaceLadder, "World", "Pulls out a ladder and props it against the higher part of its world.", "Target monitor above / higher neighbour", "Monitor geometry", false, 0.8, 60, false, 0, "Reach up", "ClimbLadder", 0.12);
        yield return new(AnimClip.ClimbLadder, "World", "Climbs rung by rung.", "On the ladder", "Monitor geometry", true, 0, 60, false, 0, "PlaceLadder", "Step off at the top", 0.1, AllowBreath: false);
        yield return new(AnimClip.TieRope, "World", "Ties a rope at the edge and lets it unroll downwards.", "Target monitor below / lower neighbour", "Monitor geometry", false, 0.7, 60, false, 0, "Crouch at edge", "ClimbRope", 0.12);
        yield return new(AnimClip.ClimbRope, "World", "Slides down the rope hand over hand.", "On the rope", "Monitor geometry", true, 0, 60, false, 0, "TieRope", "Land at the bottom", 0.1, AllowBreath: false);
        // ---------------- ACCESSORIES ----------------
        yield return new(AnimClip.CloseBackpack, "Accessories", "Closes the backpack and swings it away.", "Backpack panel closed / item stored", "Backpack UI", false, 0.35, 55, false, 0, "Backpack open", "Idle", 0.12);
        yield return new(AnimClip.LaptopOpen, "Accessories", "Sits down, pulls out a laptop and opens it.", "PC Status opened / idle activity", "PC Status UI / drive", false, 0.7, 45, false, 0, "Sitting", "LaptopType", 0.15);
        yield return new(AnimClip.LaptopType, "Accessories", "Types away, checking how the computer is doing.", "PC Status open / idle activity", "PC Status UI / drive", true, 0, 40, true, 0, "LaptopOpen", "LaptopClose");
        yield return new(AnimClip.LaptopClose, "Accessories", "Closes the laptop and tucks it away.", "PC Status closed / activity over", "PC Status UI / drive", false, 0.55, 45, false, 0, "LaptopType", "Sitting", 0.12);
        yield return new(AnimClip.ReadBook, "Accessories", "Reads a book, turning a page now and then.", "Quiet idle activity", "Internal drive", true, 0, 20, true, 0, "Sitting", "Sitting", 0.25);
        yield return new(AnimClip.WriteNotes, "Accessories", "Writes in a little notebook.", "Notes / Reminder opened, idle activity", "Notes UI / drive", true, 0, 40, true, 0, "Pull out notebook", "Put it away", 0.18);
        yield return new(AnimClip.Dance, "Accessories", "A happy little dance.", "Play mode / good mood", "Internal drive", false, 2.4, 20, true, 25, "Bounce", "Idle", 0.12);
        yield return new(AnimClip.JumpForJoy, "Accessories", "Jumps for joy.", "Success, greeting in Play mode", "Utility / drive", false, 0.8, 25, true, 8, "Crouch", "Idle", 0.08);
    }

    private static IEnumerable<ClipInfo> BuildLibrary()
    {
        ClipInfo C(AnimClip c, string meaning, string trigger, string cause, bool loop, double dur, int prio, bool intr, double cd,
            string entry = "Blend", string exit = "Blend", double blend = 0.2, bool look = true, bool blink = true, bool breath = true) =>
            new(c, "", meaning, trigger, cause, loop, dur, prio, intr, cd, entry, exit, blend, look, blink, breath);

        // Idle basic
        yield return C(AnimClip.IdleBreathing2, "Relaxed stance, weight on one leg.", "Idle base loop variant", "Idle director", true, 0, 10, true, 0);
        yield return C(AnimClip.LookLeft, "Glances to one side.", "Idle gesture", "Idle director", false, 1.6, 12, true, 6);
        yield return C(AnimClip.LookRight, "Glances to the other side.", "Idle gesture", "Idle director", false, 1.6, 12, true, 6);
        yield return C(AnimClip.LookUp, "Looks up at the screen above.", "Idle gesture", "Idle director", false, 1.8, 12, true, 8);
        yield return C(AnimClip.LookDown, "Looks at its feet.", "Idle gesture", "Idle director", false, 1.6, 12, true, 8);
        yield return C(AnimClip.HeadTilt, "Tilts its head, thinking about something.", "Idle gesture", "Idle director", false, 1.8, 12, true, 8);
        yield return C(AnimClip.WeightShift, "Shifts its weight from foot to foot.", "Idle gesture", "Idle director", false, 1.6, 12, true, 6);
        yield return C(AnimClip.StretchBody, "Leans back and stretches the whole body.", "Rare idle", "Idle director / morning", false, 1.5, 20, true, 60);
        yield return C(AnimClip.Scratch, "Scratches its hood.", "Rare idle", "Idle director", false, 1.4, 20, true, 50);
        yield return C(AnimClip.InspectSelf, "Looks at its hands and sleeves.", "Rare idle", "Idle director", false, 2.0, 20, true, 70);
        // Idle curious
        yield return C(AnimClip.Listen, "Listens to something only it can hear.", "Idle gesture", "Idle director", false, 2.0, 15, true, 30);
        yield return C(AnimClip.NoticeMovement, "Something moved! A quick glance.", "Window moved / big cursor move", "Desktop events", false, 0.8, 18, true, 10, blend: 0.08);
        yield return C(AnimClip.WatchWindow, "Watches you work on the active window.", "User typing / Company mode", "User activity", false, 3.0, 15, true, 20);
        // Idle bored
        yield return C(AnimClip.SitEdge, "Sits on an edge and swings its legs.", "Bored / at an edge", "Idle director", true, 0, 12, true, 0, "Sit on edge", "Hop down", 0.3);
        yield return C(AnimClip.ChinRest, "Rests its chin on a hand, bored.", "Sitting + boredom", "Mind: boredom", false, 3.5, 12, true, 20);
        yield return C(AnimClip.PickSurface, "Pokes at the floor.", "Sitting + boredom", "Mind: boredom", false, 2.6, 12, true, 25);
        yield return C(AnimClip.Sigh, "A long sigh.", "Boredom", "Mind: boredom", false, 1.6, 15, true, 60);
        yield return C(AnimClip.CountFingers, "Counts something on its fingers.", "Boredom", "Mind: boredom", false, 2.4, 15, true, 80);
        yield return C(AnimClip.StareVoid, "Stares into nothing.", "Very bored / AFK", "Mind: boredom", false, 4.0, 12, true, 40);
        yield return C(AnimClip.LieDown, "Lies down on the floor.", "Sleepy / very bored", "Mind: sleepiness", false, 1.1, 15, false, 0, "Sit or stand", "LieIdle / SleepLying", 0.25);
        yield return C(AnimClip.LieIdle, "Lies on its back, looking at the sky, kicking a foot.", "Bored, lying", "Mind: boredom", true, 0, 12, true, 0, blend: 0.3);
        // Idle playful
        yield return C(AnimClip.Spin, "Twirls around once.", "Playful", "Mind: playfulness", false, 0.9, 20, true, 25, blend: 0.08);
        yield return C(AnimClip.CatchCursor, "Jumps to swipe at your cursor.", "Play mode, cursor close above", "Cursor", false, 0.9, 25, true, 6, blend: 0.08);
        // Movement
        yield return C(AnimClip.Stop, "Stops with a little skid and overshoot.", "End of a fast walk/run", "Behaviour", false, 0.35, 35, true, 0, blend: 0.08);
        yield return C(AnimClip.Slip, "Slips, flails, recovers.", "Rare while running", "Physics flavour", false, 1.0, 45, false, 90, blend: 0.06);
        yield return C(AnimClip.Balance, "Balances on a narrow edge.", "At a platform edge", "Platforms", false, 1.8, 20, true, 30);
        // Screen traversal
        yield return C(AnimClip.PeekIn, "Peeks in from the edge of the screen before coming in.", "Come back", "Presence", false, 1.2, 70, false, 0, blend: 0.1);
        yield return C(AnimClip.HangEdge, "Hangs from an edge by its hands, legs kicking.", "Dropped below the taskbar edge", "Physics", true, 0, 90, false, 0, blend: 0.08, breath: false);
        yield return C(AnimClip.ClimbEdge, "Pulls itself up over the edge.", "After HangEdge", "Physics", false, 0.9, 90, false, 0, blend: 0.1, breath: false);
        // Cursor interaction
        yield return C(AnimClip.ReachCursor, "Stands on tiptoes and reaches for your cursor.", "Cursor just above", "Cursor", false, 1.4, 22, true, 15);
        yield return C(AnimClip.Dodge, "Dodges out of the way.", "Cursor rushes at it (Quiet/Focus: instead of Surprised)", "Cursor", false, 0.6, 30, true, 8, blend: 0.06);
        yield return C(AnimClip.Annoyed, "Crosses its arms and taps its foot - enough poking.", "Poked / clicked many times", "Mind: stress", false, 1.8, 30, true, 20);
        yield return C(AnimClip.Happy, "Happy wiggle - it likes the attention.", "Gentle attention", "Mind: affection", false, 1.2, 25, true, 10);
        yield return C(AnimClip.SearchCursor, "Looks around for the cursor it lost.", "Cursor disappeared far away", "Cursor", false, 2.0, 15, true, 30);
        // Drag interaction
        yield return C(AnimClip.HangHandL, "Held by the hand: the arm reaches up, the body hangs from it.", "Grabbed by a hand", "Direct manipulation", true, 0, 100, false, 0, blend: 0.1, breath: false);
        yield return C(AnimClip.HangHandR, "Held by the other hand.", "Grabbed by a hand", "Direct manipulation", true, 0, 100, false, 0, blend: 0.1, breath: false);
        yield return C(AnimClip.HangFoot, "Held by a foot: upside down, arms and hood dangling.", "Grabbed by a leg", "Direct manipulation", true, 0, 100, false, 0, blend: 0.1, breath: false);
        yield return C(AnimClip.HangTorso, "Held by the body: arms and legs dangle.", "Grabbed by the torso", "Direct manipulation", true, 0, 100, false, 0, blend: 0.1, breath: false);
        yield return C(AnimClip.Struggle, "Wriggles to get free.", "Held for long / swung hard", "Mind: stress", true, 0, 100, false, 0, blend: 0.1, breath: false);
        yield return C(AnimClip.RelaxedCarry, "Relaxes and lets itself be carried.", "Held gently for a while", "Mind: affection", true, 0, 100, false, 0, blend: 0.3, breath: false);
        yield return C(AnimClip.Dizzy, "Dizzy after a wild ride: wobbles, stars circle its head.", "Landing after heavy swinging", "Physics + mind", false, 2.2, 75, false, 0, blend: 0.1);
        // Work
        yield return C(AnimClip.CheckResult, "Nods at the result.", "Work finished", "Utility", false, 1.0, 25, true, 5);
        // PC load
        yield return C(AnimClip.CarryLoad, "Hauls a heavy crate: the computer is doing heavy lifting.", "CPU busy for a while", "System monitor", true, 0, 25, true, 0, "Lift crate", "Put down", 0.2);
        // Download
        yield return C(AnimClip.CatchPackage, "Catches a parcel of data falling from above.", "Download in progress", "System monitor (network)", false, 0.9, 25, true, 4, blend: 0.1);
        // Notification
        yield return C(AnimClip.Knock, "Knocks on the glass to get your attention.", "Reminder / timer not acknowledged", "Reminders", false, 1.2, 80, true, 6);
        yield return C(AnimClip.Point, "Points at something.", "Show where something is", "Utility", false, 1.4, 25, true, 5);
        // Success
        yield return C(AnimClip.ThumbsUp, "Raises a fist: well done!", "Something completed", "Utility", false, 1.1, 25, true, 5);
        yield return C(AnimClip.Proud, "Puffs up, proud of itself.", "Task done / praised", "Mind: mood", false, 1.6, 25, true, 20);
        // Failure
        yield return C(AnimClip.Confused, "Confused: head tilts, scratches its hood.", "Unexpected result", "Utility", false, 1.8, 25, true, 8);
        yield return C(AnimClip.Frustrated, "Stomps its foot.", "Repeated failure", "Mind: stress", false, 1.3, 25, true, 20);
        yield return C(AnimClip.Facepalm, "Facepalm.", "Silly mistake", "Utility", false, 1.5, 25, true, 20);
        // Emotions
        yield return C(AnimClip.Excited, "Bouncing with excitement.", "Something fun", "Mind: mood", false, 1.6, 25, true, 15);
        yield return C(AnimClip.Suspicious, "Squints suspiciously.", "Odd behaviour of the cursor", "Mind", false, 1.8, 20, true, 25);
        yield return C(AnimClip.Scared, "Cowers, arms over its hood.", "Very rough treatment", "Mind: stress", false, 1.5, 40, true, 10, blend: 0.08);
        yield return C(AnimClip.Embarrassed, "Hides its face in its sleeves.", "Caught doing something silly", "Mind", false, 1.6, 25, true, 30);
        yield return C(AnimClip.Sad, "Sad, head down.", "Low mood", "Mind: mood", false, 2.2, 20, true, 60);
        yield return C(AnimClip.Angry, "Angry, fists down, trembling.", "Stress peaks", "Mind: stress", false, 1.6, 30, true, 30);
        // Sleep
        yield return C(AnimClip.SleepLying, "Sleeps lying down, curled up.", "Asleep", "Mind: sleepiness / AFK", true, 0, 12, true, 0, blend: 0.4, look: false, blink: false, breath: false);
        yield return C(AnimClip.DreamTwitch, "Twitches in its sleep - dreaming.", "Asleep for a while", "Sleep", false, 0.8, 13, true, 20, look: false, blink: false, breath: false);
        yield return C(AnimClip.WakeFromLying, "Wakes gently, sits up and stretches.", "User returns / rested", "Mind", false, 1.4, 30, false, 0, blend: 0.2, blink: false);
        yield return C(AnimClip.WakeStartled, "Wakes with a start and jumps up.", "Poked or grabbed while asleep", "Direct input", false, 0.8, 40, false, 0, blend: 0.05);
        // Inventory
        yield return C(AnimClip.BackpackHeavy, "The backpack is heavy - hauls it round with effort.", "Many items in the backpack", "Inventory", false, 1.6, 55, true, 60);
        yield return C(AnimClip.TearPage, "Tears out the page, crumples it and tosses it away.", "Note abandoned / cleared", "Notes", false, 1.7, 60, false, 0, blend: 0.1);
        // Environment
        yield return C(AnimClip.CheckTime, "Looks at its wrist as if checking the time.", "Timer running / on the hour", "Clock", false, 1.6, 15, true, 120);
        yield return C(AnimClip.Shiver, "Shivers - it is late and chilly.", "Night hours", "Clock", false, 1.4, 15, true, 300);
        // Boundaries
        yield return C(AnimClip.BoundaryBump, "Bumps into an invisible wall, understands, turns back.", "Walking into a Never-enter area", "Territory", false, 1.1, 45, false, 5, blend: 0.06);

        // World items (used in context, found over time)
        yield return C(AnimClip.ShowItem, "Holds up one of its things and shows it to you.", "Found / unlocked an item, about to use it", "Progression / intent", false, 1.3, 35, true, 0, blend: 0.15);
        yield return C(AnimClip.FanSelf, "Sits and fans itself (and the hot computer) with a paper fan.", "CPU/GPU busy for a while", "Perception: PC load", true, 0, 25, true, 0, "Sit + fetch fan", "Put the fan away", 0.25);
        yield return C(AnimClip.SipMug, "Sips from a warm mug, looking over at you.", "You've worked a long time without a break", "Perception: work session", true, 0, 25, true, 0, "Fetch mug", "Wave", 0.25);
        yield return C(AnimClip.PlayBall, "Kicks its ball about and chases it.", "Bored and playful", "Intent: play", true, 0, 20, true, 0, "Fetch ball", "Pick it up", 0.2);
    }

    /// <summary>Renders the catalog as Markdown (used to produce ANIMATION_CATALOG.md).</summary>
    public static string ToMarkdown()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Hoodie Companion — Animation Catalog");
        sb.AppendLine();
        sb.AppendLine("Generated from `AnimationCatalog.cs` (the table the runtime uses). All clips are procedural poses of the cutout rig");
        sb.AppendLine("in `Assets/rig.json` — no frame-by-frame raster art, so the silhouette, limb count and clothing can never drift.");
        sb.AppendLine();
        sb.AppendLine($"**{All.Count} clips** in {All.Select(c => c.Category).Distinct().Count()} behaviour states. See PLAN.md for the state machine, the event→reaction system and the idle director.");
        sb.AppendLine();
        foreach (var group in All.GroupBy(c => c.Category).OrderBy(g => Array.IndexOf(StateOrder, g.Key)))
        {
            sb.AppendLine($"## {group.Key}");
            sb.AppendLine();
            sb.AppendLine("| Name | Narrative meaning | Trigger | System cause | Loop | Duration | Priority | Interruptible | Cooldown | Frequency | Entry | Exit |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var c in group)
            {
                var dur = c.Loop ? "loop" : $"{c.Duration:0.##} s";
                var cd = c.Cooldown > 0 ? $"{c.Cooldown:0.#} s" : "—";
                sb.AppendLine($"| {c.Clip} | {c.NarrativeMeaning} | {c.Trigger} | {c.SystemCause} | {(c.Loop ? "Loop" : "One-shot")} | {dur} | {c.Priority} | {(c.Interruptible ? "Yes" : "No")} | {cd} | {c.Rarity} | {c.Entry} | {c.Exit} |");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
