namespace JiggleSharp.Core.Engine;

public sealed record JiggleSnapshot(
    DateTimeOffset TimestampUtc,
    EngineStatus Status,
    string Emoji,
    TimeSpan IdleMs,
    TimeSpan WarningLimitMs,
    TimeSpan ActionLimitMs,
    JiggleMode Mode);