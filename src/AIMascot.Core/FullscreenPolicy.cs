using System;

namespace AIMascot.Core;

public static class FullscreenPolicy
{
    // A normal maximized/decorated desktop window can cover the monitor with an auto-hidden taskbar.
    public static bool IsFullscreen(ScreenRectangle window, ScreenRectangle monitor, bool maximized, bool decorated) =>
        !maximized && !decorated && ScreenPolicy.CoversMonitor(window, monitor);
}

public sealed class FullscreenTracker
{
    private bool _candidate;
    private TimeSpan _since;
    public bool Hidden { get; private set; }
    public bool Observe(bool fullscreen, TimeSpan now)
    {
        if (fullscreen != _candidate) { _candidate = fullscreen; _since = now; }
        // One brief focus/geometry sample cannot hide a companion. Leaving fullscreen restores immediately.
        Hidden = fullscreen && (Hidden || now - _since >= TimeSpan.FromSeconds(1));
        return Hidden;
    }
    public void Reset() { _candidate = Hidden = false; _since = TimeSpan.Zero; }
}
