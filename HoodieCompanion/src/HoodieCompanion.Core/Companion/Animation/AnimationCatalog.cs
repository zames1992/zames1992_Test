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
    bool AllowBreath = true);

public static class AnimationCatalog
{
    public const int PhysicalPriority = 100;

    private static readonly Dictionary<AnimClip, ClipInfo> Table = Build().ToDictionary(c => c.Clip);

    public static ClipInfo Get(AnimClip clip) => Table[clip];

    public static IReadOnlyCollection<ClipInfo> All => Table.Values;

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

    /// <summary>Renders the catalog as Markdown (used to produce ANIMATION_CATALOG.md).</summary>
    public static string ToMarkdown()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Hoodie Companion — Animation Catalog");
        sb.AppendLine();
        sb.AppendLine("Generated from `AnimationCatalog.cs` (the table the runtime uses). All clips are procedural poses of the cutout rig");
        sb.AppendLine("in `Assets/rig.json` — no frame-by-frame raster art, so the silhouette, limb count and clothing can never drift.");
        sb.AppendLine();
        foreach (var group in All.GroupBy(c => c.Category))
        {
            sb.AppendLine($"## {group.Key}");
            sb.AppendLine();
            sb.AppendLine("| Name | Narrative meaning | Trigger | System cause | Loop | Duration | Priority | Interruptible | Cooldown | Entry | Exit |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var c in group)
            {
                var dur = c.Loop ? "loop" : $"{c.Duration:0.##} s";
                var cd = c.Cooldown > 0 ? $"{c.Cooldown:0.#} s" : "—";
                sb.AppendLine($"| {c.Clip} | {c.NarrativeMeaning} | {c.Trigger} | {c.SystemCause} | {(c.Loop ? "Loop" : "One-shot")} | {dur} | {c.Priority} | {(c.Interruptible ? "Yes" : "No")} | {cd} | {c.Entry} | {c.Exit} |");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
