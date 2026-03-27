using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Demo.Models;
using Demo.Search;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.Resources;
using WinRT.Interop;

namespace Demo;

public sealed partial class MainWindow : Window
{
    private const int MaxSearchSuggestions = 8;
    private const int SnippetLength = 72;
    private readonly ResourceLoader _resourceLoader;
    private readonly ConversationSearchService _searchService;
    private IReadOnlyList<ConversationSearchSuggestion> _searchSuggestions = Array.Empty<ConversationSearchSuggestion>();
    private string _searchBoxUserInput = string.Empty;

    public MainWindow(ConversationSearchService searchService)
    {
        ArgumentNullException.ThrowIfNull(searchService);
        InitializeComponent();
        _resourceLoader = ResourceLoader.GetForViewIndependentUse();
        _searchService = searchService;
        ApplyLocalizedStrings();
        ConversationNavView.MenuItemsSource = ChatPageView.ViewModel.Conversations;
        ChatPageView.ViewModel.Conversations.CollectionChanged += Conversations_CollectionChanged;
        ChatPageView.ViewModel.Messages.CollectionChanged += ActiveConversationMessages_CollectionChanged;
        foreach (var conversation in ChatPageView.ViewModel.Conversations)
            conversation.PropertyChanged += Conversation_PropertyChanged;
        foreach (var message in ChatPageView.ViewModel.Messages)
            message.PropertyChanged += ActiveConversationMessage_PropertyChanged;

        ConfigureTitleBar();
        LoadingOverlay.Visibility = Visibility.Visible;
        RefreshSearchSuggestions(string.Empty);
    }

    public void UpdateStatus()
    {
        LoadingOverlay.Visibility = Visibility.Collapsed;

        if (App.ConnectionError is not null)
        {
            ChatPageView.UpdateClient(null);
            ShowStatus(InfoBarSeverity.Error, "Service connection failed", App.ConnectionError);
            ChatPageView.IsEnabled = false;
            SettingsPageView.IsEnabled = false;
            ConversationNavView.IsEnabled = false;
            NewChatButton.IsEnabled = false;
            StatusBar.Focus(FocusState.Programmatic);
        }
        else if (App.Client is null)
        {
            ChatPageView.UpdateClient(null);
            ShowStatus(InfoBarSeverity.Warning, "Not connected", "FireBox client not initialized.");
            ChatPageView.IsEnabled = false;
            SettingsPageView.IsEnabled = false;
            ConversationNavView.IsEnabled = false;
            NewChatButton.IsEnabled = false;
            StatusBar.Focus(FocusState.Programmatic);
        }
        else
        {
            ChatPageView.UpdateClient(App.Client);
            HideStatus();
            ChatPageView.IsEnabled = true;
            SettingsPageView.IsEnabled = true;
            NewChatButton.IsEnabled = true;
            ConversationNavView.IsEnabled = true;

            if (ChatPageView.ViewModel.Conversations.Count == 0)
                NewChat();
            else if (ConversationNavView.SelectedItem is null)
                ConversationNavView.SelectedItem = ChatPageView.ViewModel.Conversations.FirstOrDefault();

            if (SettingsPageView.Visibility == Visibility.Visible)
                ShowSettingsView();
            else
                ShowChatView();

            ConversationNavView.Focus(FocusState.Programmatic);
        }
    }

    public void ShowFatalError(string title, string message)
    {
        ShowStatus(InfoBarSeverity.Error, title, "FireChatBox ran into an exception.");
        ConversationNavView.IsEnabled = false;
        ChatPageView.IsEnabled = false;
        SettingsPageView.IsEnabled = false;
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
        ConversationNavView.SelectedItem = ChatPageView.ViewModel.Conversations
            .FirstOrDefault(c => c.Id == conversation.Id);
        ShowChatView();
        RefreshSearchSuggestions(TitleBarSearchBox.Text);
    }

