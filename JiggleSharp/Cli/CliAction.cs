namespace JiggleSharp.Cli;

/// <summary>
/// What the process should do, as determined by its command-line arguments.
/// </summary>
internal enum CliAction
{
    /// <summary>No recognised flags — launch the tray application normally.</summary>
    Launch,

    /// <summary>Start jiggling (<c>--start</c>).</summary>
    Start,

    /// <summary>Stop jiggling (<c>--stop</c>).</summary>
    Stop,

    /// <summary>Invert the current jiggling state (<c>--toggle</c>).</summary>
    Toggle,

    /// <summary>Report whether JiggleSharp is running and jiggling (<c>--status</c>).</summary>
    Status,

    /// <summary>Print usage and exit (<c>--help</c>).</summary>
    Help,

    /// <summary>Arguments could not be parsed; <see cref="CommandLineOptions.Error"/> explains why.</summary>
    Error
}
