using AIMascot.Core;

namespace AIMascot.Gemini;

internal static class GeminiInteraction
{
    // Gemini's bangs/forehead, excluding ears, cheeks and costume (normalized sprite coordinates).
    internal static bool OnHead(double x, double y) => x >= .27 && x <= .75 && y >= .25 && y <= .57;
    internal static void Hover(CompanionBehavior behavior, double x, double y, double now)
    {
        if (!OnHead(x, y)) { behavior.ClearHover(); return; }
        behavior.Hover(x, .16 + (y - .25) / .32 * .39, now);
    }
}
