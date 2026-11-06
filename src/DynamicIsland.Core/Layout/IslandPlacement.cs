namespace DynamicIsland.Core.Layout;

public readonly record struct IslandPosition(double Left, double Top);

public enum IslandDockEdge
{
    Top,
    Bottom,
    Left,
    Right
}

public static class IslandPlacement
{
    public const double ShadowPadding = 14.0;

    public static IslandDockEdge ParseEdge(string? value) =>
        (value ?? "top").Trim().ToLowerInvariant() switch
        {
            "bottom" => IslandDockEdge.Bottom,
            "left" => IslandDockEdge.Left,
            "right" => IslandDockEdge.Right,
            _ => IslandDockEdge.Top
        };

    public static string EdgeName(IslandDockEdge edge) => edge switch
    {
        IslandDockEdge.Bottom => "bottom",
        IslandDockEdge.Left => "left",
        IslandDockEdge.Right => "right",
        _ => "top"
    };

    public static bool IsSideEdge(IslandDockEdge edge) =>
        edge is IslandDockEdge.Left or IslandDockEdge.Right;

    /// <summary>
    /// Centers the window horizontally on the given monitor working area.
    /// Kept for callers that still want the original top-center placement.
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

    /// <summary>
    /// Places the outer window so its visible (shadow-free) content snaps to
    /// <paramref name="edge"/>. <paramref name="offset"/> is normalized along
    /// that edge and is preserved when the island changes size.
    /// </summary>
    public static IslandPosition Place(
        double workAreaLeft,
        double workAreaTop,
        double workAreaWidth,
        double workAreaHeight,
        double outerWidth,
        double outerHeight,
        IslandDockEdge edge,
        double offset,
        double topMargin)
    {
        if (!double.IsFinite(workAreaWidth) || workAreaWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workAreaWidth));
        }

        if (!double.IsFinite(workAreaHeight) || workAreaHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workAreaHeight));
        }

        if (!double.IsFinite(outerWidth) || outerWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(outerWidth));
        }

        if (!double.IsFinite(outerHeight) || outerHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(outerHeight));
        }

        var contentWidth = Math.Max(0.0, outerWidth - (ShadowPadding * 2));
        var contentHeight = Math.Max(0.0, outerHeight - (ShadowPadding * 2));
        var normalized = double.IsFinite(offset) ? Math.Clamp(offset, 0.0, 1.0) : 0.5;

        // Offset is the center of the visible content as a fraction of the
        // working area. Using the center keeps the island anchored when its
        // width/height changes on an orientation switch.
        var centerX = workAreaLeft + (workAreaWidth * normalized);
        var centerY = workAreaTop + (workAreaHeight * normalized);
        var visibleLeft = Math.Clamp(
            centerX - (contentWidth / 2.0),
            workAreaLeft,
            Math.Max(workAreaLeft, workAreaLeft + workAreaWidth - contentWidth));
        var visibleTop = Math.Clamp(
            centerY - (contentHeight / 2.0),
            workAreaTop,
            Math.Max(workAreaTop, workAreaTop + workAreaHeight - contentHeight));

        switch (edge)
        {
            case IslandDockEdge.Bottom:
                visibleTop = workAreaTop + workAreaHeight - contentHeight;
                break;
            case IslandDockEdge.Left:
                visibleLeft = workAreaLeft;
                break;
            case IslandDockEdge.Right:
                visibleLeft = workAreaLeft + workAreaWidth - contentWidth;
                break;
            default:
                visibleTop = workAreaTop + Math.Clamp(topMargin, 0.0, 80.0);
                break;
        }

        return new IslandPosition(
            visibleLeft - ShadowPadding,
            visibleTop - ShadowPadding);
    }

    /// <summary>
    /// Computes the normalized offset of a visible content rectangle along the
    /// given edge, so a drag can be restored after the window changes size.
    /// </summary>
    public static double OffsetAlongEdge(
        double workAreaLeft,
        double workAreaTop,
        double workAreaWidth,
        double workAreaHeight,
        double visibleLeft,
        double visibleTop,
        double contentWidth,
        double contentHeight,
        IslandDockEdge edge)
    {
        if (IsSideEdge(edge))
        {
            return Math.Clamp(
                (visibleTop + (contentHeight / 2.0) - workAreaTop) / Math.Max(1.0, workAreaHeight),
                0.0,
                1.0);
        }

        return Math.Clamp(
            (visibleLeft + (contentWidth / 2.0) - workAreaLeft) / Math.Max(1.0, workAreaWidth),
            0.0,
            1.0);
    }

    /// <summary>
    /// Returns the nearest of the four working-area edges for the visible
    /// content rectangle. Corners therefore resolve to one of the two nearest
    /// sides instead of a diagonal/corner mode.
    /// </summary>
    public static IslandDockEdge NearestEdge(
        double workAreaLeft,
        double workAreaTop,
        double workAreaWidth,
        double workAreaHeight,
        double visibleLeft,
        double visibleTop,
        double contentWidth,
        double contentHeight)
    {
        var left = Math.Abs(visibleLeft - workAreaLeft);
        var top = Math.Abs(visibleTop - workAreaTop);
        var right = Math.Abs(workAreaLeft + workAreaWidth - (visibleLeft + contentWidth));
        var bottom = Math.Abs(workAreaTop + workAreaHeight - (visibleTop + contentHeight));

        var best = top;
        var edge = IslandDockEdge.Top;

        if (left < best)
        {
            best = left;
            edge = IslandDockEdge.Left;
        }

        if (right < best)
        {
            best = right;
            edge = IslandDockEdge.Right;
        }

        if (bottom < best)
        {
            edge = IslandDockEdge.Bottom;
        }

        return edge;
    }
}
