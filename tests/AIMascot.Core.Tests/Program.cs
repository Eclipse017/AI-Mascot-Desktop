using System;
using AIMascot.Core;

int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name); passed++;
}
var motion = new Motion();
Check(motion.React(1), "first click accepted");
Check(!motion.React(1.05), "rapid click has cooldown");
Check(motion.React(1.2), "later click replaces reaction");
for (int i = 0; i < 10000; i++)
{
    double time = 2 + i * .005;
    motion.React(time);
    var p = motion.Sample(time);
    if (!double.IsFinite(p.OffsetY) || p.ScaleX < .9 || p.ScaleY > 1.12)
        throw new Exception("Unbounded reaction");
}
Check(true, "10000 rapid events remain bounded");
motion.BeginDrag();
Check(!motion.React(70), "drag blocks click reaction");
motion.EndDrag();
var idle = new Motion().Sample(72);
Check(motion.Sample(72) == idle, "drag clears earlier reaction");
motion.React(80);
Check(motion.Sample(82) == new Motion().Sample(82), "reaction completes without backlog");
motion.Paused = true;
Check(!motion.React(90) && motion.Sample(90) == new Pose(1,1,0,0), "pause is stationary");
var pref = new PetPreferences { Left = double.NaN, Top = double.PositiveInfinity, Size = 2000 }.Sanitize();
Check(pref.Left == null && pref.Top == null && pref.Size == 360, "invalid coordinates and size bounded");
Check(new PetPreferences { Size = double.NaN }.Sanitize().Size == 220, "invalid size recovers");
var target = TargetBinding.CodexDesktop;
ProcessIdentity Valid(int id) => new(id, "ChatGPT", target.PackageFamily, "ChatGPT.exe");
PresenceSample Match(params ProcessIdentity[] processes) => PresenceMatcher.Match(target, processes);
Check(Match().Kind == PresenceKind.Stopped, "no candidate means stopped");
Check(Match(new ProcessIdentity(1, "AIAppVpnGuard", null, "AIAppVpnGuard.exe"),
    new(2, "codex", null, "codex.exe"), new(3, "node", null, "node.exe"),
    new(4, "chrome", null, "chrome.exe")).Kind == PresenceKind.Stopped, "guard CLI Node browser do not match");
Check(Match(new ProcessIdentity(1, "ChatGPT", "Other.Package", "ChatGPT.exe")).Kind == PresenceKind.Stopped,
    "same filename with another package does not match");
Check(Match(new ProcessIdentity(1, "ChatGPT", null, "ChatGPT.exe")).Kind == PresenceKind.Stopped, "unpackaged namesake does not match");
Check(Match(new ProcessIdentity(1, "ChatGPT", target.PackageFamily, "Other.exe")).Kind == PresenceKind.Stopped, "executable identity must also match");
Check(Match(new ProcessIdentity(1, "ChatGPT", null, null, ReadFailed: true)).Kind == PresenceKind.Unknown, "permission failure is unknown");
Check(Match(new ProcessIdentity(1, "ChatGPT", null, null, ReadFailed: true, Exited: true)).Kind == PresenceKind.Stopped, "exit during query is skipped");
Check(PresenceMatcher.Match(target, Array.Empty<ProcessIdentity>(), true).Kind == PresenceKind.Unknown, "enumeration failure is unknown");
Check(Match(Valid(1), Valid(2), Valid(1)).MatchedProcesses == 2, "multiple processes deduplicate by PID");
Check(Match(Valid(1), new(2, "ChatGPT", null, null, ReadFailed: true)).Kind == PresenceKind.Running,
    "known live target wins over unreadable sibling");
Check(Match(new ProcessIdentity(1, "chatgpt", target.PackageFamily.ToLowerInvariant(), "CHATGPT.EXE")).Kind == PresenceKind.Running,
    "Windows identity match ignores case");
