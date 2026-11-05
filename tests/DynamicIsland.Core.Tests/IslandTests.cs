using DynamicIsland.Core.Layout;
using DynamicIsland.Core.Models;
using DynamicIsland.Core.Services;

namespace DynamicIsland.Core.Tests;

public sealed class IslandMetricsTests
{
    [Fact]
    public void Outer_AddsShadowPaddingOnBothAxes()
    {
        var content = IslandMetrics.Content(IslandVisualState.Compact);
        var outer = IslandMetrics.Outer(IslandVisualState.Compact);

        Assert.Equal(content.Width + (IslandMetrics.ShadowPadding * 2), outer.Width);
        Assert.Equal(content.Height + (IslandMetrics.ShadowPadding * 2), outer.Height);
    }

    [Fact]
    public void Expanded_IsLargerThanCompact()
    {
        var compact = IslandMetrics.Content(IslandVisualState.Compact);
        var expanded = IslandMetrics.Content(IslandVisualState.Expanded);

        Assert.True(expanded.Width > compact.Width);
        Assert.True(expanded.Height > compact.Height);
    }

    [Fact]
    public void Notification_IsShorterThanExpanded()
    {
        var notification = IslandMetrics.Content(IslandVisualState.Notification);
        var expanded = IslandMetrics.Content(IslandVisualState.Expanded);

        Assert.Equal(118, notification.Height);
        Assert.True(notification.Height < expanded.Height);
    }

    [Fact]
    public void FitWithin_ScalesWorkspaceToSmallWorkingArea()
    {
        var workspace = IslandMetrics.Outer(IslandVisualState.Expanded, workspace: true);
        var fitted = IslandMetrics.FitWithin(workspace, maxWidth: 900, maxHeight: 520);

        Assert.True(fitted.Width <= 900);
        Assert.True(fitted.Height <= 520);
        Assert.InRange(
            fitted.Width / fitted.Height,
            (workspace.Width / workspace.Height) - 0.001,
            (workspace.Width / workspace.Height) + 0.001);
    }

    [Fact]
    public void Placement_CentersWindowAndHonorsTopMargin()
    {
        var position = IslandPlacement.TopCenter(
            workAreaLeft: 1920,
            workAreaTop: 40,
            workAreaWidth: 2560,
            windowWidth: 452,
            topMargin: 8);

        Assert.Equal(1920 + ((2560 - 452) / 2.0), position.Left);
        Assert.Equal(48, position.Top);
    }

    [Fact]
    public void Placement_ClampsTopMargin()
    {
        var position = IslandPlacement.TopCenter(0, 0, 1920, 276, 500);
        Assert.Equal(80, position.Top);
    }
}

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "DynamicIsland.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void SaveThenLoad_RoundTripsAllSettings()
    {
        var path = Path.Combine(_directory, "settings.json");
        var store = new SettingsStore(path);
        var settings = new IslandSettings
        {
            StartWithWindows = true,
            AlwaysOnTop = false,
            AnimationsEnabled = false,
            ShowSeconds = false,
            Opacity = 0.82,
            TopMargin = 17,
            PreferredScreen = @"\\.\DISPLAY2",
            WebCalendarUrl = "https://outlook.office365.com/owa/calendar/example/calendar.ics"
        };

        Assert.True(store.TrySave(settings, out var error), error);

        var loaded = store.Load();
        Assert.True(loaded.StartWithWindows);
        Assert.False(loaded.AlwaysOnTop);
        Assert.False(loaded.AnimationsEnabled);
        Assert.False(loaded.ShowSeconds);
        Assert.Equal(0.82, loaded.Opacity, 3);
        Assert.Equal(17, loaded.TopMargin, 3);
        Assert.Equal(@"\\.\DISPLAY2", loaded.PreferredScreen);
        Assert.Equal("https://outlook.office365.com/owa/calendar/example/calendar.ics", loaded.WebCalendarUrl);
    }

    [Fact]
    public void Load_InvalidJson_FallsBackToDefaults()
    {
        var path = Path.Combine(_directory, "settings.json");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, "{ this is not json");

        var settings = new SettingsStore(path).Load();

        Assert.True(settings.AlwaysOnTop);
        Assert.True(settings.AnimationsEnabled);
        Assert.Equal(1.0, settings.Opacity, 3);
    }

    [Fact]
    public void Load_MissingFile_UsesDefaults()
    {
        var settings = new SettingsStore(Path.Combine(_directory, "missing.json")).Load();
        Assert.False(settings.StartWithWindows);
        Assert.Equal(0.0, settings.TopMargin, 3);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Test cleanup is best effort.
        }
    }
}

public sealed class IslandSettingsTests
{
    [Fact]
    public void Normalize_ClampsOpacityAndMargin()
    {
        var settings = new IslandSettings
        {
            Opacity = 4.2,
            TopMargin = -20
        };

        settings.Normalize();

        Assert.Equal(1.0, settings.Opacity, 3);
        Assert.Equal(0.0, settings.TopMargin, 3);
    }

    [Fact]
    public void Clone_IsIndependent()
    {
        var original = new IslandSettings { Opacity = 0.9 };
        var clone = original.Clone();

        clone.Opacity = 0.7;

        Assert.Equal(0.9, original.Opacity, 3);
    }
}
