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

    public static IslandSize Content(IslandVisualState state, bool translationWorkspace = false) => state switch
    {
        IslandVisualState.Compact => new IslandSize(248, 46),
        IslandVisualState.Expanded when translationWorkspace => Translation,
        IslandVisualState.Expanded => new IslandSize(424, 284),
        IslandVisualState.Notification => new IslandSize(400, 118),
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
    };

    public static IslandSize Outer(IslandVisualState state, bool translationWorkspace = false)
    {
        var content = Content(state, translationWorkspace);
        return new IslandSize(
            content.Width + (ShadowPadding * 2),
            content.Height + (ShadowPadding * 2));
    }
}
