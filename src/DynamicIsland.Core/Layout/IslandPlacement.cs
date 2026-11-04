namespace DynamicIsland.Core.Layout;

public readonly record struct IslandPosition(double Left, double Top);

public static class IslandPlacement
{
    /// <summary>
    /// Centers the window horizontally on the given monitor working area.
    /// </summary>
    public static IslandPosition TopCenter(
        double workAreaLeft,
        double workAreaTop,
        double workAreaWidth,
        double windowWidth,
        double topMargin)
    {
        if (!double.IsFinite(workAreaWidth) || workAreaWidth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workAreaWidth));
        }

        if (!double.IsFinite(windowWidth) || windowWidth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowWidth));
        }

        var left = workAreaLeft + ((workAreaWidth - windowWidth) / 2.0);
        var top = workAreaTop + Math.Clamp(topMargin, 0.0, 80.0);
        return new IslandPosition(left, top);
    }
}
