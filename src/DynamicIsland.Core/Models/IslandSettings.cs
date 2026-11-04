namespace DynamicIsland.Core.Models;

/// <summary>
/// User-facing configuration. The class is deliberately serializable and UI-agnostic.
/// </summary>
public sealed class IslandSettings
{
    public int SchemaVersion { get; set; } = 1;

    public bool StartWithWindows { get; set; }

    public bool AlwaysOnTop { get; set; } = true;

    public bool AnimationsEnabled { get; set; } = true;

    public bool ShowSeconds { get; set; } = true;

    /// <summary>Window opacity in the range 0.60 - 1.00.</summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>
    /// Gap between the working-area top and the visible capsule, in DIPs.
    /// Zero means the capsule is flush with the top edge.
    /// </summary>
    public double TopMargin { get; set; } = 0.0;

    /// <summary>
    /// Device name of the preferred monitor (for example <c>\\.\DISPLAY1</c>).
    /// Empty means the primary monitor.
    /// </summary>
    public string PreferredScreen { get; set; } = string.Empty;

    public IslandSettings Clone() => (IslandSettings)MemberwiseClone();

    /// <summary>
    /// Repairs values that can be produced by hand-edited or older settings files.
    /// </summary>
    public void Normalize()
    {
        SchemaVersion = Math.Max(1, SchemaVersion);

        if (!double.IsFinite(Opacity))
        {
            Opacity = 1.0;
        }

        Opacity = Math.Clamp(Opacity, 0.60, 1.00);

        if (!double.IsFinite(TopMargin))
        {
            TopMargin = 0.0;
        }

        TopMargin = Math.Clamp(TopMargin, 0.0, 80.0);
        PreferredScreen ??= string.Empty;
    }
}
