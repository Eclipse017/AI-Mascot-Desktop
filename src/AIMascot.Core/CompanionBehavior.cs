using System;

namespace AIMascot.Core;

public enum PetMood { Idle, Greeting, Happy, Resting, Dragging }
public enum PetFace { Open, Closed, Happy }
public readonly record struct CompanionFrame(Pose Pose, PetMood Mood, PetFace Face, double Sparkle);

/// <summary>Bounded local interaction state, independent of OS input and application presence.</summary>
public sealed class CompanionBehavior
{
    private readonly Motion _motion = new();
    private double _lastActivity, _happyUntil, _greetUntil, _lastSample;
    private double _lean, _targetLean, _lastStroke = double.NegativeInfinity;
    private double? _previousHeadX;
    private double _strokeDistance;
    private bool _restRequested;
    public bool Paused { get => _motion.Paused; set { _motion.Paused = value; if (value) ClearHover(); } }
    public bool RestEnabled { get; set; } = true;
    public double RestAfterSeconds { get; set; } = 120;
    public bool Dragging => _motion.Dragging;

    public bool Click(double now)
    {
        if (Paused || Dragging) return false;
        _restRequested = false; _lastActivity = now; _greetUntil = now + .85;
        return _motion.React(now);
    }
    public void BeginDrag(double now)
    {
        _restRequested = false; _lastActivity = now; _happyUntil = _greetUntil = 0;
        ClearHover(); _motion.BeginDrag();
    }
    public void EndDrag(double now)
    {
        if (!Dragging) return;
        _motion.EndDrag(); _lastActivity = now;
        if (!Paused) _motion.React(now);
    }
    public void Hover(double x, double y, double now)
    {
        if (Paused || Dragging) return;
        _targetLean = Math.Clamp((x - .5) * 5, -2.5, 2.5);
        bool onHead = x >= .22 && x <= .78 && y >= .16 && y <= .55;
        if (!onHead) { _previousHeadX = null; _strokeDistance = 0; return; }
        if (now - _lastStroke > .7) _strokeDistance = 0;
        if (_previousHeadX is double prior && now - _lastStroke < .7)
            _strokeDistance += Math.Min(Math.Abs(x - prior), .12);
        _previousHeadX = x; _lastStroke = now;
        if (_strokeDistance >= .30)
        {
            Pet(now); _strokeDistance = 0;
        }
    }
    public void Pet(double now)
    {
        if (Paused || Dragging) return;
        _restRequested = false; _lastActivity = now; _happyUntil = now + 1.8;
        _motion.React(now);
    }
    public void Wake(double now) { _restRequested = false; _lastActivity = now; _happyUntil = _greetUntil = 0; ClearHover(); }
    public void Rest(double now) { if (Paused || Dragging) return; _restRequested = true; _happyUntil = _greetUntil = 0; ClearHover(); }
    public void ClearHover() { _targetLean = 0; _previousHeadX = null; _strokeDistance = 0; }
    public CompanionFrame Sample(double now)
    {
        double dt = Math.Clamp(now - _lastSample, 0, .1); _lastSample = now;
        _lean += (_targetLean - _lean) * (1 - Math.Exp(-dt * 10));
        if (Paused) return new(new(1, 1, 0, 0), PetMood.Idle, PetFace.Open, 0);
        if (Dragging) return new(_motion.Sample(now), PetMood.Dragging, PetFace.Open, 0);
        bool resting = _restRequested || (RestEnabled && now - _lastActivity >= Math.Clamp(RestAfterSeconds, 30, 3600));
        if (resting)
            return new(new(1, 1 + .009 * Math.Sin(now * 1.4), -.8, Math.Sin(now * 1.4)), PetMood.Resting, PetFace.Closed, 0);
        var pose = _motion.Sample(now);
        pose = pose with { Angle = pose.Angle + _lean };
        if (now < _happyUntil)
            return new(pose with { Angle = pose.Angle + 2 * Math.Sin(now * 10) }, PetMood.Happy, PetFace.Happy, Math.Clamp((_happyUntil - now) / 1.8, 0, 1));
        bool blink = now % 5.7 < .14 || (now % 17.1 > .30 && now % 17.1 < .40);
        return new(pose, now < _greetUntil ? PetMood.Greeting : PetMood.Idle, blink ? PetFace.Closed : PetFace.Open,
            now < _greetUntil ? Math.Clamp((_greetUntil - now) / .85, 0, 1) : 0);
    }
}

public readonly record struct ScreenRectangle(double Left, double Top, double Right, double Bottom)
{
    public double Width => Right - Left;
    public double Height => Bottom - Top;
}
public static class ScreenPolicy
{
    public static bool CoversMonitor(ScreenRectangle foreground, ScreenRectangle monitor, double tolerance = 2) =>
        foreground.Width > 0 && foreground.Height > 0 && monitor.Width > 0 && monitor.Height > 0 &&
        foreground.Left <= monitor.Left + tolerance && foreground.Top <= monitor.Top + tolerance &&
        foreground.Right >= monitor.Right - tolerance && foreground.Bottom >= monitor.Bottom - tolerance;
}
