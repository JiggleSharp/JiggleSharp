using System;
using System.Collections.Generic;

namespace JiggleSharp.Cli;

/// <summary>
/// Parses JiggleSharp's command-line arguments.
///
/// <para>
/// Exactly one action flag may be supplied. Anything not recognised as a flag
/// (a bare word, or macOS's <c>-psn_…</c> process serial number, which the
/// Finder appends when launching a bundled app) is ignored and left for
/// Avalonia; an unrecognised <c>--flag</c> is an error, so that a typo such as
/// <c>--strat</c> reports itself instead of silently opening the tray UI.
/// </para>
/// </summary>
internal sealed class CommandLineOptions
{
    // =========================================================================
    // Constants
    // =========================================================================

    /// <summary>Prefix of the process serial number argument macOS passes to bundled apps.</summary>
    private const string MacProcessSerialNumberPrefix = "-psn_";

    // =========================================================================
    // Properties
    // =========================================================================

    /// <summary>The action requested by the arguments.</summary>
    public CliAction Action { get; private init; }

    /// <summary>
    /// Explanation of a parse failure. Non-null only when
    /// <see cref="Action"/> is <see cref="CliAction.Error"/>.
    /// </summary>
    public string? Error { get; private init; }

    // =========================================================================
    // Parsing
    // =========================================================================

    /// <summary>
    /// Parses the raw argument array into a single <see cref="CliAction"/>.
    /// </summary>
    public static CommandLineOptions Parse(string[] args)
    {
        var action = CliAction.Launch;
        var seen   = new List<string>();

        foreach (var arg in args)
        {
            if (string.IsNullOrWhiteSpace(arg))
                continue;

            // Not a flag, or a macOS Finder artefact — leave it for Avalonia.
            if (!arg.StartsWith('-') || arg.StartsWith(MacProcessSerialNumberPrefix, StringComparison.Ordinal))
                continue;

            var parsed = arg.ToLowerInvariant() switch
            {
                "--start"                   => CliAction.Start,
                "--stop"                    => CliAction.Stop,
                "--toggle"                  => CliAction.Toggle,
                "--status"                  => CliAction.Status,
                "--help" or "-h" or "-?"    => CliAction.Help,
                _                           => CliAction.Error
            };

            if (parsed == CliAction.Error)
                return new CommandLineOptions
                {
                    Action = CliAction.Error,
                    Error  = $"Unknown option '{arg}'."
                };

            // --help always wins, so that `--start --help` documents itself
            // rather than quietly starting the engine.
            if (parsed == CliAction.Help)
                return new CommandLineOptions { Action = CliAction.Help };

            seen.Add(arg);

            if (seen.Count > 1)
                return new CommandLineOptions
                {
                    Action = CliAction.Error,
                    Error  = $"Options '{seen[0]}' and '{arg}' cannot be combined; specify one action at a time."
                };

            action = parsed;
        }

        return new CommandLineOptions { Action = action };
    }

    // =========================================================================
    // Help
    // =========================================================================

    /// <summary>Usage text printed for <c>--help</c> and for a parse failure.</summary>
    public static string HelpText =>
        """
        JiggleSharp — a cross-platform mouse jiggler that runs in the system tray.

        Usage:
          JiggleSharp [option]

        Running JiggleSharp with no options launches the tray application. If an
        instance is already running, the new process reports that and exits rather
        than starting a second tray icon.

        Options:
          --start     Start jiggling. Sent to the already-running instance if there
                      is one; otherwise JiggleSharp launches with jiggling active,
                      regardless of the "Start engine on app launch" setting.
          --stop      Stop jiggling in the running instance. Does nothing (and does
                      not launch JiggleSharp) if no instance is running.
          --toggle    Invert the running instance's jiggling state. Behaves like
                      --start if no instance is running.
          --status    Report whether JiggleSharp is running and whether it is
                      currently jiggling. Never launches JiggleSharp.
          -h, --help  Show this help text.

        Exit codes:
          0  Success, including --stop and --status when no instance is running.
          1  An instance was reached but reported an error.
          2  The command line could not be parsed.

        Examples:
          JiggleSharp --start      # begin jiggling (launching the app if needed)
          JiggleSharp --stop       # pause jiggling without quitting the tray app
          JiggleSharp --status     # check the current state
        """;
}
