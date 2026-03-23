using System;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ConfigureTitleBar();
        // Don't call UpdateStatus() here - it will be called from App.OnLaunched after connection is established
    }

    public void UpdateStatus()
    {
        if (App.ConnectionError is not null)
        {
            ShowStatus(InfoBarSeverity.Error, "Service connection failed", App.ConnectionError);
            NavView.IsEnabled = false;
        }
        else if (App.Connection is null)
        {
            ShowStatus(InfoBarSeverity.Warning, "Not connected", "Service connection not initialized.");
            NavView.IsEnabled = false;
        }
        else
        {
            HideStatus();
            NavView.IsEnabled = true;
            if (!ContentFrame.Navigate(typeof(Views.DashboardPage)))
            {
                ShowFatalError("Failed to load dashboard", "The dashboard page could not be created.");
                return;
            }

            NavView.SelectedItem = NavView.MenuItems[0];
        }
    }

    public void ShowFatalError(string title, string message)
    {
        ShowStatus(InfoBarSeverity.Error, title, "FireBox ran into an exception.");
        NavView.IsEnabled = false;
        NavView.SelectedItem = null;
        ContentFrame.Content = CreateErrorView(title, message);
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer is NavigationViewItem item && item.Tag is string tag)
        {
            var pageType = tag switch
            {
                "Dashboard" => typeof(Views.DashboardPage),
                "Connections" => typeof(Views.ConnectionsPage),
                "Providers" => typeof(Views.ProvidersPage),
                "Routes" => typeof(Views.RoutesPage),
                "Allowlist" => typeof(Views.AllowlistPage),
                _ => null,
            };
            if (pageType is not null && !ContentFrame.Navigate(pageType))
            {
                ShowFatalError("Failed to navigate", $"The page '{tag}' could not be loaded.");
            }
        }
    }

    private void ConfigureTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragRegion);

        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        if (!AppWindowTitleBar.IsCustomizationSupported())
            return;

        var titleBar = appWindow.TitleBar;
        titleBar.ExtendsContentIntoTitleBar = true;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
    }

    private void ShowStatus(InfoBarSeverity severity, string title, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Title = title;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }

    private void HideStatus()
    {
        StatusBar.IsOpen = false;
        StatusBar.Title = string.Empty;
        StatusBar.Message = string.Empty;
    }

    private static UIElement CreateErrorView(string title, string message)
    {
        var panel = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(24),
        };

        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 28,
            TextWrapping = TextWrapping.Wrap,
        });

        panel.Children.Add(new TextBlock
        {
            Text = "Exception details",
            FontSize = 16,
        });

        panel.Children.Add(new TextBox
        {
            Text = message,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 220,
        });

        return new ScrollViewer
        {
            Content = panel,
        };
    }
}
