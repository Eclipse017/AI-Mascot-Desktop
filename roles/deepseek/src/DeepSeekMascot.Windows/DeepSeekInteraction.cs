using AIMascot.Core;

namespace AIMascot.DeepSeek;

internal static class DeepSeekInteraction
{
    // Blue bangs above the eyes, below the maid headband; normalized to the original DeepSeek sprite.
    internal static bool OnHead(double x, double y) => x >= .29 && x <= .72 && y >= .28 && y <= .51;
    internal static void Hover(CompanionBehavior behavior, double x, double y, double now)
    {
        if (!OnHead(x, y)) { behavior.ClearHover(); return; }
        behavior.Hover(x, .16 + (y - .28) / .23 * .39, now);
    }
}
