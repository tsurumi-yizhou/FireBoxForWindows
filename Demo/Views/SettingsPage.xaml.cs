using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Resources;

namespace Demo.Views;

public sealed partial class SettingsPage : UserControl
{
    private readonly ResourceLoader _resourceLoader;

    public SettingsPage()
    {
        InitializeComponent();
        _resourceLoader = ResourceLoader.GetForViewIndependentUse();
        ApplyLocalizedStrings();
        SyncSectionVisibility(SettingsSelectorBar.SelectedItem as SelectorBarItem);
    }

    public bool FocusPrimaryControl()
    {
        if (SettingsSelectorBar.Focus(FocusState.Programmatic))
            return true;

        return RootScrollViewer.Focus(FocusState.Programmatic);
    }

    private void ApplyLocalizedStrings()
    {
        GeneralSelectorItem.Text = GetRequiredString("SettingsTabGeneral");
        AboutSelectorItem.Text = GetRequiredString("SettingsTabAbout");

        GeneralSectionExpander.Header = GetRequiredString("SettingsSectionGeneral");
        ReasoningSectionExpander.Header = GetRequiredString("SettingsSectionReasoning");
        ComposerSectionExpander.Header = GetRequiredString("SettingsSectionComposer");

        ModelAutoRefreshCard.Header = GetRequiredString("SettingsModelAutoRefreshHeader");
        ModelAutoRefreshCard.Description = GetRequiredString("SettingsModelAutoRefreshDescription");
        ThinkingIntensityCard.Header = GetRequiredString("SettingsThinkingIntensityHeader");
        ThinkingIntensityCard.Description = GetRequiredString("SettingsThinkingIntensityDescription");
        CompactComposerCard.Header = GetRequiredString("SettingsCompactComposerHeader");
        CompactComposerCard.Description = GetRequiredString("SettingsCompactComposerDescription");

        ThinkingDefaultOption.Content = GetRequiredString("SettingsThinkingDefaultOption");
        ThinkingLowOption.Content = GetRequiredString("SettingsThinkingLowOption");
        ThinkingMediumOption.Content = GetRequiredString("SettingsThinkingMediumOption");
        ThinkingHighOption.Content = GetRequiredString("SettingsThinkingHighOption");
        ThinkingMaxOption.Content = GetRequiredString("SettingsThinkingMaxOption");

        AboutAppCard.Header = GetRequiredString("SettingsAboutAppHeader");
        AboutAppCard.Description = GetRequiredString("SettingsAboutAppDescription");
        AboutAppNameValueText.Text = GetRequiredString("MainWindowTitle");
        AboutVersionLabelText.Text = GetRequiredString("SettingsAboutVersionLabel");
        AboutVersionValueText.Text = GetAppVersionText();
    }

    private void SettingsSelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        SyncSectionVisibility(sender.SelectedItem as SelectorBarItem);
    }

    private void SyncSectionVisibility(SelectorBarItem? selectedItem)
    {
        var showAbout = ReferenceEquals(selectedItem, AboutSelectorItem);
        GeneralContentPanel.Visibility = showAbout ? Visibility.Collapsed : Visibility.Visible;
        AboutContentPanel.Visibility = showAbout ? Visibility.Visible : Visibility.Collapsed;
    }

    private string GetRequiredString(string key)
    {
        var value = _resourceLoader.GetString(key);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Missing required string resource '{key}'.");

        return value;
    }

    private static string GetAppVersionText()
    {
        var version = Package.Current.Id.Version;
        return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
    }
}
