using System.ComponentModel;
using System.Threading.Tasks;
using Demo.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Demo.Views;

public sealed partial class ChatPage : UserControl
{
    public ChatViewModel ViewModel { get; }
    private int _feedbackVersion;

    public ChatPage()
    {
        ViewModel = new ChatViewModel(App.Client, DispatcherQueue.GetForCurrentThread());
        InitializeComponent();
        ModelSelector.ItemsSource = ViewModel.AvailableModels;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.SetClient(App.Client);
        ReloadModels();
        UpdateFeedbackBar();
    }

    public void UpdateClient(Client.FireBoxClient? client)
    {
        ViewModel.SetClient(client);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChatViewModel.FeedbackMessage) or nameof(ChatViewModel.FeedbackSeverity))
            UpdateFeedbackBar();
    }

    private void ModelSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModelSelector.SelectedItem is string modelId)
            ViewModel.SelectedModelId = modelId;
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        await SendAsync();
    }

    private async void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && ViewModel.CanSend)
        {
            e.Handled = true;
            await SendAsync();
        }
    }

    private async Task SendAsync()
    {
        if (ViewModel.SendCommand.CanExecute(null))
        {
            await ViewModel.SendCommand.ExecuteAsync(null);
            ScrollToBottom();
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearChatCommand.Execute(null);
    }

    private void RefreshModels_Click(object sender, RoutedEventArgs e)
    {
        ReloadModels();
    }

    public void ReloadModels()
    {
        ViewModel.LoadModels();

        if (!string.IsNullOrWhiteSpace(ViewModel.SelectedModelId) &&
            ViewModel.AvailableModels.Contains(ViewModel.SelectedModelId))
        {
            ModelSelector.SelectedItem = ViewModel.SelectedModelId;
            return;
        }

        if (ViewModel.AvailableModels.Count > 0)
        {
            ModelSelector.SelectedIndex = 0;
            return;
        }

        ModelSelector.SelectedIndex = -1;
    }

    private void ScrollToBottom()
    {
        if (ViewModel.Messages.Count > 0)
        {
            MessageList.ScrollIntoView(ViewModel.Messages[^1]);
        }
    }

    private void FeedbackBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        if (!string.IsNullOrWhiteSpace(ViewModel.FeedbackMessage))
            ViewModel.FeedbackMessage = null;
    }

    private void UpdateFeedbackBar()
    {
        if (string.IsNullOrWhiteSpace(ViewModel.FeedbackMessage))
        {
            _feedbackVersion++;
            FeedbackBar.IsOpen = false;
            FeedbackBar.Title = string.Empty;
            FeedbackBar.Message = string.Empty;
            return;
        }

        var version = ++_feedbackVersion;
        FeedbackBar.Severity = ViewModel.FeedbackSeverity;
        FeedbackBar.Title = ViewModel.FeedbackSeverity switch
        {
            InfoBarSeverity.Success => "Success",
            InfoBarSeverity.Warning => "Notice",
            InfoBarSeverity.Error => "Request failed",
            _ => "Info",
        };
        FeedbackBar.Message = ViewModel.FeedbackMessage;
        FeedbackBar.IsOpen = true;

        if (ViewModel.FeedbackSeverity is InfoBarSeverity.Success or InfoBarSeverity.Informational)
            _ = AutoDismissFeedbackAsync(version);
    }

    private async Task AutoDismissFeedbackAsync(int version)
    {
        await Task.Delay(3000);
        if (version != _feedbackVersion)
            return;

        if (ViewModel.FeedbackSeverity is InfoBarSeverity.Success or InfoBarSeverity.Informational)
            ViewModel.FeedbackMessage = null;
    }
}
