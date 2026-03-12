using JiggleSharp.Core.Hosting;

namespace JiggleSharp.Windows.System;

/// <summary>
/// Windows implementation of <see cref="ISystemIntegrationHandler"/>.
/// No-op on Windows — taskbar presence is tied to window creation and requires
/// no explicit management. See the macOS implementation for context.
/// </summary>
public class WindowsSystemIntegrationHandler : ISystemIntegrationHandler
{
    /// <inheritdoc/>
    public void ShowWindowIndicator() { }

    /// <inheritdoc/>
    public void HideWindowIndicator() { }
}