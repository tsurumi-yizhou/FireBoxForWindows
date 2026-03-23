using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace App.Views;

public sealed partial class ProvidersPage : Page
{
    private List<ProviderDto> _providers = [];

    public ProvidersPage()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => RefreshProviders();

    private async void AddProvider_Click(object sender, RoutedEventArgs e) =>
        await ShowProviderEditorDialogAsync(null);

    private void RefreshProviders()
    {
        ProviderCardsPanel.Children.Clear();

        if (App.Connection is null)
        {
            ProviderCardsPanel.Children.Add(ConfigurationUiHelpers.CreateInlineMessage("Connect to the service to manage providers."));
            ConfigurationUiHelpers.ShowStatus(StatusBar, InfoBarSeverity.Warning, "Not connected", "Connect to FireBox Service to manage providers.");
            return;
        }

        try
        {
            var json = App.Connection.Control.ListProviders();
            _providers = JsonSerializer.Deserialize<List<ProviderDto>>(json, ConfigurationUiHelpers.JsonOptions) ?? [];

            foreach (var provider in _providers.OrderBy(static provider => provider.Name, StringComparer.OrdinalIgnoreCase))
            {
                ProviderCardsPanel.Children.Add(CreateProviderCard(provider));
            }

            if (_providers.Count == 0)
            {
                ProviderCardsPanel.Children.Add(ConfigurationUiHelpers.CreateInlineMessage("No providers configured yet. Add one to start enabling models."));
            }

            ConfigurationUiHelpers.ShowStatus(StatusBar, InfoBarSeverity.Success, "Providers loaded", $"Loaded {_providers.Count} provider{(_providers.Count == 1 ? string.Empty : "s")}.");
        }
        catch (Exception ex)
        {
            ProviderCardsPanel.Children.Add(ConfigurationUiHelpers.CreateInlineMessage($"Failed to load providers: {ex.Message}"));
            ConfigurationUiHelpers.ShowStatus(StatusBar, InfoBarSeverity.Error, "Couldn't load providers", ex.Message);
        }
    }

    private Border CreateProviderCard(ProviderDto provider)
    {
        var content = new StackPanel { Spacing = 14 };

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titlePanel = new StackPanel { Spacing = 4 };
        titlePanel.Children.Add(new TextBlock
        {
            Text = provider.Name,
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });
        titlePanel.Children.Add(new TextBlock
        {
            Text = provider.ProviderType,
            Foreground = ConfigurationUiHelpers.SecondaryBrush(),
        });
        Grid.SetColumn(titlePanel, 0);
        header.Children.Add(titlePanel);

        var editButton = new Button { Content = "Edit Provider", Margin = new Thickness(0, 0, 8, 0) };
        editButton.Click += async (_, _) => await ShowProviderEditorDialogAsync(provider);
        Grid.SetColumn(editButton, 1);
        header.Children.Add(editButton);

        var modelsButton = new Button { Content = "Enable Models", Margin = new Thickness(0, 0, 8, 0) };
        modelsButton.Click += async (_, _) => await ShowEnableModelsDialogAsync(provider);
        Grid.SetColumn(modelsButton, 2);
        header.Children.Add(modelsButton);

        var deleteButton = new Button { Content = "Delete" };
        deleteButton.Click += async (_, _) => await DeleteProviderAsync(provider);
        Grid.SetColumn(deleteButton, 3);
        header.Children.Add(deleteButton);

        content.Children.Add(header);
        content.Children.Add(new TextBlock
        {
            Text = $"Base URL: {provider.BaseUrlLabel}",
            TextWrapping = TextWrapping.Wrap,
            Foreground = ConfigurationUiHelpers.SecondaryBrush(),
        });
        content.Children.Add(new TextBlock
        {
            Text = BuildEnabledModelsSummary(provider.EnabledModelIds),
            TextWrapping = TextWrapping.Wrap,
        });

        return new Border
        {
            Style = (Style)Resources["CardStyle"],
            Child = content,
        };
    }

