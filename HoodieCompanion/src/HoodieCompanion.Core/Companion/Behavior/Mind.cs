namespace HoodieCompanion.Companion.Behavior;

/// <summary>What the user seems to be doing, judged only from input idle time (never from content).</summary>
public enum UserActivity
{
    Active,
    Idle,
    Away,
}

/// <summary>
/// Stages of the away-from-keyboard timeline. Thresholds (minutes of no input):
/// 5 relaxed → 15 bored → 30 explores the desktop → 60 sleepy → 90 finds a spot and sleeps.
/// </summary>
public enum AfkPhase
{
    Present,
    Relaxed,
    Bored,
    Exploring,
    Sleepy,
    Asleep,
}

/// <summary>
/// Stable character traits (0..1). Seeded once per install so every Hoodie is a little different, then
/// shaped slowly by what happens (attachment grows with time together, caution with scares, confidence with
/// successful climbs). One trait influences many decisions; none maps to a single mechanic.
/// </summary>
public sealed class Personality
{
    public double Curiosity { get; set; } = 0.55;
    public double Energy { get; set; } = 0.55;
    public double Confidence { get; set; } = 0.5;
    public double Playfulness { get; set; } = 0.5;
    public double Attachment { get; set; } = 0.3;
    public double Comfort { get; set; } = 0.5;
    public double Caution { get; set; } = 0.45;

    public static Personality FromSeed(int seed)
    {
        var r = new Random(seed);
        double T() => 0.35 + r.NextDouble() * 0.4;
        return new Personality
        {
            Curiosity = T(), Energy = T(), Confidence = T(), Playfulness = T(), Attachment = 0.2 + r.NextDouble() * 0.15,
            Comfort = T(), Caution = T(),
        };
    }

    public override string ToString() =>
        $"curiosity {Curiosity:0.00} energy {Energy:0.00} confidence {Confidence:0.00} playfulness {Playfulness:0.00} " +
        $"attachment {Attachment:0.00} comfort {Comfort:0.00} caution {Caution:0.00}";
}

/// <summary>
/// Hoodie's hidden inner state. Values are 0..1, drift slowly on their own and are nudged by events.
/// They only bias which clip / activity is chosen and how; they are never shown as meters, and ignoring
/// Hoodie never makes it sad or punishes the user.
/// </summary>
public sealed class Mind
{
    public static readonly double[] AfkMinutes = { 5, 15, 30, 60, 90 };

    private readonly CharacterDrives _drives;

    public Mind(CharacterDrives drives) => _drives = drives;

    /// <summary>Stable traits (see <see cref="Personality"/>).</summary>
    public Personality Traits { get; set; } = new();

    /// <summary>What Hoodie currently intends and why (for the debug view and logs; never shown as UI).</summary>
    public string? CurrentIntent { get; set; }
    public string? CurrentReason { get; set; }

    // Core parameters (0..1).
    public double Energy => _drives.Energy;
    public double Curiosity => _drives.Curiosity;
    public double Mood { get; set; } = 0.65;
    public double Attention { get; set; } = 0.3;
    public double Boredom { get; set; } = 0.2;
    public double Sleepiness { get; set; } = 0.15;
    public double Stress { get; set; }
    public double Affection { get; set; } = 0.45;
    /// <summary>How busy Hoodie currently is (0 resting .. 1 running/playing).</summary>
    public double Activity { get; set; } = 0.3;

    // Context.
    public string? CurrentTarget { get; set; }
    public string? CurrentZone { get; set; }
    public string? HeldItem { get; set; }
    public int BackpackItems { get; set; }
    public UserActivity UserActivity { get; private set; }
    public double PcLoad { get; set; }
    public double TimeSinceInteraction { get; private set; }
    public double UserIdleSeconds { get; private set; }
    public AfkPhase Afk { get; private set; }
    public int Hour { get; set; } = 12;

    public bool IsNight => Hour >= 23 || Hour < 6;

    /// <summary>Scales the AFK timeline (tests and the QA scenario use short timelines).</summary>
    public double AfkScale { get; set; } = 1;

    public static AfkPhase PhaseFor(double idleSeconds, double scale = 1)
    {
        var min = idleSeconds / 60 / Math.Max(1e-6, scale);
        if (min >= AfkMinutes[4]) return AfkPhase.Asleep;
        if (min >= AfkMinutes[3]) return AfkPhase.Sleepy;
        if (min >= AfkMinutes[2]) return AfkPhase.Exploring;
        if (min >= AfkMinutes[1]) return AfkPhase.Bored;
        if (min >= AfkMinutes[0]) return AfkPhase.Relaxed;
        return AfkPhase.Present;
    }

