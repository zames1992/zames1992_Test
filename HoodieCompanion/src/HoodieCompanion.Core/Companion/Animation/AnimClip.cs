namespace HoodieCompanion.Companion.Animation;

/// <summary>Every animation in ANIMATION_CATALOG.md. Names match the catalog exactly.</summary>
public enum AnimClip
{
    // Movement
    IdleBreathing, Blink, Walk, Run, Turn, Jump, Airborne, Fall, LandSoft, LandHard, Recover,
    // Rest
    SitDown, SitIdle, StandUp, SleepStart, SleepLoop, WakeUp, Stretch, Yawn,
    // Cursor
    LookCursor, FollowCursor, Curious, Wave, Surprised,
    // Physical
    Grabbed, GrabReaction, Swinging, Thrown, RecoverFromThrow,
    // Inventory
    NoticeItem, CatchItem, InspectItem, PutInBackpack, OpenBackpack, SearchBackpack, PresentItem, MissingItem,
    // Utility
    ReminderAlert, TimerAlert, Thinking, Success, Error,
    // System
    PCBusy, PCIdle, DownloadWatching,
    // World
    PeekEdge, JumpMonitor, LandMonitor, LeaveScreen, ReturnToScreen,
    PlaceLadder, ClimbLadder, TieRope, ClimbRope,
    // Accessories & activities
    CloseBackpack, LaptopOpen, LaptopType, LaptopClose, ReadBook, WriteNotes, Dance, JumpForJoy,
}
