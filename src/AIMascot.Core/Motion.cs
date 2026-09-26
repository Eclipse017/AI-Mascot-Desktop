using System;

namespace AIMascot.Core;

public readonly record struct Pose(double ScaleX, double ScaleY, double Angle, double OffsetY);

/// <summary>One bounded reaction, no queued animations. Time comes from the caller.</summary>
public sealed class Motion
{
    private double _reactionStarted = double.NegativeInfinity;
    public bool Paused { get; set; }
    public bool Dragging { get; private set; }

    public bool React(double seconds)
    {
        if (Paused || Dragging || seconds - _reactionStarted < 0.12) return false;
        _reactionStarted = seconds;
        return true;
    }

    public void BeginDrag() { Dragging = true; _reactionStarted = double.NegativeInfinity; }
    public void EndDrag() => Dragging = false;

    public Pose Sample(double seconds)
    {
        if (Paused) return new(1, 1, 0, 0);
        if (Dragging) return new(1.025, 0.975, -3, 0);
        double breath = Math.Sin(seconds * Math.PI / 2.3);
        double elapsed = seconds - _reactionStarted;
        double bounce = elapsed >= 0 && elapsed < 0.85
            ? Math.Sin(elapsed * 19) * Math.Exp(-elapsed * 5) : 0;
        return new(1 - 0.025 * bounce, 1 + 0.012 * breath + 0.065 * bounce,
            1.1 * Math.Sin(seconds * 0.75) + 4 * bounce, -2.8 * breath - 7 * bounce);
    }
}

public sealed record PetPreferences
{
    public int Version { get; init; } = 3;
    public double? Left { get; init; }
    public double? Top { get; init; }
    public double Size { get; init; } = 220;
    public DisplayMode Mode { get; init; } = DisplayMode.Demo;
    public bool UserHidden { get; init; }
    public bool Paused { get; init; }
    public bool HideOnFullscreen { get; init; } = true;
    public bool RestEnabled { get; init; } = true;
    public bool ReducedMotion { get; init; }
    public int RestAfterSeconds { get; init; } = 120;

    public PetPreferences Sanitize() => this with
    {
        Version = 3,
        RestAfterSeconds = Math.Clamp(RestAfterSeconds, 30, 3600),
        Mode = Enum.IsDefined(Mode) ? Mode : DisplayMode.Auto,
        Left = Left is double x && double.IsFinite(x) && Math.Abs(x) < 100000 ? x : null,
        Top = Top is double y && double.IsFinite(y) && Math.Abs(y) < 100000 ? y : null,
        Size = double.IsFinite(Size) ? Math.Clamp(Size, 120, 360) : 220
    };
}
