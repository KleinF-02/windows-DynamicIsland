using System.Reflection;
using DynamicIsland.Core.Models;

namespace DynamicIsland.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly IslandSettings _draft;

    public SettingsViewModel(IslandSettings current)
    {
        _draft = current.Clone();
    }

    public bool StartWithWindows
    {
        get => _draft.StartWithWindows;
        set
        {
            if (_draft.StartWithWindows == value)
            {
                return;
            }

            _draft.StartWithWindows = value;
            OnPropertyChanged();
        }
    }

    public bool AlwaysOnTop
    {
        get => _draft.AlwaysOnTop;
        set
        {
            if (_draft.AlwaysOnTop == value)
            {
                return;
            }

            _draft.AlwaysOnTop = value;
            OnPropertyChanged();
        }
    }

    public bool AnimationsEnabled
    {
        get => _draft.AnimationsEnabled;
        set
        {
            if (_draft.AnimationsEnabled == value)
            {
                return;
            }

            _draft.AnimationsEnabled = value;
            OnPropertyChanged();
        }
    }

    public bool ShowSeconds
    {
        get => _draft.ShowSeconds;
        set
        {
            if (_draft.ShowSeconds == value)
            {
                return;
            }

            _draft.ShowSeconds = value;
            OnPropertyChanged();
        }
    }

    public double Opacity
    {
        get => _draft.Opacity;
        set
        {
            var clamped = Math.Clamp(value, 0.60, 1.00);
            if (Math.Abs(_draft.Opacity - clamped) < 0.001)
            {
                return;
            }

            _draft.Opacity = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OpacityLabel));
        }
    }

    public string OpacityLabel => $"{_draft.Opacity:P0}";

    public double TopMargin
    {
        get => _draft.TopMargin;
        set
        {
            var clamped = Math.Clamp(value, 0, 40);
            if (Math.Abs(_draft.TopMargin - clamped) < 0.001)
            {
                return;
            }

            _draft.TopMargin = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TopMarginLabel));
        }
    }

    public string TopMarginLabel => _draft.TopMargin <= 0.01 ? "贴顶" : $"{_draft.TopMargin:0} DIP";

    public string WebCalendarUrl
    {
        get => _draft.WebCalendarUrl;
        set
        {
            if (string.Equals(_draft.WebCalendarUrl, value, StringComparison.Ordinal))
            {
                return;
            }

            _draft.WebCalendarUrl = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

    public string LocalLlmEndpoint
    {
        get => _draft.LocalLlmEndpoint;
        set
        {
            if (string.Equals(_draft.LocalLlmEndpoint, value, StringComparison.Ordinal))
            {
                return;
            }

            _draft.LocalLlmEndpoint = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

    public string LocalLlmModel
    {
        get => _draft.LocalLlmModel;
        set
        {
            if (string.Equals(_draft.LocalLlmModel, value, StringComparison.Ordinal))
            {
                return;
            }

            _draft.LocalLlmModel = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

    public string AgentApiEndpoint
    {
        get => _draft.AgentApiEndpoint;
        set
        {
            if (string.Equals(_draft.AgentApiEndpoint, value, StringComparison.Ordinal))
            {
                return;
            }

            _draft.AgentApiEndpoint = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

    public string AgentApiModel
    {
        get => _draft.AgentApiModel;
        set
        {
            if (string.Equals(_draft.AgentApiModel, value, StringComparison.Ordinal))
            {
                return;
            }

            _draft.AgentApiModel = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

    public string AgentApiKey
    {
        get => _draft.AgentApiKey;
        set
        {
            if (string.Equals(_draft.AgentApiKey, value, StringComparison.Ordinal))
            {
                return;
            }

            _draft.AgentApiKey = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

    public string VersionText
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version is null ? "v0.1.0" : $"v{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    public IslandSettings BuildSettings()
    {
        _draft.Normalize();
        return _draft.Clone();
    }
}
