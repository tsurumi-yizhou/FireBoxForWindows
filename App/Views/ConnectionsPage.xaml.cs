using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace App.Views;

public sealed partial class ConnectionsPage : Page
{
    public ConnectionsPage()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => _ = RefreshAsync();

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        if (App.Connection is null)
        {
            TotalCount.Text = "0";
            ConnectionList.ItemsSource = null;
            ConfigurationUiHelpers.ShowStatus(StatusBar, InfoBarSeverity.Warning, "Not connected", "Connect to FireBox Service to view active connections.");
            return;
        }

        RefreshButton.IsEnabled = false;
        LoadingRing.IsActive = true;
        ConnectionList.Visibility = Visibility.Collapsed;
        EmptyStateView.Visibility = Visibility.Collapsed;

        try
        {
            var json = App.Connection.Control.ListConnections();
            var connections = JsonSerializer.Deserialize<List<ConnectionDto>>(json, ConfigurationUiHelpers.JsonOptions) ?? [];
            
            TotalCount.Text = connections.Count.ToString();
            ConnectionList.ItemsSource = connections.ConvertAll(c => new ConnectionViewModel
            {
                ProcessName = c.ProcessName,
                ExecutablePath = c.ExecutablePath,
                RequestCountText = $"{c.RequestCount} requests",
                ConnectedAtText = c.ConnectedAt.ToLocalTime().ToString("t"),
            });
            
            if (connections.Count == 0)
            {
                EmptyStateView.Visibility = Visibility.Visible;
            }
            else
            {
                ConnectionList.Visibility = Visibility.Visible;
            }

            ConfigurationUiHelpers.ShowStatus(StatusBar, InfoBarSeverity.Success, "Connections loaded", $"Loaded {connections.Count} active connection{(connections.Count == 1 ? "" : "s")}.");
        }
        catch (Exception ex)
        {
            TotalCount.Text = "—";
            ConnectionList.ItemsSource = null;
            EmptyStateView.Visibility = Visibility.Collapsed;
            ConfigurationUiHelpers.ShowStatus(StatusBar, InfoBarSeverity.Error, "Couldn't load connections", ex.Message);
        }
        finally
        {
            LoadingRing.IsActive = false;
            RefreshButton.IsEnabled = true;
        }
    }

    private sealed record ConnectionDto(long ConnectionId, int ProcessId, string ProcessName, string ExecutablePath, DateTimeOffset ConnectedAt, long RequestCount);

    private sealed class ConnectionViewModel
    {
        public string ProcessName { get; init; } = string.Empty;
        public string ExecutablePath { get; init; } = string.Empty;
        public string RequestCountText { get; init; } = string.Empty;
        public string ConnectedAtText { get; init; } = string.Empty;
    }
}
