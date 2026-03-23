using System;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace App.Views;

public sealed partial class DashboardPage : Page
{
    public DashboardPage()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LoadStats();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        LoadStats();
    }

    private void LoadStats()
    {
        if (App.Connection is null)
        {
            ConfigurationUiHelpers.ShowStatus(StatusBar, InfoBarSeverity.Warning, "Not connected", "Connect to FireBox Service to load dashboard stats.");
            return;
        }

        try
        {
            var control = App.Connection.Control;
            var now = DateTime.UtcNow;

            var todayJson = control.GetDailyStats(now.Year, now.Month, now.Day);
            var today = JsonSerializer.Deserialize<StatsDto>(todayJson, ConfigurationUiHelpers.JsonOptions);
            if (today is not null)
            {
                TodayRequests.Text = today.RequestCount.ToString("N0");
                TodayTokens.Text = today.TotalTokens.ToString("N0");
                TodayCost.Text = $"${today.EstimatedCostUsd:F4}";
            }

            var monthJson = control.GetMonthlyStats(now.Year, now.Month);
            var month = JsonSerializer.Deserialize<StatsDto>(monthJson, ConfigurationUiHelpers.JsonOptions);
            if (month is not null)
            {
                MonthRequests.Text = month.RequestCount.ToString("N0");
                MonthTokens.Text = month.TotalTokens.ToString("N0");
                MonthCost.Text = $"${month.EstimatedCostUsd:F4}";
            }

            ConfigurationUiHelpers.ShowStatus(StatusBar, InfoBarSeverity.Success, "Dashboard updated", $"Statistics refreshed at {DateTime.Now:HH:mm:ss}.");
        }
        catch (Exception ex)
        {
            ConfigurationUiHelpers.ShowStatus(StatusBar, InfoBarSeverity.Error, "Failed to load dashboard", ex.Message);
        }
    }

    private sealed record StatsDto(long RequestCount, long PromptTokens, long CompletionTokens, long TotalTokens, decimal EstimatedCostUsd);
}