var tracker = new PresenceTracker();
PresenceKind Observe(PresenceKind state, double seconds) => tracker.Observe(state, TimeSpan.FromSeconds(seconds));
Check(Observe(PresenceKind.Running, 0) == PresenceKind.Unknown, "startup waits for confirmation");
Check(Observe(PresenceKind.Running, 1) == PresenceKind.Running, "second live sample confirms");
Check(Observe(PresenceKind.Stopped, 2) == PresenceKind.Running, "single exit sample does not flicker");
Check(Observe(PresenceKind.Running, 3) == PresenceKind.Running, "quick restart retains running state");
Check(Observe(PresenceKind.Stopped, 4) == PresenceKind.Running && Observe(PresenceKind.Stopped, 5) == PresenceKind.Stopped,
    "two missing samples confirm exit");
Observe(PresenceKind.Running, 6); Observe(PresenceKind.Running, 7);
Check(Observe(PresenceKind.Unknown, 8) == PresenceKind.Running && Observe(PresenceKind.Unknown, 12.9) == PresenceKind.Running,
    "unknown preserves last state for less than five seconds");
Check(Observe(PresenceKind.Unknown, 13) == PresenceKind.Unknown, "unknown expires at five seconds");
Check(Observe(PresenceKind.Running, 14) == PresenceKind.Unknown && Observe(PresenceKind.Running, 15) == PresenceKind.Running,
    "unknown recovery needs two confirmations");
Observe(PresenceKind.Stopped, 16); Observe(PresenceKind.Unknown, 17);
Check(Observe(PresenceKind.Stopped, 18) == PresenceKind.Running, "unknown interrupts candidate confirmation");
foreach (var state in Enum.GetValues<PresenceKind>())
{
    Check(!DisplayPolicy.ShouldShow(DisplayMode.Auto, true, state) && !DisplayPolicy.ShouldShow(DisplayMode.Demo, true, state),
        "manual hide wins in both modes: " + state);
    Check(DisplayPolicy.ShouldShow(DisplayMode.Demo, false, state), "demo is independent of presence: " + state);
    Check(DisplayPolicy.ShouldShow(DisplayMode.Auto, false, state) == (state == PresenceKind.Running),
        "auto requires confirmed running: " + state);
}
Check(new PetPreferences { Mode = (DisplayMode)99 }.Sanitize().Mode == DisplayMode.Auto, "invalid mode safely becomes auto");
var companion = new CompanionBehavior();
Check(companion.Sample(1).Mood == PetMood.Idle, "companion initially idle");
Check(companion.Sample(5.72).Face == PetFace.Closed, "brief periodic blink uses closed frame");
Check(companion.Sample(6).Face == PetFace.Open, "blink returns to open frame");
Check(companion.Sample(121).Mood == PetMood.Resting, "inactivity enters local rest");
companion.Click(122);
Check(companion.Sample(122.1).Mood == PetMood.Greeting, "click wakes resting companion");
companion.Pet(123);
Check(companion.Sample(123.1).Face == PetFace.Happy, "head pat has happy expression");
Check(companion.Sample(125).Mood == PetMood.Idle, "happy response returns to idle");
companion.Pet(126); companion.BeginDrag(126.1); companion.Pet(126.2);
Check(companion.Sample(126.3).Mood == PetMood.Dragging, "drag overrides happy and ignores new pats");
companion.EndDrag(127);
Check(companion.Sample(127.1).Mood == PetMood.Idle, "drag ends without an old reaction queue");
companion.Hover(.3, .3, 128); companion.Hover(.42, .3, 128.1); companion.Hover(.54, .3, 128.2); companion.Hover(.66, .3, 128.3);
Check(companion.Sample(128.4).Mood == PetMood.Happy, "continuous head strokes trigger happy");
companion.Wake(130);
companion.Hover(.3, .8, 131); companion.Hover(.42, .8, 131.1); companion.Hover(.54, .8, 131.2); companion.Hover(.66, .8, 131.3);
Check(companion.Sample(131.4).Mood == PetMood.Idle, "moving across body is not a head pat");
companion.Wake(132); companion.Hover(.3, .3, 132); companion.Hover(.42, .3, 134); companion.Hover(.54, .3, 136); companion.Hover(.66, .3, 138);
Check(companion.Sample(138.1).Mood == PetMood.Idle, "widely spaced head motion does not accumulate");
companion.Paused = true; companion.Pet(140); companion.Click(140); companion.Rest(140);
Check(companion.Sample(200).Pose == new Pose(1,1,0,0) && companion.Sample(200).Sparkle == 0, "pause freezes interactions and effects");
companion.Paused = false; companion.RestEnabled = false; companion.Wake(201);
Check(companion.Sample(1000).Mood == PetMood.Idle, "idle rest can be disabled");
companion.Rest(1001);
Check(companion.Sample(1001.1).Mood == PetMood.Resting, "manual rest works independently of idle preference");
companion.Wake(1002);
Check(companion.Sample(1002.1).Mood == PetMood.Idle, "manual wake clears rest");
for (int i=0; i<10000; i++)
{
    double t=1100+i*.01; companion.Pet(t); companion.Click(t); var frame=companion.Sample(t);
    if (!double.IsFinite(frame.Pose.Angle) || frame.Sparkle < 0 || frame.Sparkle > 1 || frame.Pose.ScaleY > 1.12)
        throw new Exception("Unbounded companion behavior");
}
Check(true, "10000 mixed interactions remain bounded");
Check(!DisplayPolicy.ShouldShow(DisplayMode.Demo, false, PresenceKind.Running, fullscreen:true), "fullscreen suppresses demo too");
Check(!DisplayPolicy.ShouldShow(DisplayMode.Auto, false, PresenceKind.Running, suspended:true), "system suspend suppresses visible pet");
tracker.Reset();
Check(tracker.Stable == PresenceKind.Unknown && Observe(PresenceKind.Running, 200) == PresenceKind.Unknown, "resume discards stale identity until reconfirmed");
var screen = new ScreenRectangle(0,0,1920,1080);
Check(ScreenPolicy.CoversMonitor(screen,screen), "borderless fullscreen recognized");
Check(!ScreenPolicy.CoversMonitor(new(0,0,1920,1040),screen), "maximized work-area window is not fullscreen");
Check(!ScreenPolicy.CoversMonitor(new(1920,0,3840,1080),screen), "another monitor does not cover pet monitor");
Check(ScreenPolicy.CoversMonitor(new(-1920,0,0,1080),new(-1920,0,0,1080)), "negative monitor coordinates supported");
Check(!ScreenPolicy.CoversMonitor(new(0,0,0,0),screen), "invalid rectangles are not fullscreen");
Check(new PetPreferences { RestAfterSeconds = -5 }.Sanitize().RestAfterSeconds == 30 && new PetPreferences { RestAfterSeconds = 9000 }.Sanitize().RestAfterSeconds == 3600,
    "rest interval has bounded configuration");
