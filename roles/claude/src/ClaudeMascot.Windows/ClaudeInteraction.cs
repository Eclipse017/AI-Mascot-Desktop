using AIMascot.Core;

namespace AIMascot.Claude;

internal static class ClaudeInteraction
{
    // Claude's bangs/forehead, excluding flower, cheeks and costume (normalized sprite coordinates).
    internal static bool OnHead(double x, double y) => x >= .26 && x <= .72 && y >= .18 && y <= .47;
    internal static void Hover(CompanionBehavior behavior, double x, double y, double now)
    {
        if (!OnHead(x, y)) { behavior.ClearHover(); return; }
        behavior.Hover(x, .16 + (y - .18) / .29 * .39, now);
    }
}
