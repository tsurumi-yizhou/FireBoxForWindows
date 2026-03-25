using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Core.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace App.Views;

public sealed partial class RoutesPage : Page
{
    private List<ProviderDto> _providers = [];
    private List<RouteDto> _routes = [];

    public RoutesPage()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => RefreshRoutes();

    private async void AddRoute_Click(object sender, RoutedEventArgs e) =>
        await ShowRouteEditorDialogAsync(null);

    private void RefreshRoutes()
    {
        RouteCardsPanel.Children.Clear();

        if (App.Connection is null)
        {
            AddRouteButton.IsEnabled = false;
            RouteCardsPanel.Children.Add(ConfigurationUiHelpers.CreateInlineMessage("Connect to the service to manage routes."));
            ConfigurationUiHelpers.ShowStatus(StatusBar, InfoBarSeverity.Warning, "Not connected", "Connect to FireBox Service to manage routes.");
            return;
        }

        try
        {
            var providersJson = App.Connection.Control.ListProviders();
            _providers = JsonSerializer.Deserialize<List<ProviderDto>>(providersJson, ConfigurationUiHelpers.JsonOptions) ?? [];

            var routesJson = App.Connection.Control.ListRoutes();
            _routes = JsonSerializer.Deserialize<List<RouteDto>>(routesJson, ConfigurationUiHelpers.JsonOptions) ?? [];

            AddRouteButton.IsEnabled = GetSelectableProviders().Count > 0;

            foreach (var route in _routes.OrderBy(static route => route.VirtualModelId, StringComparer.OrdinalIgnoreCase))
            {
                RouteCardsPanel.Children.Add(CreateRouteCard(route));
            }

            if (_routes.Count == 0)
            {
                RouteCardsPanel.Children.Add(ConfigurationUiHelpers.CreateInlineMessage("No routes configured yet. Add one after enabling models on a provider."));
            }

            ConfigurationUiHelpers.ShowStatus(StatusBar, InfoBarSeverity.Success, "Routes loaded", $"Loaded {_routes.Count} route{(_routes.Count == 1 ? string.Empty : "s")}.");
        }
        catch (Exception ex)
        {
            RouteCardsPanel.Children.Add(ConfigurationUiHelpers.CreateInlineMessage($"Failed to load routes: {ex.Message}"));
            ConfigurationUiHelpers.ShowStatus(StatusBar, InfoBarSeverity.Error, "Couldn't load routes", ex.Message);
        }
    }

    private Border CreateRouteCard(RouteDto route)
    {
        var content = new StackPanel { Spacing = 14 };

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titlePanel = new StackPanel { Spacing = 4 };
        titlePanel.Children.Add(new TextBlock
        {
            Text = route.VirtualModelId,
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });
        titlePanel.Children.Add(new TextBlock
        {
            Text = $"Strategy: {FireBoxRouteStrategies.Normalize(route.Strategy)}",
            Foreground = ConfigurationUiHelpers.SecondaryBrush(),
        });
        Grid.SetColumn(titlePanel, 0);
        header.Children.Add(titlePanel);

        var editButton = new Button { Content = "Edit Route", Margin = new Thickness(0, 0, 8, 0) };
        editButton.Click += async (_, _) => await ShowRouteEditorDialogAsync(route);
        Grid.SetColumn(editButton, 1);
        header.Children.Add(editButton);

        var deleteButton = new Button { Content = "Delete" };
        deleteButton.Click += async (_, _) => await DeleteRouteAsync(route);
        Grid.SetColumn(deleteButton, 2);
        header.Children.Add(deleteButton);

        content.Children.Add(header);

        var capabilities = new List<string>();
        if (route.Reasoning) capabilities.Add("Reasoning");
        if (route.ToolCalling) capabilities.Add("Tool Calling");

        content.Children.Add(new TextBlock
        {
            Text = capabilities.Count == 0 ? "No extra capability flags enabled." : $"Capabilities: {string.Join(", ", capabilities)}",
            Foreground = ConfigurationUiHelpers.SecondaryBrush(),
            TextWrapping = TextWrapping.Wrap,
        });

        content.Children.Add(new TextBlock
        {
            Text = $"Input: {DescribeMediaFormats(route.InputFormatsMask)}",
            Foreground = ConfigurationUiHelpers.SecondaryBrush(),
            TextWrapping = TextWrapping.Wrap,
        });

        content.Children.Add(new TextBlock
        {
            Text = $"Output: {DescribeMediaFormats(route.OutputFormatsMask)}",
            Foreground = ConfigurationUiHelpers.SecondaryBrush(),
            TextWrapping = TextWrapping.Wrap,
        });

        if (route.Candidates.Count == 0)
        {
            content.Children.Add(ConfigurationUiHelpers.CreateInlineMessage("No target candidates configured."));
        }
        else
        {
            foreach (var candidate in route.Candidates)
            {
                content.Children.Add(new TextBlock
                {
                    Text = ResolveCandidateLabel(candidate),
                    TextWrapping = TextWrapping.Wrap,
                });
            }
        }

        return new Border
        {
            Style = (Style)Resources["CardStyle"],
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = content,
        };
    }