Check(!FullscreenPolicy.IsFullscreen(screen, screen, true, true), "maximized decorated window covering auto-hide taskbar does not hide pet");
Check(!FullscreenPolicy.IsFullscreen(screen, screen, false, true), "decorated screen-sized desktop window is not exclusive fullscreen");
Check(FullscreenPolicy.IsFullscreen(screen, screen, false, false), "borderless fullscreen still hides pet");
Check(!FullscreenPolicy.IsFullscreen(new(1920,0,3840,1080),screen,false,false), "fullscreen on another monitor stays isolated");
var fullscreen = new FullscreenTracker();
Check(!fullscreen.Observe(true,TimeSpan.Zero), "one fullscreen observation cannot hide a clicked pet");
Check(!fullscreen.Observe(false,TimeSpan.FromSeconds(.5)), "short focus transient stays visible");
Check(!fullscreen.Observe(true,TimeSpan.FromSeconds(1)), "new fullscreen candidate restarts confirmation");
Check(fullscreen.Observe(true,TimeSpan.FromSeconds(2)), "sustained real fullscreen hides after confirmation");
Check(!fullscreen.Observe(false,TimeSpan.FromSeconds(2.1)), "leaving fullscreen restores immediately");
fullscreen.Observe(true,TimeSpan.FromSeconds(3));fullscreen.Observe(true,TimeSpan.FromSeconds(4));fullscreen.Reset();
Check(!fullscreen.Hidden && !fullscreen.Observe(true,TimeSpan.FromSeconds(5)), "resume discards old fullscreen state");
Console.WriteLine($"RESULT: {passed} checks passed");
