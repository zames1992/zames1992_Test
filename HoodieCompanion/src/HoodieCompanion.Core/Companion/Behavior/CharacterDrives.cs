namespace HoodieCompanion.Companion.Behavior;

/// <summary>
/// Hidden internal drives that make autonomous behavior varied. Never shown as bars, never decay into
/// guilt: ignoring Hoodie has no consequences, it simply entertains itself.
/// </summary>
public sealed class CharacterDrives
{
    public double Energy { get; set; } = 0.8;
    public double Curiosity { get; set; } = 0.5;
    public double Playfulness { get; set; } = 0.4;
    public double SocialInterest { get; set; } = 0.4;
    public double Comfort { get; set; } = 0.8;

    public void Update(double dt, BehaviorState state, bool running)
    {
        switch (state)
        {
            case BehaviorState.Sleeping:
                Energy += dt / 240;
                break;
            case BehaviorState.Sitting:
                Energy += dt / 900;
                break;
            case BehaviorState.Walking:
                Energy -= dt / (running ? 420 : 900);
                break;
            default:
                Energy -= dt / 2400;
                break;
        }
        if (state is BehaviorState.Idle or BehaviorState.Sitting) Curiosity += dt / 500;
        SocialInterest += dt / 1200;
        Comfort += dt / 240;
        Playfulness += (0.4 - Playfulness) * dt / 600;
        Clamp();
    }

    public void OnExplored() { Curiosity -= 0.35; Clamp(); }
    public void OnUserAttention() { SocialInterest -= 0.4; Playfulness += 0.1; Clamp(); }
    public void OnHardLanding() { Comfort -= 0.3; Clamp(); }
    public void OnEnvironmentBusy() { Comfort -= 0.15; Clamp(); }

    private void Clamp()
    {
        Energy = Math.Clamp(Energy, 0, 1);
        Curiosity = Math.Clamp(Curiosity, 0, 1);
        Playfulness = Math.Clamp(Playfulness, 0, 1);
        SocialInterest = Math.Clamp(SocialInterest, 0, 1);
        Comfort = Math.Clamp(Comfort, 0, 1);
    }
}
