namespace DynamicIsland.Core.Models;

public readonly record struct IslandSize(double Width, double Height);

/// <summary>
/// Single source of truth for the island geometry. Values are in device-independent
/// pixels (DIPs). <see cref="ShadowPadding"/> is transparent space reserved for the
/// WPF drop shadow and is included in <see cref="Outer"/>.
/// </summary>
public static class IslandMetrics
{
    public const double ShadowPadding = 14.0;

    /// <summary>Larger Expanded footprint used by the translation workspace.</summary>
    public static IslandSize Translation { get; } = new(720, 560);

    public static IslandSize Content(IslandVisualState state, bool workspace = false) => state switch
    {
        IslandVisualState.Compact => new IslandSize(248, 46),
        IslandVisualState.Expanded when workspace => Translation,
        IslandVisualState.Expanded => new IslandSize(424, 284),
        IslandVisualState.Notification => new IslandSize(400, 118),
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
    };

    public static IslandSize Outer(IslandVisualState state, bool workspace = false)
    {
        var content = Content(state, workspace);
        return new IslandSize(
            content.Width + (ShadowPadding * 2),
            content.Height + (ShadowPadding * 2));
    }

    /// <summary>
    /// Keeps the island inside the monitor working area on high-DPI or small
    /// screens. Aspect ratio is preserved; normal desktop sizes are unchanged.
    /// </summary>
    public static IslandSize FitWithin(IslandSize size, double maxWidth, double maxHeight)
    {
        if (!double.IsFinite(size.Width) || !double.IsFinite(size.Height)
            || size.Width <= 0 || size.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        if (!double.IsFinite(maxWidth) || !double.IsFinite(maxHeight))
        {
            return size;
        }

        var widthLimit = Math.Max(1.0, maxWidth);
        var heightLimit = Math.Max(1.0, maxHeight);
        var scale = Math.Min(1.0, Math.Min(widthLimit / size.Width, heightLimit / size.Height));
        return scale >= 1.0
            ? size
            : new IslandSize(size.Width * scale, size.Height * scale);
    }
}
