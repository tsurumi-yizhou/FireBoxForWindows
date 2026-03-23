using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace App.Views;

public sealed partial class AllowlistPage : Page
{
    private bool _suppressToggleEvents;

    public AllowlistPage()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => Refresh();

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => Refresh();

    private void Refresh()
    {
        if (App.Connection is null)
        {
            AccessList.ItemsSource = null;
            ConfigurationUiHelpers.ShowStatus(StatusBar, InfoBarSeverity.Warning, "Not connected", "Connect to FireBox Service to manage allowlist entries.");
            return;
        }

        try
        {
            var json = App.Connection.Control.ListClientAccess();
            var records = JsonSerializer.Deserialize<List<ClientAccessDto>>(json, ConfigurationUiHelpers.JsonOptions) ?? [];
            AccessList.ItemsSource = records.ConvertAll(r => new AccessViewModel
            {
                Id = r.Id,
                ProcessName = r.ProcessName,
                ExecutablePath = r.ExecutablePath,
                RequestCountText = $"{r.RequestCount} requests",
                LastSeenText = r.LastSeenAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                IsAllowed = r.IsAllowed,
            });
            ConfigurationUiHelpers.ShowStatus(StatusBar, InfoBarSeverity.Success, "Allowlist loaded", $"Loaded {records.Count} client entr{(records.Count == 1 ? "y" : "ies")}.");
        }
        catch (Exception ex)
        {
            AccessList.ItemsSource = null;
            ConfigurationUiHelpers.ShowStatus(StatusBar, InfoBarSeverity.Error, "Couldn't load allowlist", ex.Message);
        }
    }

    private void AllowToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggleEvents)
            return;

        if (sender is ToggleSwitch toggle && toggle.Tag is int id)
        {
            try
            {
                App.Connection.Control.UpdateClientAccessAllowed(id, toggle.IsOn ? 1 : 0);
                ConfigurationUiHelpers.ShowStatus(StatusBar, InfoBarSeverity.Success, "Allowlist updated", toggle.IsOn ? "Client allowed." : "Client blocked.");
            }
            catch (Exception ex)
            {
                _suppressToggleEvents = true;
                toggle.IsOn = !toggle.IsOn;
                _suppressToggleEvents = false;
                ConfigurationUiHelpers.ShowStatus(StatusBar, InfoBarSeverity.Error, "Failed to update allowlist", ex.Message);
            }
        }
    }

    private sealed record ClientAccessDto(int Id, int ProcessId, string ProcessName, string ExecutablePath, long RequestCount, DateTimeOffset FirstSeenAt, DateTimeOffset LastSeenAt, bool IsAllowed);

    private sealed class AccessViewModel
    {
        public int Id { get; init; }
        public string ProcessName { get; init; } = string.Empty;
        public string ExecutablePath { get; init; } = string.Empty;
        public string RequestCountText { get; init; } = string.Empty;
        public string LastSeenText { get; init; } = string.Empty;
        public bool IsAllowed { get; set; }
    }
}
