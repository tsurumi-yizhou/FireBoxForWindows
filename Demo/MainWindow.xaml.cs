using System;
using System.Linq;
using Demo.Models;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace Demo;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ConfigureTitleBar();
        Activated += MainWindow_Activated;
    }

    public void UpdateStatus()
    {
        if (App.ConnectionError is not null)
        {
            ChatPageView.UpdateClient(null);
            ShowStatus(InfoBarSeverity.Error, "Service connection failed", App.ConnectionError);
            ChatPageView.IsEnabled = false;
            NewChatButton.IsEnabled = false;
        }
        else if (App.Client is null)
        {
            ChatPageView.UpdateClient(null);
            ShowStatus(InfoBarSeverity.Warning, "Not connected", "FireBox client not initialized.");
            ChatPageView.IsEnabled = false;
            NewChatButton.IsEnabled = false;
        }
        else
        {
            ChatPageView.UpdateClient(App.Client);
            HideStatus();
            ChatPageView.IsEnabled = true;
            NewChatButton.IsEnabled = true;
            NewChat();
        }
    }

    public void ShowFatalError(string title, string message)
    {
        ShowStatus(InfoBarSeverity.Error, title, "FireBox Demo ran into an exception.");
        ConversationList.IsEnabled = false;
        ChatPageView.IsEnabled = false;
        NewChatButton.IsEnabled = false;
        Content = CreateErrorView(title, message);
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

    private void NewChat_Click(object sender, RoutedEventArgs e) => NewChat();

    private void NewChat()
    {
        var conversation = new Conversation();
        ChatPageView.ViewModel.AddConversation(conversation);
        RefreshConversationList();
        ConversationList.SelectedItem = ConversationList.Items.Cast<Conversation>()
            .FirstOrDefault(c => c.Id == conversation.Id);
    }

    private void ConversationList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ConversationList.SelectedItem is Conversation conv)
            ChatPageView.ViewModel.SwitchConversation(conv.Id);
    }

    private void DeleteConversation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            ChatPageView.ViewModel.DeleteConversation(id);
            RefreshConversationList();

            if (ChatPageView.ViewModel.Conversations.Count == 0)
                NewChat();
            else
                ConversationList.SelectedIndex = 0;
        }
    }

    private void RefreshConversationList()
    {
        ConversationList.ItemsSource = null;
        ConversationList.ItemsSource = ChatPageView.ViewModel.Conversations;
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated)
        {
            ChatPageView.ReloadModels();
        }
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
