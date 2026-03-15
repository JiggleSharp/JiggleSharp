using System;
using System.Reflection;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JiggleSharp.Core;

namespace JiggleSharp.ViewModels;

/// <summary>
/// ViewModel for the Settings window. Exposes bindable properties for all
/// <see cref="ApplicationConfiguration"/> fields and handles loading from and
/// saving back to the active configuration.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    #region Private Fields

    private readonly ApplicationConfiguration _settings;
    private readonly Action<ApplicationConfiguration> _saveAction;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes the ViewModel and populates all properties from the provided configuration.
    /// </summary>
    /// <param name="config">The active application configuration to edit.</param>
    /// <param name="saveAction">
    /// Callback invoked with the updated configuration when the user confirms.
    /// Responsible for persisting to disk and closing the window.
    /// </param>
    public SettingsViewModel(ApplicationConfiguration config, Action<ApplicationConfiguration> saveAction)
    {
        _settings = config;
        _saveAction = saveAction;
        LoadFromSettings();
    }

    #endregion

    #region General Properties

    /// <summary>
    /// Whether the engine should be started automatically when the application starts
    /// </summary>
    [ObservableProperty] private bool _startEngineOnStartup;
    
    /// <summary>
    /// Whether the engine should be started automatically when the system starts up.
    /// </summary>
    [ObservableProperty] private bool _startOnSystemStartup;

    /// <summary>The emoji or symbol displayed as the system tray icon.</summary>
    [ObservableProperty] private string _trayIcon;

    /// <summary>The color applied to the tray icon indicator.</summary>
    [ObservableProperty] private Color _trayIconColor;

    /// <summary>
    /// The idle timeout duration as a string in <c>hh:mm:ss</c> format.
    /// Parsed to <see cref="TimeSpan"/> on save.
    /// </summary>
    [ObservableProperty] private double _idleTimeout;

    #endregion

    #region Engine Range Properties

    /// <summary>Minimum mouse movement speed in pixels per step.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MouseSpeedLabel))]
    private double _mouseSpeedMinimum;

    /// <summary>Maximum mouse movement speed in pixels per step.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MouseSpeedLabel))]
    private double _mouseSpeedMaximum;

    /// <summary>Minimum gravity force applied during WindMouse movement.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GravityLabel))]
    private double _gravityMinimum;

    /// <summary>Maximum gravity force applied during WindMouse movement.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GravityLabel))]
    private double _gravityMaximum;

    /// <summary>Minimum wind perturbation applied during WindMouse movement.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindLabel))]
    private double _windMinimum;

    /// <summary>Maximum wind perturbation applied during WindMouse movement.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindLabel))]
    private double _windMaximum;

    /// <summary>Minimum radius of the movement target zone in pixels.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetRadiusLabel))]
    private double _targetRadiusMinimum;

    /// <summary>Maximum radius of the movement target zone in pixels.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetRadiusLabel))]
    private double _targetRadiusMaximum;

    /// <summary>Minimum per-step velocity cap during movement.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VelocityMaxStepLabel))]
    private double _velocityMaxStepMinimum;

    /// <summary>Maximum per-step velocity cap during movement.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VelocityMaxStepLabel))]
    private double _velocityMaxStepMaximum;

    /// <summary>Minimum delay between movement bursts in milliseconds.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MovementDelayLabel))]
    private double _movementDelayMinimum;

    /// <summary>Maximum delay between movement bursts in milliseconds.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MovementDelayLabel))]
    private double _movementDelayMaximum;

    /// <summary>Maximum number of waypoints generated per movement path.</summary>
    [ObservableProperty] private double _pathPointsMaximum;

    #endregion

    #region Range Labels

    // These are computed display strings shown beneath each range slider,
    // updated automatically when either bound of the range changes.

    /// <summary>Human-readable label for the mouse speed range.</summary>
    public string MouseSpeedLabel      => $"{MouseSpeedMinimum:0} – {MouseSpeedMaximum:0} px/step";

    /// <summary>Human-readable label for the gravity range.</summary>
    public string GravityLabel         => $"{GravityMinimum:0} – {GravityMaximum:0}";

    /// <summary>Human-readable label for the wind range.</summary>
    public string WindLabel            => $"{WindMinimum:0} – {WindMaximum:0}";

    /// <summary>Human-readable label for the target radius range.</summary>
    public string TargetRadiusLabel    => $"{TargetRadiusMinimum:0} – {TargetRadiusMaximum:0} px";

    /// <summary>Human-readable label for the velocity max step range.</summary>
    public string VelocityMaxStepLabel => $"{VelocityMaxStepMinimum:0} – {VelocityMaxStepMaximum:0}";

    /// <summary>Human-readable label for the movement delay range.</summary>
    public string MovementDelayLabel   => $"{MovementDelayMinimum:0} – {MovementDelayMaximum:0} ms";

    #endregion
    
    #region About Labels
    public string ApplicationVersion => Assembly.GetEntryAssembly()?.GetName()?.Version?.ToString() ?? string.Empty;
    #endregion

    #region Events

    /// <summary>
    /// Raised when the ViewModel requests the view to close (e.g. Cancel).
    /// The view should subscribe and call <c>Close()</c> in response.
    /// </summary>
    public event Action? CloseRequested;

    #endregion

    #region Commands

    /// <summary>
    /// Closes the settings window without saving any changes.
    /// </summary>
    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke();

    /// <summary>
    /// Writes all bound values back to the configuration object and invokes
    /// <see cref="_saveAction"/> to persist and close the window.
    /// </summary>
    [RelayCommand]
    private void Save()
    {
        _settings.StartEngineOnApplicationStart = StartEngineOnStartup;
        _settings.StartJiggleSharpOnSystemStartup = StartOnSystemStartup;
        _settings.TrayIcon = TrayIcon;
        _settings.TrayIconColor = TrayIconColor;
        _settings.JigglerEngineOptions.IdleTimeout = TimeSpan.FromSeconds(IdleTimeout);

        var e = _settings.JigglerEngineOptions;
        e.MouseSpeedMinimum    = (int)MouseSpeedMinimum;
        e.MouseSpeedMaximum    = (int)MouseSpeedMaximum;
        e.GravityMinimum       = (int)GravityMinimum;
        e.GravityMaximum       = (int)GravityMaximum;
        e.WindMinimum          = (int)WindMinimum;
        e.WindMaximum          = (int)WindMaximum;
        e.TargetRadiusMinimum  = (int)TargetRadiusMinimum;
        e.TargetRadiusMaximum  = (int)TargetRadiusMaximum;
        e.VelocityMaxStepMinimum = (int)VelocityMaxStepMinimum;
        e.VelocityMaxStepMaximum = (int)VelocityMaxStepMaximum;
        e.MovementDelayMinimum = (int)MovementDelayMinimum;
        e.MovementDelayMaximum = (int)MovementDelayMaximum;
        e.PathPointsMaximum    = (int)PathPointsMaximum;

        _saveAction(_settings);
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Populates all ViewModel properties from the current <see cref="_settings"/> state.
    /// Called once during construction.
    /// </summary>
    private void LoadFromSettings()
    {
        StartEngineOnStartup = _settings.StartEngineOnApplicationStart;
        StartOnSystemStartup = _settings.SystemIntegrationHandler?.IsStartupApplicationRegistered() ?? false;
        TrayIcon      = _settings.TrayIcon;
        TrayIconColor = _settings.TrayIconColor;
        IdleTimeout   = _settings.JigglerEngineOptions.IdleTimeout.TotalSeconds;

        var e = _settings.JigglerEngineOptions;
        MouseSpeedMinimum      = e.MouseSpeedMinimum;
        MouseSpeedMaximum      = e.MouseSpeedMaximum;
        GravityMinimum         = e.GravityMinimum;
        GravityMaximum         = e.GravityMaximum;
        WindMinimum            = e.WindMinimum;
        WindMaximum            = e.WindMaximum;
        TargetRadiusMinimum    = e.TargetRadiusMinimum;
        TargetRadiusMaximum    = e.TargetRadiusMaximum;
        VelocityMaxStepMinimum = e.VelocityMaxStepMinimum;
        VelocityMaxStepMaximum = e.VelocityMaxStepMaximum;
        MovementDelayMinimum   = e.MovementDelayMinimum;
        MovementDelayMaximum   = e.MovementDelayMaximum;
        PathPointsMaximum      = e.PathPointsMaximum;
    }

    #endregion
}