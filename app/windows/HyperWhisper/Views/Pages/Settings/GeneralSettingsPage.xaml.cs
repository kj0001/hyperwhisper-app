// GENERAL SETTINGS PAGE
// Handles general application settings including startup and window behavior.

using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using HyperWhisper.Localization;
using HyperWhisper.Services;

namespace HyperWhisper.Views.Pages.Settings;

public partial class GeneralSettingsPage : Page
{
    public GeneralSettingsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        InitializeSettings();
    }

    /// <summary>
    /// Initializes all settings with current values.
    /// </summary>
    private void InitializeSettings()
    {
        // Load launch at startup state from registry.
        // #1471: on an isolated app-data profile the shared HKCU Run value
        // belongs to the installed app, so this build never reads or changes
        // it. The box shows unticked and disabled, with a note saying why.
        var startupAvailable = StartupService.IsAvailable;
        var startupEnabled = startupAvailable && StartupService.Instance.IsEnabled;
        LaunchAtStartupCheckbox.Checked -= LaunchAtStartupCheckbox_Checked;
        LaunchAtStartupCheckbox.Unchecked -= LaunchAtStartupCheckbox_Unchecked;
        LaunchAtStartupCheckbox.IsChecked = startupEnabled;
        LaunchAtStartupCheckbox.IsEnabled = startupAvailable;
        LaunchAtStartupIsolatedNote.Visibility = startupAvailable ? Visibility.Collapsed : Visibility.Visible;
        if (startupAvailable)
        {
            LaunchAtStartupCheckbox.Checked += LaunchAtStartupCheckbox_Checked;
            LaunchAtStartupCheckbox.Unchecked += LaunchAtStartupCheckbox_Unchecked;
        }

        // Load launch minimized state from settings
        LaunchMinimizedCheckbox.Checked -= LaunchMinimizedCheckbox_Checked;
        LaunchMinimizedCheckbox.Unchecked -= LaunchMinimizedCheckbox_Unchecked;
        LaunchMinimizedCheckbox.IsChecked = SettingsService.Instance.LaunchMinimized;
        LaunchMinimizedCheckbox.Checked += LaunchMinimizedCheckbox_Checked;
        LaunchMinimizedCheckbox.Unchecked += LaunchMinimizedCheckbox_Unchecked;

        // Load minimize to tray state from settings
        MinimizeToTrayCheckbox.Checked -= MinimizeToTrayCheckbox_Checked;
        MinimizeToTrayCheckbox.Unchecked -= MinimizeToTrayCheckbox_Unchecked;
        MinimizeToTrayCheckbox.IsChecked = SettingsService.Instance.MinimizeToTray;
        MinimizeToTrayCheckbox.Checked += MinimizeToTrayCheckbox_Checked;
        MinimizeToTrayCheckbox.Unchecked += MinimizeToTrayCheckbox_Unchecked;

        // Load show recording window state from settings
        ShowRecordingWindowCheckbox.Checked -= ShowRecordingWindowCheckbox_Checked;
        ShowRecordingWindowCheckbox.Unchecked -= ShowRecordingWindowCheckbox_Unchecked;
        ShowRecordingWindowCheckbox.IsChecked = SettingsService.Instance.ShowRecordingWindow;
        ShowRecordingWindowCheckbox.Checked += ShowRecordingWindowCheckbox_Checked;
        ShowRecordingWindowCheckbox.Unchecked += ShowRecordingWindowCheckbox_Unchecked;

        // Load opt-in app context sharing state from settings
        SendAppContextCheckbox.Checked -= SendAppContextCheckbox_Checked;
        SendAppContextCheckbox.Unchecked -= SendAppContextCheckbox_Unchecked;
        SendAppContextCheckbox.IsChecked = SettingsService.Instance.SendAppContextToPostProcessing;
        SendAppContextCheckbox.Checked += SendAppContextCheckbox_Checked;
        SendAppContextCheckbox.Unchecked += SendAppContextCheckbox_Unchecked;

        // Load anonymous speed data state from settings
        ShareSpeedDataCheckbox.Checked -= ShareSpeedDataCheckbox_Checked;
        ShareSpeedDataCheckbox.Unchecked -= ShareSpeedDataCheckbox_Unchecked;
        ShareSpeedDataCheckbox.IsChecked = SettingsService.Instance.ShareAnonymousSpeedData;
        ShareSpeedDataCheckbox.Checked += ShareSpeedDataCheckbox_Checked;
        ShareSpeedDataCheckbox.Unchecked += ShareSpeedDataCheckbox_Unchecked;

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var shortVersion = version?.ToString(3) ?? "Unknown";
        var buildVersion = version?.Revision.ToString() ?? "0";
        VersionText.Text = Loc.S("settings.version.detail", shortVersion, buildVersion);

        LoggingService.Debug($"GeneralSettingsPage: Initialized (startup={startupEnabled}, startupAvailable={startupAvailable}, launchMinimized={SettingsService.Instance.LaunchMinimized}, minimizeToTray={SettingsService.Instance.MinimizeToTray}, showRecordingWindow={SettingsService.Instance.ShowRecordingWindow}, shareSpeedData={SettingsService.Instance.ShareAnonymousSpeedData})");
    }

    // =========================================================================
    // LAUNCH AT STARTUP
    // =========================================================================

    private void LaunchAtStartupCheckbox_Checked(object sender, RoutedEventArgs e)
    {
        if (!StartupService.IsAvailable)
        {
            return;
        }

        var success = StartupService.Instance.Enable();
        if (!success)
        {
            LaunchAtStartupCheckbox.Checked -= LaunchAtStartupCheckbox_Checked;
            LaunchAtStartupCheckbox.IsChecked = false;
            LaunchAtStartupCheckbox.Checked += LaunchAtStartupCheckbox_Checked;

            WpfMessageBox.Show(
                Loc.S("settings.general.startup.enableFailed"),
                Loc.S("common.error"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        else
        {
            LoggingService.Info("GeneralSettingsPage: Enabled launch at startup");
        }
    }

    private void LaunchAtStartupCheckbox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (!StartupService.IsAvailable)
        {
            return;
        }

        var success = StartupService.Instance.Disable();
        if (!success)
        {
            LaunchAtStartupCheckbox.Unchecked -= LaunchAtStartupCheckbox_Unchecked;
            LaunchAtStartupCheckbox.IsChecked = true;
            LaunchAtStartupCheckbox.Unchecked += LaunchAtStartupCheckbox_Unchecked;

            WpfMessageBox.Show(
                Loc.S("settings.general.startup.disableFailed"),
                Loc.S("common.error"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        else
        {
            LoggingService.Info("GeneralSettingsPage: Disabled launch at startup");
        }
    }

    // =========================================================================
    // LAUNCH MINIMIZED
    // =========================================================================

    private void LaunchMinimizedCheckbox_Checked(object sender, RoutedEventArgs e)
    {
        SettingsService.Instance.LaunchMinimized = true;
        LoggingService.Info("GeneralSettingsPage: Enabled launch minimized");
    }

    private void LaunchMinimizedCheckbox_Unchecked(object sender, RoutedEventArgs e)
    {
        SettingsService.Instance.LaunchMinimized = false;
        LoggingService.Info("GeneralSettingsPage: Disabled launch minimized");
    }

    // =========================================================================
    // MINIMIZE TO TRAY
    // =========================================================================

    private void MinimizeToTrayCheckbox_Checked(object sender, RoutedEventArgs e)
    {
        SettingsService.Instance.MinimizeToTray = true;
        LoggingService.Info("GeneralSettingsPage: Enabled minimize to tray");
    }

    private void MinimizeToTrayCheckbox_Unchecked(object sender, RoutedEventArgs e)
    {
        SettingsService.Instance.MinimizeToTray = false;
        LoggingService.Info("GeneralSettingsPage: Disabled minimize to tray");
    }

    // =========================================================================
    // SHOW RECORDING WINDOW
    // =========================================================================

    private void ShowRecordingWindowCheckbox_Checked(object sender, RoutedEventArgs e)
    {
        SettingsService.Instance.ShowRecordingWindow = true;
        LoggingService.Info("GeneralSettingsPage: Enabled show recording window");
    }

    private void ShowRecordingWindowCheckbox_Unchecked(object sender, RoutedEventArgs e)
    {
        SettingsService.Instance.ShowRecordingWindow = false;
        LoggingService.Info("GeneralSettingsPage: Disabled show recording window");
    }

    private void SendAppContextCheckbox_Checked(object sender, RoutedEventArgs e)
    {
        SettingsService.Instance.SendAppContextToPostProcessing = true;
        LoggingService.Info("GeneralSettingsPage: Enabled sending active app context to post-processing");
    }

    private void SendAppContextCheckbox_Unchecked(object sender, RoutedEventArgs e)
    {
        SettingsService.Instance.SendAppContextToPostProcessing = false;
        LoggingService.Info("GeneralSettingsPage: Disabled sending active app context to post-processing");
    }

    // =========================================================================
    // ANONYMOUS SPEED DATA
    // =========================================================================
    //
    // The setting is read once per transcription, where the cloud services build
    // their core TranscribeParams, so a change applies to the very next
    // transcription — nothing to start or stop here.

    private void ShareSpeedDataCheckbox_Checked(object sender, RoutedEventArgs e)
    {
        SettingsService.Instance.ShareAnonymousSpeedData = true;
        LoggingService.Info("GeneralSettingsPage: Enabled anonymous speed data sharing");
    }

    private void ShareSpeedDataCheckbox_Unchecked(object sender, RoutedEventArgs e)
    {
        SettingsService.Instance.ShareAnonymousSpeedData = false;
        LoggingService.Info("GeneralSettingsPage: Disabled anonymous speed data sharing");
    }
}
