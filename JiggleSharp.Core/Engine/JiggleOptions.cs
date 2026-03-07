namespace JiggleSharp.Core.Engine;

public class JiggleOptions
{
    // thresholds
    public TimeSpan ActionTime { get; init; } = TimeSpan.FromSeconds(30);
    public long WarningLimitMs { get; init; } = 30_000;
    public long MinActionMs { get; init; } = 60_000;
    public long MaxActionMs { get; init; } = 120_000;

    // action execution
    public JiggleMode Mode { get; init; } = JiggleMode.Smooth;

    // target selection (default roughly matches your earlier example)
    public double TargetMinX { get; init; } = -400;
    public double TargetMaxX { get; init; } =  400;
    public double TargetMinY { get; init; } = -400;
    public double TargetMaxY { get; init; } =  400;

    // delays
    public int BatchStepDelayMs { get; init; } = 5;
    public int SmoothFallbackDelayUs { get; init; } = 10_000;
    public int PostActionCooldownMs { get; init; } = 3000;

    // logging toggles
    public bool LogStatusTransitions { get; init; } = true;
    public bool LogActions { get; init; } = true;
    public bool LogNextTrigger { get; init; } = false;
}