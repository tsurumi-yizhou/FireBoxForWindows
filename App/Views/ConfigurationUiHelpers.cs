using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace App.Views;

internal static class ConfigurationUiHelpers
{
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static Brush SecondaryBrush() =>
        (Brush)App.Current.Resources["TextFillColorSecondaryBrush"];

    public static Brush CardBackgroundBrush() =>
        (Brush)App.Current.Resources["CardBackgroundFillColorDefaultBrush"];

    public static Brush CardBorderBrush() =>
        (Brush)App.Current.Resources["CardStrokeColorDefaultBrush"];

    public static TextBlock CreateInlineMessage(string text) =>
        new()
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = SecondaryBrush(),
        };

    public static double GetResponsiveDialogWidth(
        XamlRoot? xamlRoot,
        double minWidth,
        double preferredFraction = 0.68,
        double maxFraction = 0.75,
        double edgePadding = 96)
    {
        var rootWidth = xamlRoot?.Size.Width ?? 1600;
        var maxWidth = Math.Max(minWidth, Math.Min(rootWidth * maxFraction, rootWidth - edgePadding));
        return Math.Max(minWidth, Math.Min(rootWidth * preferredFraction, maxWidth));
    }

    public static async Task ShowMessageDialogAsync(XamlRoot xamlRoot, string title, string message)
    {
        await new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "Close",
            XamlRoot = xamlRoot,
        }.ShowAsync();
    }

    public static void ShowStatus(InfoBar statusBar, InfoBarSeverity severity, string title, string message)
    {
        statusBar.Severity = severity;
        statusBar.Title = title;
        statusBar.Message = message;
        statusBar.IsOpen = true;
    }
}