    private async Task ShowRouteEditorDialogAsync(RouteDto? existing)
    {
        if (App.Connection is null)
        {
            ConfigurationUiHelpers.ShowStatus(StatusBar, InfoBarSeverity.Warning, "Not connected", "Connect to FireBox Service before editing routes.");
            return;
        }

        var selectableProviders = GetSelectableProviders();
        if (selectableProviders.Count == 0)
        {
            await ConfigurationUiHelpers.ShowMessageDialogAsync(
                XamlRoot,
                "No enabled models yet",
                "Enable models on at least one provider before creating or editing routes.");
            ConfigurationUiHelpers.ShowStatus(StatusBar, InfoBarSeverity.Warning, "No enabled models", "Enable at least one model on a provider before configuring routes.");
            return;
        }

        var candidateStates = existing?.Candidates
            .Select(candidate => new CandidateEditorState
            {
                ProviderId = selectableProviders.Any(provider => provider.Id == candidate.ProviderId) ? candidate.ProviderId : null,
                ModelId = candidate.ModelId,
            })
            .ToList() ?? [];

        if (candidateStates.Count == 0)
            candidateStates.Add(new CandidateEditorState());

        var hasUnavailableCandidates = existing is not null && existing.Candidates.Any(candidate =>
            !selectableProviders.Any(provider =>
                provider.Id == candidate.ProviderId &&
                provider.EnabledModelIds.Contains(candidate.ModelId, StringComparer.OrdinalIgnoreCase)));

        var dialog = new ContentDialog
        {
            Title = existing is null ? "Add Route" : "Edit Route",
            PrimaryButtonText = existing is null ? "Add" : "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        ConfigurationUiHelpers.ApplyDialogWidth(dialog, ConfigurationUiHelpers.GetResponsiveDialogWidth(XamlRoot));

        var content = new StackPanel
        {
            Spacing = 16,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var errorText = new TextBlock
        {
            Foreground = new SolidColorBrush(Colors.IndianRed),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        content.Children.Add(errorText);

        if (hasUnavailableCandidates)
        {
            content.Children.Add(new TextBlock
            {
                Text = "Some saved targets are no longer available because their provider no longer exposes them as enabled models. Pick replacements before saving.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = ConfigurationUiHelpers.SecondaryBrush(),
            });
        }

        var virtualModelIdBox = new TextBox
        {
            Header = "Virtual Model ID",
            Text = existing?.VirtualModelId ?? string.Empty,
            PlaceholderText = "e.g. gpt-4o",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        content.Children.Add(virtualModelIdBox);

        var strategyBox = new ComboBox
        {
            Header = "Strategy",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        strategyBox.Items.Add(FireBoxRouteStrategies.Ordered);
        strategyBox.Items.Add(FireBoxRouteStrategies.Random);
        strategyBox.SelectedItem = existing is null ? null : FireBoxRouteStrategies.Normalize(existing.Strategy);
        content.Children.Add(strategyBox);

        var capabilitiesSection = new StackPanel
        {
            Spacing = 8,
        };
        capabilitiesSection.Children.Add(new TextBlock
        {
            Text = "Capabilities",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });

        var capabilitiesPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
        };
        var reasoningCheck = new CheckBox
        {
            Content = "Reasoning",
            IsChecked = existing?.Reasoning ?? false,
        };
        var toolCallingCheck = new CheckBox
        {
            Content = "Tool Calling",
            IsChecked = existing?.ToolCalling ?? false,
        };
        capabilitiesPanel.Children.Add(reasoningCheck);
        capabilitiesPanel.Children.Add(toolCallingCheck);
        capabilitiesSection.Children.Add(capabilitiesPanel);
        content.Children.Add(capabilitiesSection);

        var inputImageCheck = new CheckBox
        {
            Content = "Image",
            IsChecked = (((existing?.InputFormatsMask) ?? 0) & ModelMediaFormatMask.ImageBit) != 0,
        };
        var inputVideoCheck = new CheckBox
        {
            Content = "Video",
            IsChecked = (((existing?.InputFormatsMask) ?? 0) & ModelMediaFormatMask.VideoBit) != 0,
        };
        var inputAudioCheck = new CheckBox
        {
            Content = "Audio",
            IsChecked = (((existing?.InputFormatsMask) ?? 0) & ModelMediaFormatMask.AudioBit) != 0,
        };
        var inputFormatsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
        };
        inputFormatsPanel.Children.Add(inputImageCheck);
        inputFormatsPanel.Children.Add(inputVideoCheck);
        inputFormatsPanel.Children.Add(inputAudioCheck);

        var outputImageCheck = new CheckBox
        {
            Content = "Image",
            IsChecked = (((existing?.OutputFormatsMask) ?? 0) & ModelMediaFormatMask.ImageBit) != 0,
        };
        var outputVideoCheck = new CheckBox
        {
            Content = "Video",
            IsChecked = (((existing?.OutputFormatsMask) ?? 0) & ModelMediaFormatMask.VideoBit) != 0,
        };
        var outputAudioCheck = new CheckBox
        {
            Content = "Audio",
            IsChecked = (((existing?.OutputFormatsMask) ?? 0) & ModelMediaFormatMask.AudioBit) != 0,
        };
        var outputFormatsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
        };
        outputFormatsPanel.Children.Add(outputImageCheck);
        outputFormatsPanel.Children.Add(outputVideoCheck);
        outputFormatsPanel.Children.Add(outputAudioCheck);

        var inputSection = new StackPanel { Spacing = 8 };
        inputSection.Children.Add(new TextBlock
        {
            Text = "Multimodal Input",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });
        inputSection.Children.Add(inputFormatsPanel);
        content.Children.Add(inputSection);

        var outputSection = new StackPanel { Spacing = 8 };
        outputSection.Children.Add(new TextBlock
        {
            Text = "Multimodal Output",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });
        outputSection.Children.Add(outputFormatsPanel);
        content.Children.Add(outputSection);

        content.Children.Add(new TextBlock
        {
            Text = "Candidate Targets",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });

        var candidatesHost = new StackPanel
        {
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var candidatesScrollViewer = new ScrollViewer
        {
            Content = new Border
            {
                Padding = new Thickness(0, 0, 12, 0),
                Child = candidatesHost,
            },
            Height = 240,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Enabled,
        };
        content.Children.Add(candidatesScrollViewer);

        var addCandidateButton = new Button { Content = "Add Candidate" };
        ScrollViewer? dialogScrollViewer = null;
        addCandidateButton.Click += (_, _) =>
        {
            candidateStates.Add(new CandidateEditorState());
            RenderCandidates();
            DispatcherQueue.TryEnqueue(() =>
            {
                candidatesScrollViewer.UpdateLayout();
                candidatesScrollViewer.ChangeView(null, candidatesScrollViewer.ScrollableHeight, null, disableAnimation: false);
            });
        };
        content.Children.Add(addCandidateButton);

        dialogScrollViewer = new ScrollViewer
        {
            Content = new Border
            {
                Padding = new Thickness(0, 12, 16, 8),
                Child = content,
            },
            MaxHeight = 700,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Enabled,
        };
        dialog.Content = dialogScrollViewer;

        List<string> GetEnabledModels(int? providerId)
        {
            return selectableProviders
                .FirstOrDefault(provider => provider.Id == providerId)?
                .EnabledModelIds
                .OrderBy(static model => model, StringComparer.OrdinalIgnoreCase)
                .ToList() ?? [];
        }

        void RenderCandidates()
        {
            candidatesHost.Children.Clear();

            for (var index = 0; index < candidateStates.Count; index++)
            {
                var candidate = candidateStates[index];

                var cardContent = new StackPanel
                {
                    Spacing = 12,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };

                var cardHeader = new Grid
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                cardHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                cardHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var cardTitle = new TextBlock
                {
                    Text = $"Candidate {index + 1}",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(cardTitle, 0);
                cardHeader.Children.Add(cardTitle);

                var providerBox = new ComboBox
                {
                    Header = "Provider",
                    PlaceholderText = "Select provider",
                    MaxDropDownHeight = 320,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    ItemsSource = selectableProviders,
                };
                providerBox.SelectedItem = selectableProviders.FirstOrDefault(provider => provider.Id == candidate.ProviderId);

                var modelBox = new ComboBox
                {
                    Header = "Model",
                    PlaceholderText = "Select model",
                    MaxDropDownHeight = 320,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };

                void SyncModelBox(bool autoSelectFirstModel)
                {
                    candidate.ProviderId = (providerBox.SelectedItem as ProviderDto)?.Id;

                    var models = GetEnabledModels(candidate.ProviderId);
                    modelBox.ItemsSource = models;

                    if (candidate.ModelId is not null && models.Contains(candidate.ModelId, StringComparer.OrdinalIgnoreCase))
                    {
                        modelBox.SelectedItem = models.First(model => string.Equals(model, candidate.ModelId, StringComparison.OrdinalIgnoreCase));
                        return;
                    }

                    if (autoSelectFirstModel && models.Count > 0)
                    {
                        candidate.ModelId = models[0];
                        modelBox.SelectedItem = models[0];
                        return;
                    }

                    candidate.ModelId = null;
                    modelBox.SelectedIndex = -1;
                }

                providerBox.SelectionChanged += (_, _) => SyncModelBox(autoSelectFirstModel: true);
                modelBox.SelectionChanged += (_, _) => candidate.ModelId = modelBox.SelectedItem as string;

                SyncModelBox(autoSelectFirstModel: false);

                var removeButton = new Button
                {
                    Content = new SymbolIcon(Symbol.Cancel),
                    Width = 28,
                    Height = 28,
                    MinWidth = 28,
                    Padding = new Thickness(0),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top,
                    IsEnabled = candidateStates.Count > 1,
                };
                ToolTipService.SetToolTip(removeButton, "Remove candidate");
                removeButton.Click += (_, _) =>
                {
                    candidateStates.Remove(candidate);
                    if (candidateStates.Count == 0)
                        candidateStates.Add(new CandidateEditorState());
                    RenderCandidates();
                };
                Grid.SetColumn(removeButton, 1);
                cardHeader.Children.Add(removeButton);

                var candidateFieldsPanel = new StackPanel
                {
                    Spacing = 12,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                candidateFieldsPanel.Children.Add(providerBox);
                candidateFieldsPanel.Children.Add(modelBox);

                cardContent.Children.Add(cardHeader);
                cardContent.Children.Add(candidateFieldsPanel);

                candidatesHost.Children.Add(new Border
                {
                    Background = ConfigurationUiHelpers.CardBackgroundBrush(),
                    BorderBrush = ConfigurationUiHelpers.CardBorderBrush(),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(12),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Child = cardContent,
                });
            }
        }

        RenderCandidates();

        var saved = false;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            var virtualModelId = virtualModelIdBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(virtualModelId))
            {
                args.Cancel = true;
                errorText.Text = "Virtual model ID is required.";
                errorText.Visibility = Visibility.Visible;
                return;
            }

            if (candidateStates.Count == 0)
            {
                args.Cancel = true;
                errorText.Text = "Add at least one candidate target.";
                errorText.Visibility = Visibility.Visible;
                return;
            }

            var strategy = strategyBox.SelectedItem?.ToString();
            if (string.IsNullOrWhiteSpace(strategy))
            {
                args.Cancel = true;
                errorText.Text = "Strategy is required.";
                errorText.Visibility = Visibility.Visible;
                return;
            }

            try
            {
                var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var serializedTargets = new List<RouteCandidatePayload>();

                foreach (var candidate in candidateStates)
                {
                    if (candidate.ProviderId is null || string.IsNullOrWhiteSpace(candidate.ModelId))
                        throw new InvalidOperationException("Each candidate must have both a provider and a model.");

                    var targetKey = $"{candidate.ProviderId}:{candidate.ModelId}";
                    if (!seenTargets.Add(targetKey))
                        throw new InvalidOperationException("Duplicate candidate targets are not allowed.");

                    serializedTargets.Add(new RouteCandidatePayload(candidate.ProviderId.Value, candidate.ModelId));
                }

                var candidatesJson = JsonSerializer.Serialize(serializedTargets);
                var inputFormatsMask = BuildMediaMask(inputImageCheck, inputVideoCheck, inputAudioCheck);
                var outputFormatsMask = BuildMediaMask(outputImageCheck, outputVideoCheck, outputAudioCheck);

                if (existing is null)
                {
                    App.Connection.Control.AddRoute(
                        virtualModelId,
                        strategy,
                        candidatesJson,
                        reasoningCheck.IsChecked == true ? 1 : 0,
                        toolCallingCheck.IsChecked == true ? 1 : 0,
                        inputFormatsMask,
                        outputFormatsMask);
                }
                else
                {
                    App.Connection.Control.UpdateRoute(
                        existing.Id,
                        virtualModelId,
                        strategy,
                        candidatesJson,
                        reasoningCheck.IsChecked == true ? 1 : 0,
                        toolCallingCheck.IsChecked == true ? 1 : 0,
                        inputFormatsMask,
                        outputFormatsMask);
                }

                saved = true;
            }
            catch (Exception ex)
            {
                args.Cancel = true;
                errorText.Text = ex.Message;
                errorText.Visibility = Visibility.Visible;
            }
        };

        await dialog.ShowAsync();

        if (saved)
        {
            RefreshRoutes();
            ConfigurationUiHelpers.ShowStatus(StatusBar, InfoBarSeverity.Success, existing is null ? "Route added" : "Route updated", existing is null ? "Route saved successfully." : $"Route '{existing.VirtualModelId}' updated successfully.");
        }
    }

    private async Task DeleteRouteAsync(RouteDto route)
    {
        if (App.Connection is null)
        {
            ConfigurationUiHelpers.ShowStatus(StatusBar, InfoBarSeverity.Warning, "Not connected", "Connect to FireBox Service before deleting routes.");
            return;
        }

        var confirmed = await new ContentDialog
        {
            Title = "Delete Route",
            Content = "This virtual model will stop resolving immediately.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        }.ShowAsync();

        if (confirmed != ContentDialogResult.Primary)
            return;

        try
        {
            App.Connection.Control.DeleteRoute(route.Id);
            RefreshRoutes();
            ConfigurationUiHelpers.ShowStatus(StatusBar, InfoBarSeverity.Success, "Route deleted", $"Deleted route '{route.VirtualModelId}'.");
        }
        catch (Exception ex)
        {
            await ConfigurationUiHelpers.ShowMessageDialogAsync(XamlRoot, "Couldn't delete route", ex.Message);
            ConfigurationUiHelpers.ShowStatus(StatusBar, InfoBarSeverity.Error, "Couldn't delete route", ex.Message);
        }
    }

    private List<ProviderDto> GetSelectableProviders()
    {
        return _providers
            .Where(provider => provider.EnabledModelIds.Count > 0)
            .OrderBy(static provider => provider.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private string ResolveCandidateLabel(RouteCandidateDto candidate)
    {
        var provider = _providers.FirstOrDefault(item => item.Id == candidate.ProviderId);
        var providerLabel = provider?.DisplayName ?? $"Provider #{candidate.ProviderId}";
        return $"{providerLabel} -> {candidate.ModelId}";
    }

    private static int BuildMediaMask(CheckBox imageCheck, CheckBox videoCheck, CheckBox audioCheck)
    {
        var mask = 0;
        if (imageCheck.IsChecked == true) mask |= ModelMediaFormatMask.ImageBit;
        if (videoCheck.IsChecked == true) mask |= ModelMediaFormatMask.VideoBit;
        if (audioCheck.IsChecked == true) mask |= ModelMediaFormatMask.AudioBit;
        return mask;
    }

    private static string DescribeMediaFormats(int mask)
    {
        var formats = new List<string>();
        if ((mask & ModelMediaFormatMask.ImageBit) != 0) formats.Add("Image");
        if ((mask & ModelMediaFormatMask.VideoBit) != 0) formats.Add("Video");
        if ((mask & ModelMediaFormatMask.AudioBit) != 0) formats.Add("Audio");
        return formats.Count == 0 ? "Text only" : string.Join(", ", formats);
    }

    private sealed class CandidateEditorState
    {
        public int? ProviderId { get; set; }
        public string? ModelId { get; set; }
    }

    private sealed record RouteCandidatePayload(int ProviderId, string ModelId);
}