    /// <summary>Advances slow drifts. Returns the previous AFK phase when it changed (otherwise null).</summary>
    public AfkPhase? Update(double dt, BehaviorState state, double userIdleSeconds, bool moving)
    {
        UserIdleSeconds = userIdleSeconds;
        UserActivity = userIdleSeconds < 60 ? UserActivity.Active : userIdleSeconds < 300 * AfkScale ? UserActivity.Idle : UserActivity.Away;
        TimeSinceInteraction += dt;

        var resting = state is BehaviorState.Sitting or BehaviorState.Sleeping;
        Activity = Approach(Activity, moving ? 0.8 : resting ? 0.05 : 0.3, dt / 4);
        // Boredom grows while nothing happens, falls while doing things or when the user plays.
        Boredom += dt * (state == BehaviorState.Idle ? 1 / 240.0 : state == BehaviorState.Activity ? -1 / 60.0 : 1 / 600.0);
        if (moving) Boredom -= dt / 90;
        // Sleepiness follows low energy, the clock and long absence.
        var sleepTarget = (1 - Energy) * 0.6 + (IsNight ? 0.3 : 0) + Math.Min(0.4, userIdleSeconds / 3600 / AfkScale * 0.4);
        Sleepiness = Approach(Sleepiness, state == BehaviorState.Sleeping ? 0 : sleepTarget, dt / (state == BehaviorState.Sleeping ? 120 : 300));
        Stress = Approach(Stress, 0, dt / 20);
        Mood = Approach(Mood, 0.65 - Stress * 0.3, dt / 180);
        Attention = Approach(Attention, userIdleSeconds < 5 ? 0.5 : 0.1, dt / 10);
        Affection = Approach(Affection, 0.45, dt / 1800);
        Clamp();

        var phase = PhaseFor(userIdleSeconds, AfkScale);
        if (phase == Afk) return null;
        var old = Afk;
        Afk = phase;
        return old;
    }

    // ------------------------------------------------------------------ events

    public void OnUserInteraction()
    {
        TimeSinceInteraction = 0;
        Attention = Math.Max(Attention, 0.8);
        Boredom -= 0.3;
        Clamp();
    }

    public void OnClicked() { OnUserInteraction(); Affection += 0.05; Mood += 0.05; Clamp(); }
    public void OnGrabbed(bool wasAsleep) { OnUserInteraction(); Stress += wasAsleep ? 0.45 : 0.15; Clamp(); }

    public void OnReleased(double speedDip, double heldSeconds)
    {
        if (speedDip > 1800) Stress += 0.2;
        if (heldSeconds > 4 && speedDip < 400) Affection += 0.05;
        Clamp();
    }

    public void OnHeldTick(double dt, double swingSpeedDeg)
    {
        // Hard swinging stresses, gentle carrying calms.
        Stress += dt * (Math.Abs(swingSpeedDeg) > 180 ? 0.35 : -0.08);
        if (Math.Abs(swingSpeedDeg) < 60) Affection += dt * 0.01;
        Clamp();
    }

    public void OnLanded(bool hard) { if (hard) { Stress += 0.25; Mood -= 0.05; } Clamp(); }
    public void OnSmallWin() { Mood += 0.08; Stress -= 0.1; Clamp(); }
    public void OnItemReceived() { OnUserInteraction(); Mood += 0.05; Curiosity2(0.1); Clamp(); }
    public void OnFailure() { Mood -= 0.08; Stress += 0.1; Clamp(); }
    public void OnPlayed() { Boredom -= 0.2; Mood += 0.05; Clamp(); }
    public void OnRested() { Sleepiness -= 0.4; Clamp(); }

    public void Curiosity2Boost(double d) => Curiosity2(d);

    private void Curiosity2(double d) => _drives.Curiosity = Math.Clamp(_drives.Curiosity + d, 0, 1);

    private static double Approach(double v, double target, double k) => v + (target - v) * Math.Clamp(k, 0, 1);

    private void Clamp()
    {
        Mood = Math.Clamp(Mood, 0, 1);
        Attention = Math.Clamp(Attention, 0, 1);
        Boredom = Math.Clamp(Boredom, 0, 1);
        Sleepiness = Math.Clamp(Sleepiness, 0, 1);
        Stress = Math.Clamp(Stress, 0, 1);
        Affection = Math.Clamp(Affection, 0, 1);
        Activity = Math.Clamp(Activity, 0, 1);
    }

    public override string ToString() =>
        $"energy {Energy:0.00} mood {Mood:0.00} curiosity {Curiosity:0.00} attention {Attention:0.00} boredom {Boredom:0.00} " +
        $"sleepiness {Sleepiness:0.00} stress {Stress:0.00} affection {Affection:0.00} activity {Activity:0.00} afk {Afk}";
}
