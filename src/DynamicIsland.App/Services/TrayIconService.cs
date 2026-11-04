using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace DynamicIsland.App.Services;

/// <summary>
/// Owns the notification-area icon and its dark context menu.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _startupItem;
    private readonly Icon _icon;
    private readonly bool _ownsIcon;
    private bool _disposed;

    public TrayIconService(bool startWithWindows)
    {
        var loaded = LoadAppIcon();
        _icon = loaded.Icon;
        _ownsIcon = loaded.OwnsIcon;

        _startupItem = new ToolStripMenuItem("开机自动启动")
        {
            CheckOnClick = true,
            Checked = startWithWindows
        };
        _startupItem.Click += (_, _) => StartWithWindowsChanged?.Invoke(_startupItem.Checked);

        _menu = new ContextMenuStrip
        {
            BackColor = Color.FromArgb(22, 24, 32),
            ForeColor = Color.FromArgb(246, 247, 251),
            Font = new Font("Segoe UI", 9.25f),
            ShowImageMargin = false,
            ShowCheckMargin = true,
            Padding = new Padding(4),
            Renderer = new ToolStripProfessionalRenderer(new DarkMenuColorTable())
        };

        _menu.Items.Add(CreateItem("显示灵动岛", () => ShowRequested?.Invoke(this, EventArgs.Empty)));
        _menu.Items.Add(CreateItem("打开设置…", () => SettingsRequested?.Invoke(this, EventArgs.Empty)));
        _menu.Items.Add(CreateItem("播放演示通知", () => NotificationRequested?.Invoke(this, EventArgs.Empty)));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_startupItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(CreateItem("退出灵动岛", () => ExitRequested?.Invoke(this, EventArgs.Empty)));

        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "灵动岛 · Dynamic Island",
            Visible = true,
            ContextMenuStrip = _menu
        };
        _notifyIcon.DoubleClick += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? ShowRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler? NotificationRequested;

    public event EventHandler? ExitRequested;

    public event Action<bool>? StartWithWindowsChanged;

    public bool StartWithWindows
    {
        set
        {
            if (_startupItem.Checked != value)
            {
                _startupItem.Checked = value;
            }
        }
    }

    public void ShowBalloon(string title, string message)
    {
        if (_disposed)
        {
            return;
        }

        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.ShowBalloonTip(2500);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip = null;
        _notifyIcon.Dispose();
        _menu.Dispose();

        if (_ownsIcon)
        {
            _icon.Dispose();
        }
    }

    private static ToolStripMenuItem CreateItem(string text, Action onClick)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => onClick();
        return item;
    }

    private static (Icon Icon, bool OwnsIcon) LoadAppIcon()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute));

            if (resource?.Stream is not null)
            {
                using var stream = resource.Stream;
                using var source = new Icon(stream);
                return ((Icon)source.Clone(), true);
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UriFormatException)
        {
            AppLogger.Error("Unable to load the application icon; using the system default.", ex);
        }

        return (SystemIcons.Application, false);
    }

    private sealed class DarkMenuColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.FromArgb(22, 24, 32);

        public override Color ImageMarginGradientBegin => Color.FromArgb(22, 24, 32);

        public override Color ImageMarginGradientMiddle => Color.FromArgb(22, 24, 32);

        public override Color ImageMarginGradientEnd => Color.FromArgb(22, 24, 32);

        public override Color MenuBorder => Color.FromArgb(48, 255, 255, 255);

        public override Color MenuItemBorder => Color.FromArgb(36, 255, 255, 255);

        public override Color MenuItemSelected => Color.FromArgb(36, 255, 255, 255);

        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(36, 255, 255, 255);

        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(36, 255, 255, 255);

        public override Color SeparatorDark => Color.FromArgb(28, 255, 255, 255);

        public override Color SeparatorLight => Color.FromArgb(16, 255, 255, 255);
    }
}
