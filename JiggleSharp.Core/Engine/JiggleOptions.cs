namespace JiggleSharp.Core.Engine;

/// <summary>
/// Configuration for <see cref="JiggleEngine"/>. All properties have
/// sensible defaults and can be replaced on the live engine at any time —
/// the next idle event will pick up changes automatically.
///
/// Min/Max pairs define the range from which <see cref="JiggleEngine"/>
/// samples a random value once per jiggle, producing natural variation
/// between movements rather than a fixed, mechanical trajectory.
/// </summary>
public class JiggleOptions
{
    // -------------------------------------------------------------------------
    // Idle detection
    // -------------------------------------------------------------------------

    /// <summary>
    /// How long the user must be idle before a jiggle is performed.
    /// Defaults to 5 minutes.
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(5);

    // -------------------------------------------------------------------------
    // WindMouse tuning — mouse speed
    // -------------------------------------------------------------------------

    /// <summary>
    /// Minimum mouse speed divisor applied to the force vector each step.
    /// Higher values produce slower, more deliberate movement.
    /// </summary>
    public double MouseSpeedMinimum { get; set; } = 5.0d;

    /// <summary>
    /// Maximum mouse speed divisor applied to the force vector each step.
    /// Higher values produce slower, more deliberate movement.
    /// </summary>
    public double MouseSpeedMaximum { get; set; } = 15.0d;

    // -------------------------------------------------------------------------
    // WindMouse tuning — gravity
    // -------------------------------------------------------------------------

    /// <summary>
    /// Minimum gravity strength pulling the cursor toward the target each step.
    /// Higher values produce straighter, more direct paths.
    /// </summary>
    public double GravityMinimum { get; set; } = 5.0d;

    /// <summary>
    /// Maximum gravity strength pulling the cursor toward the target each step.
    /// Higher values produce straighter, more direct paths.
    /// </summary>
    public double GravityMaximum { get; set; } = 10.0d;

    // -------------------------------------------------------------------------
    // WindMouse tuning — wind
    // -------------------------------------------------------------------------

    /// <summary>
    /// Minimum wind perturbation magnitude added to the velocity each step.
    /// Higher values produce more erratic, wandering paths.
    /// </summary>
    public double WindMinimum { get; set; } = 1.0d;

    /// <summary>
    /// Maximum wind perturbation magnitude added to the velocity each step.
    /// Higher values produce more erratic, wandering paths.
    /// </summary>
    public double WindMaximum { get; set; } = 5.0d;

    // -------------------------------------------------------------------------
    // WindMouse tuning — target radius
    // -------------------------------------------------------------------------

    /// <summary>
    /// Minimum acceptable distance from the target at which path generation
    /// stops. A larger radius means the cursor does not need to reach the
    /// exact target pixel before the move is considered complete.
    /// </summary>
    public double TargetRadiusMinimum { get; set; } = 2.0d;

    /// <summary>
    /// Maximum acceptable distance from the target at which path generation
    /// stops. A larger radius means the cursor does not need to reach the
    /// exact target pixel before the move is considered complete.
    /// </summary>
    public double TargetRadiusMaximum { get; set; } = 5.0d;

    // -------------------------------------------------------------------------
    // WindMouse tuning — velocity cap
    // -------------------------------------------------------------------------

    /// <summary>
    /// Minimum cap on the velocity magnitude per step (pixels per iteration).
    /// Prevents the cursor from moving too slowly between path points.
    /// </summary>
    public double VelocityMaxStepMinimum { get; set; } = 5.0d;

    /// <summary>
    /// Maximum cap on the velocity magnitude per step (pixels per iteration).
    /// Prevents the cursor from overshooting or jumping erratically.
    /// </summary>
    public double VelocityMaxStepMaximum { get; set; } = 15.0d;

    // -------------------------------------------------------------------------
    // Path generation limits
    // -------------------------------------------------------------------------

    /// <summary>
    /// Hard upper bound on the number of points a generated path may contain.
    /// Acts as a safety valve to prevent runaway generation if the WindMouse
    /// loop fails to converge toward the target.
    /// </summary>
    public int PathPointsMaximum { get; set; } = 1000;

    // -------------------------------------------------------------------------
    // Per-point movement delay (smooth mode)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Minimum delay between consecutive path points, in microseconds.
    /// Only used in smooth execution mode. Defaults to 2000 µs (2 ms).
    /// </summary>
    public int MovementDelayMinimum { get; set; } = 2_000;

    /// <summary>
    /// Maximum delay between consecutive path points, in microseconds.
    /// Only used in smooth execution mode. Defaults to 3500 µs (3.5 ms).
    /// </summary>
    public int MovementDelayMaximum { get; set; } = 3_500;
}