    private void ConversationNavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            ShowSettingsView();
            return;
        }

        if (args.SelectedItem is Conversation conv)
        {
            ChatPageView.ViewModel.SwitchConversation(conv.Id);
            ShowChatView();
            return;
        }
    }

    private void DeleteConversationMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem menuItem && menuItem.Tag is string id)
        {
            ChatPageView.ViewModel.DeleteConversation(id);

            if (ChatPageView.ViewModel.Conversations.Count == 0)
                NewChat();
            else
                ConversationNavView.SelectedItem = ChatPageView.ViewModel.Conversations.FirstOrDefault();

            RefreshSearchSuggestions(TitleBarSearchBox.Text);
        }
    }

    private void TitleBarSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
            return;

        _searchBoxUserInput = sender.Text;
        RefreshSearchSuggestions(sender.Text);
        sender.IsSuggestionListOpen = _searchSuggestions.Count > 0;
    }

    private void TitleBarSearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        // Keep user-entered text; selecting a suggestion should not auto-fill the box.
        if (!string.Equals(sender.Text, _searchBoxUserInput, StringComparison.Ordinal))
            sender.Text = _searchBoxUserInput;
    }

    private void TitleBarSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var chosen = args.ChosenSuggestion as ConversationSearchSuggestion;
        if (chosen is null)
        {
            RefreshSearchSuggestions(args.QueryText);
            chosen = _searchSuggestions.FirstOrDefault();
        }

        if (chosen is null)
            return;

        var conversation = ChatPageView.ViewModel.Conversations.FirstOrDefault(item =>
            string.Equals(item.Id, chosen.ConversationId, StringComparison.Ordinal));
        if (conversation is null)
            return;

        ShowChatView();
        ConversationNavView.SelectedItem = conversation;
        ChatPageView.ViewModel.SwitchConversation(conversation.Id);
        sender.Text = _searchBoxUserInput;
        sender.IsSuggestionListOpen = false;
    }

    private void Conversations_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<Conversation>())
                item.PropertyChanged += Conversation_PropertyChanged;
        }

        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems.OfType<Conversation>())
                item.PropertyChanged -= Conversation_PropertyChanged;
        }

        RefreshSearchSuggestions(TitleBarSearchBox.Text);
    }

    private void ActiveConversationMessages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (var message in e.NewItems.OfType<ChatUiMessage>())
                message.PropertyChanged += ActiveConversationMessage_PropertyChanged;
        }

        if (e.OldItems is not null)
        {
            foreach (var message in e.OldItems.OfType<ChatUiMessage>())
                message.PropertyChanged -= ActiveConversationMessage_PropertyChanged;
        }

        RefreshSearchSuggestions(TitleBarSearchBox.Text);
    }

    private void ActiveConversationMessage_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(ChatUiMessage.Content), StringComparison.Ordinal))
            return;

        RefreshSearchSuggestions(TitleBarSearchBox.Text);
    }

    private void Conversation_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(Conversation.Title), StringComparison.Ordinal))
            return;

        RefreshSearchSuggestions(TitleBarSearchBox.Text);
    }

    private void RefreshSearchSuggestions(string? queryText)
    {
        var results = _searchService.Search(
            ChatPageView.ViewModel.Conversations,
            queryText,
            MaxSearchSuggestions,
            SnippetLength);

        _searchSuggestions = results
            .Select(result => new ConversationSearchSuggestion(
                result.ConversationId,
                result.Title,
                result.Subtitle))
            .ToList();
        TitleBarSearchBox.ItemsSource = _searchSuggestions;
    }

    private void ShowChatView()
    {
        ChatPageView.Visibility = Visibility.Visible;
        SettingsPageView.Visibility = Visibility.Collapsed;
        CollapseTitleBarSearchSuggestions();
        DispatcherQueue.TryEnqueue(() => ConversationNavView.Focus(FocusState.Programmatic));
    }

    private void ShowSettingsView()
    {
        ChatPageView.Visibility = Visibility.Collapsed;
        SettingsPageView.Visibility = Visibility.Visible;
        CollapseTitleBarSearchSuggestions();
        DispatcherQueue.TryEnqueue(() => SettingsPageView.FocusPrimaryControl());
    }

    private void CollapseTitleBarSearchSuggestions()
    {
        TitleBarSearchBox.IsSuggestionListOpen = false;
    }

    private void ApplyLocalizedStrings()
    {
        Title = GetRequiredString("MainWindowTitle");
        TitleBarAppNameText.Text = GetRequiredString("TitleBarAppName");
        TitleBarSearchBox.PlaceholderText = GetRequiredString("TitleBarSearchPlaceholder");
        LoadingStatusText.Text = GetRequiredString("LoadingOverlayConnectingText");
    }

    private string GetRequiredString(string key)
    {
        var value = _resourceLoader.GetString(key);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Missing required string resource '{key}'.");

        return value;
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

    private sealed record ConversationSearchSuggestion(string ConversationId, string Title, string Subtitle);
}