    private async Task ShowProviderEditorDialogAsync(ProviderDto? existing)
    {
        if (App.Connection is null)
        {
            ConfigurationUiHelpers.ShowStatus(StatusBar, InfoBarSeverity.Warning, "Not connected", "Connect to FireBox Service before editing providers.");
            return;
        }

        var dialog = new ContentDialog
        {
            Title = existing is null ? "Add Provider" : "Edit Provider",
            PrimaryButtonText = existing is null ? "Add" : "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        var form = new StackPanel
        {
            Spacing = 12,
            MinWidth = 420,
        };

        var statusText = new TextBlock
        {
            Foreground = new SolidColorBrush(Colors.IndianRed),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        form.Children.Add(statusText);

        ComboBox? providerTypeBox = null;
        if (existing is null)
        {
            providerTypeBox = new ComboBox
            {
                Header = "Provider Type",
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            providerTypeBox.Items.Add("OpenAI");
            providerTypeBox.Items.Add("Anthropic");
            providerTypeBox.Items.Add("Gemini");
            providerTypeBox.SelectedIndex = 0;
            form.Children.Add(providerTypeBox);
        }
        else
        {
            form.Children.Add(new TextBlock
            {
                Text = $"Provider Type: {existing.ProviderType}",
                Foreground = ConfigurationUiHelpers.SecondaryBrush(),
            });
        }

        var nameBox = new TextBox
        {
            Header = "Display Name",
            Text = existing?.Name ?? string.Empty,
            PlaceholderText = "e.g. Team OpenAI",
        };
        form.Children.Add(nameBox);

        var urlBox = new TextBox
        {
            Header = "Base URL",
            Text = existing?.BaseUrl ?? string.Empty,
            PlaceholderText = "Leave empty to use the provider default",
        };
        form.Children.Add(urlBox);

        var apiKeyBox = new PasswordBox
        {
            Header = "API Key",
            PlaceholderText = existing is null ? "Required" : "Leave blank to keep the current key",
        };
        form.Children.Add(apiKeyBox);

        dialog.Content = form;

        var saved = false;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            var name = nameBox.Text.Trim();
            var baseUrl = urlBox.Text.Trim();
            var apiKey = apiKeyBox.Password.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                args.Cancel = true;
                statusText.Text = "Display name is required.";
                statusText.Visibility = Visibility.Visible;
                return;
            }

            if (existing is null && string.IsNullOrWhiteSpace(apiKey))
            {
                args.Cancel = true;
                statusText.Text = "API key is required when creating a provider.";
                statusText.Visibility = Visibility.Visible;
                return;
            }

            try
            {
                if (existing is null)
                {
                    App.Connection.Control.AddProvider(
                        providerTypeBox?.SelectedItem?.ToString() ?? "OpenAI",
                        name,
                        baseUrl,
                        apiKey);
                }
                else
                {
                    App.Connection.Control.UpdateProvider(
                        existing.Id,
                        name,
                        baseUrl,
                        apiKey,
                        string.Empty,
                        1);
                }

                saved = true;
            }
            catch (Exception ex)
            {
                args.Cancel = true;
                statusText.Text = ex.Message;
                statusText.Visibility = Visibility.Visible;
            }
        };

        await dialog.ShowAsync();

        if (saved)
        {
            RefreshProviders();
            ConfigurationUiHelpers.ShowStatus(StatusBar, InfoBarSeverity.Success, existing is null ? "Provider added" : "Provider updated", existing is null ? "Provider saved successfully." : $"Provider '{existing.Name}' updated successfully.");
        }
    }

    private async Task ShowEnableModelsDialogAsync(ProviderDto provider)
    {
        if (App.Connection is null)
        {
            ConfigurationUiHelpers.ShowStatus(StatusBar, InfoBarSeverity.Warning, "Not connected", "Connect to FireBox Service before enabling models.");
            return;
        }

        List<string> allModels;
        try
        {
            var json = App.Connection.Control.FetchProviderModels(provider.Id);
            var fetchedModels = JsonSerializer.Deserialize<List<string>>(json, ConfigurationUiHelpers.JsonOptions) ?? [];
            allModels = fetchedModels
                .Concat(provider.EnabledModelIds)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static model => model, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            await ConfigurationUiHelpers.ShowMessageDialogAsync(XamlRoot, "Couldn't fetch models", ex.Message);
            ConfigurationUiHelpers.ShowStatus(StatusBar, InfoBarSeverity.Error, "Couldn't fetch models", ex.Message);
            return;
        }

        var selectedModels = new HashSet<string>(provider.EnabledModelIds, StringComparer.OrdinalIgnoreCase);

        var dialog = new ContentDialog
        {
            Title = $"Enable Models for {provider.Name}",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        var content = new StackPanel
        {
            Spacing = 12,
            MinWidth = 500,
        };

        var statusText = new TextBlock
        {
            Foreground = new SolidColorBrush(Colors.IndianRed),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        content.Children.Add(statusText);

        var searchBox = new TextBox
        {
            Header = "Search Models",
            PlaceholderText = "Type part of a model name",
        };
        content.Children.Add(searchBox);

        var summaryText = new TextBlock
        {
            Foreground = ConfigurationUiHelpers.SecondaryBrush(),
            TextWrapping = TextWrapping.Wrap,
        };
        content.Children.Add(summaryText);

        var modelsHost = new StackPanel { Spacing = 4 };
        content.Children.Add(new ScrollViewer
        {
            Content = modelsHost,
            MinHeight = 340,
            MaxHeight = 420,
        });

        void RenderModels()
        {
            modelsHost.Children.Clear();

            var query = searchBox.Text.Trim();
            var filtered = string.IsNullOrWhiteSpace(query)
                ? allModels
                : allModels.Where(model => model.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

            summaryText.Text = $"{selectedModels.Count} enabled / {allModels.Count} available";

            if (filtered.Count == 0)
            {
                modelsHost.Children.Add(ConfigurationUiHelpers.CreateInlineMessage("No models match the current search."));
                return;
            }

            foreach (var model in filtered)
            {
                var checkbox = new CheckBox
                {
                    Content = model,
                    IsChecked = selectedModels.Contains(model),
                };

                checkbox.Checked += (_, _) =>
                {
                    selectedModels.Add(model);
                    summaryText.Text = $"{selectedModels.Count} enabled / {allModels.Count} available";
                };
                checkbox.Unchecked += (_, _) =>
                {
                    selectedModels.Remove(model);
                    summaryText.Text = $"{selectedModels.Count} enabled / {allModels.Count} available";
                };

                modelsHost.Children.Add(checkbox);
            }
        }

        searchBox.TextChanged += (_, _) => RenderModels();
        RenderModels();
        dialog.Content = content;

        var saved = false;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try
            {
                var enabledModelsJson = JsonSerializer.Serialize(
                    selectedModels.OrderBy(static model => model, StringComparer.OrdinalIgnoreCase).ToList());

                App.Connection.Control.UpdateProvider(
                    provider.Id,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    enabledModelsJson,
                    1);

                saved = true;
            }
            catch (Exception ex)
            {
                args.Cancel = true;
                statusText.Text = ex.Message;
                statusText.Visibility = Visibility.Visible;
            }
        };

        await dialog.ShowAsync();

        if (saved)
        {
            RefreshProviders();
            ConfigurationUiHelpers.ShowStatus(StatusBar, InfoBarSeverity.Success, "Model allowlist updated", $"Enabled models updated for '{provider.Name}'.");
        }
    }

    private async Task DeleteProviderAsync(ProviderDto provider)
    {
        if (App.Connection is null)
        {
            ConfigurationUiHelpers.ShowStatus(StatusBar, InfoBarSeverity.Warning, "Not connected", "Connect to FireBox Service before deleting providers.");
            return;
        }

        var confirmed = await new ContentDialog
        {
            Title = "Delete Provider",
            Content = "Deleting a provider also invalidates any route targets that point at it.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        }.ShowAsync();

        if (confirmed != ContentDialogResult.Primary)
            return;

        try
        {
            App.Connection.Control.DeleteProvider(provider.Id);
            RefreshProviders();
            ConfigurationUiHelpers.ShowStatus(StatusBar, InfoBarSeverity.Success, "Provider deleted", $"Deleted provider '{provider.Name}'.");
        }
        catch (Exception ex)
        {
            await ConfigurationUiHelpers.ShowMessageDialogAsync(XamlRoot, "Couldn't delete provider", ex.Message);
            ConfigurationUiHelpers.ShowStatus(StatusBar, InfoBarSeverity.Error, "Couldn't delete provider", ex.Message);
        }
    }

    private static string BuildEnabledModelsSummary(IReadOnlyCollection<string> enabledModels)
    {
        if (enabledModels.Count == 0)
            return "No models enabled yet.";

        var preview = enabledModels
            .OrderBy(static model => model, StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();

        var suffix = enabledModels.Count > preview.Count ? ", ..." : string.Empty;
        return $"{enabledModels.Count} enabled model(s): {string.Join(", ", preview)}{suffix}";
    }
}
