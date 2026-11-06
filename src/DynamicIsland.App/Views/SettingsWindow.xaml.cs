using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DynamicIsland.App.Interop;
using DynamicIsland.App.ViewModels;
using DynamicIsland.Core.Models;
using Button = System.Windows.Controls.Button;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace DynamicIsland.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(IslandSettings settings)
    {
        InitializeComponent();
        _viewModel = new SettingsViewModel(settings);
        DataContext = _viewModel;
        AgentApiKeyBox.Password = _viewModel.AgentApiKey;
    }

    public event Action<IslandSettings>? SettingsSaved;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeMethods.UseDarkWindowChrome(new WindowInteropHelper(this).Handle);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        SettingsSaved?.Invoke(_viewModel.BuildSettings());
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void AgentApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _viewModel.AgentApiKey = AgentApiKeyBox.Password;
    }

    private void AgentProvider_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string provider })
        {
            _viewModel.SelectAgentProvider(provider);
            e.Handled = true;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
