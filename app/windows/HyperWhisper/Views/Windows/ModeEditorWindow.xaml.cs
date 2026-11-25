using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using HyperWhisper.Data.Entities;
using HyperWhisper.Localization;
using HyperWhisper.Models;
using HyperWhisper.Services;
using HyperWhisper.Utilities;
using HyperWhisper.Views.Pages.Settings;

namespace HyperWhisper.Views.Windows;

public partial class ModeEditorWindow : Window
{
    private readonly Mode _mode;
    private readonly bool _isCreateMode;
    private bool _isLoading;
    private bool _isUpdatingCloudModels;

    // Name-taken check (issue #2478). _storedName is the edited mode's name as
    // stored (null when creating); _otherModeNames is every OTHER mode's name.
    // Both are read on load and again at Save.
    private string? _storedName;
    private List<string> _otherModeNames = [];

    public ModeEditorWindow(Mode mode)
    {
        InitializeComponent();
        // 700 DIP is taller than a 1080p work area at 175%; the body scrolls (issue #1500).
        WindowWorkAreaFit.Attach(this);
        _mode = mode;
        _isCreateMode = false;
        _storedName = mode.Name;

        ApplyDefaultModeNameLock();
        Loaded += OnLoaded;
    }

    /// <summary>
    /// The default mode's name is fixed, and the field says so rather than just going
    /// grey (issue #494). Keyed on IsDefault - the mode's identity, not its label.
    ///
    /// Here and not in Loaded: it reads only _mode, which is fixed from construction,
    /// so a window that is never shown still applies the rule.
    /// </summary>
    private void ApplyDefaultModeNameLock()
    {
        ModeNameBox.IsEnabled = !_mode.IsDefault;
        ModeNameLockedHint.Visibility = _mode.IsDefault ? Visibility.Visible : Visibility.Collapsed;
        System.Windows.Automation.AutomationProperties.SetHelpText(
            ModeNameBox, _mode.IsDefault ? ModeNameLockedHint.Text : string.Empty);
    }

    public ModeEditorWindow(bool isCreateMode)
    {
        InitializeComponent();
        // 700 DIP is taller than a 1080p work area at 175%; the body scrolls (issue #1500).
        WindowWorkAreaFit.Attach(this);
        _isCreateMode = isCreateMode;

        // Create a new mode with local-first defaults (no cloud calls until configured)
        var existingModes = ModeService.Instance.GetAllModes();
        _mode = new Mode
        {
            Id = Guid.NewGuid(),
            Name = "",
            Preset = "hyper",
            ProviderType = "local",
            LocalEngine = "whisper",
            Model = "large-v3-turbo",
            ModelType = "large-v3-turbo",
            Language = "auto",
            IsDefault = false,
            IsSystemProvided = false,
            SortOrder = existingModes.Count,
            Punctuation = true,
            Capitalization = true,
            PostProcessingMode = 0,
            // Seed the spelling variant from the system region so a brand-new
            // mode opens on the user's own variant instead of always American.
            EnglishSpelling = EnglishSpellingRegionDefault.ForCurrentRegion(),
            CreatedDate = DateTime.UtcNow,
            ModifiedDate = DateTime.UtcNow
        };

        // No ApplyDefaultModeNameLock() here: this constructor sets IsDefault = false
        // above, so the lock could only ever be a no-op, and the XAML already ships
        // the unlocked state.
        Loaded += OnLoaded;
    }

    private void ReloadOtherModeNames()
    {
        var modes = ModeService.Instance.GetAllModes();
        _otherModeNames = modes.Where(m => m.Id != _mode.Id).Select(m => m.Name).ToList();
        // The stored name too: the Local API can rename this mode while the
        // window is open, and a stale one would exempt a name it no longer has.
        var stored = modes.FirstOrDefault(m => m.Id == _mode.Id);
        if (!_isCreateMode && stored != null) _storedName = stored.Name;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ReloadOtherModeNames();

        // Configure UI for ARM64 architecture before loading data
        ConfigureForArchitecture();

        LoadLocalModels();
        LoadLanguages();
        LoadPostProcessingProviders();
        // Note: Cloud models are loaded dynamically in LoadModeIntoEditor
        // based on the selected cloud provider
        LoadModeIntoEditor(_mode);

        // Configure UI for create vs edit mode
        if (_isCreateMode)
        {
            Title = Loc.S("mode.editor.title.create");
            DeleteModeButton.Visibility = Visibility.Collapsed;
            SaveModeButton.Content = Loc.S("modes.button.create");
        }

        // Update save button state based on name
        UpdateSaveButtonState();
    }

    /// <summary>
    /// Configures local provider availability for the current architecture.
    /// Local transcription remains visible when any native local engine is
    /// available, such as the ARM64 sherpa-onnx daemon.
    /// </summary>
    private void ConfigureForArchitecture()
    {
        if (!PlatformHelper.SupportsLocalTranscription)
        {
            // Show ARM64 info banner
            Arm64InfoBanner.Visibility = Visibility.Visible;

            // Hide the On-device source segment when no local engine can load.
            SourceOnDeviceSegment.Visibility = Visibility.Collapsed;

            // If the current mode uses an unavailable local engine, switch to BYOK cloud.
            if (!LocalModeModel.IsCloud(_mode) && !IsLocalEngineSupported(_mode.LocalEngine))
            {
                LocalModeModel.ApplyCloud(_mode);
                _mode.CloudProvider = "openai";
            }
        }
        else if (!LocalModeModel.IsCloud(_mode) && !IsLocalEngineSupported(_mode.LocalEngine))
        {
            LocalModeModel.ApplyCloud(_mode);
            _mode.CloudProvider = "openai";
        }

        if (!PlatformHelper.SupportsLocalLlmPostProcessing)
        {
            // Hide the On-device post-processing segment and remove Local LLM from
            // the BYOK provider list.
            PpSourceOnDeviceSegment.Visibility = Visibility.Collapsed;

            var localLlmItem = PostProcessingProviderCombo.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(i => i.Tag?.ToString() == PostProcessingProvider.LocalLlm.ToStringValue());
            if (localLlmItem != null)
            {
                PostProcessingProviderCombo.Items.Remove(localLlmItem);
            }

            if (_mode.PostProcessingProvider == PostProcessingProvider.LocalLlm.ToStringValue())
            {
                _mode.PostProcessingMode = 0;
                _mode.PostProcessingProvider = null;
                _mode.LocalPostProcessingModel = null;
            }
        }

        // The BYOK post-processing list excludes Local LLM (the On-device segment).
        var localLlmByokItem = PostProcessingProviderCombo.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(i => i.Tag?.ToString() == PostProcessingProvider.LocalLlm.ToStringValue());
        if (localLlmByokItem != null)
        {
            PostProcessingProviderCombo.Items.Remove(localLlmByokItem);
        }
    }

    // =========================================================================
    // SOURCE SEGMENT HELPERS (2-way: On-device / Your provider)
    // =========================================================================

    /// <summary>
    /// The currently selected transcription source segment tag
    /// ("ondevice" / "yourprovider").
    /// </summary>
    private string SelectedTranscriptionSource()
        => SourceYourProviderSegment.IsChecked == true ? "yourprovider" : "ondevice";

    /// <summary>
    /// Legacy provider-type string ("local" / "cloud") derived from the selected
    /// transcription source segment. Methods that branch on local-vs-cloud use this
    /// in place of the removed ProviderTypeCombo.
    /// </summary>
    private string SelectedProviderType()
        => SelectedTranscriptionSource() == "ondevice" ? "local" : "cloud";

    /// <summary>Selects the transcription source segment for the given source tag.</summary>
    private void SetTranscriptionSourceSegment(string source)
    {
        if (source == "yourprovider")
            SourceYourProviderSegment.IsChecked = true;
        else
            SourceOnDeviceSegment.IsChecked = true;
    }

    /// <summary>The currently selected post-processing source segment tag.</summary>
    private string SelectedPostProcessingSource()
        => PpSourceYourProviderSegment.IsChecked == true ? "yourprovider" : "ondevice";

    /// <summary>Selects the post-processing source segment for the given source tag.</summary>
    private void SetPostProcessingSourceSegment(string source)
    {
        if (source == "yourprovider")
            PpSourceYourProviderSegment.IsChecked = true;
        else
            PpSourceOnDeviceSegment.IsChecked = true;
    }

    // =========================================================================
    // NAME VALIDATION
    // =========================================================================

    private void ModeNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading) return;
        UpdateSaveButtonState();
    }

    private bool HasValidDictationLanguage()
    {
        if (SelectedProviderType() != "cloud") return true;
        ResolveEffectiveCloudProviderAndModel(out var provider, out var modelId);
        if (provider != CloudTranscriptionProvider.AssemblyAI || modelId != "dictation") return true;

        var language = (LanguageCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var support = Services.SharedModelsCatalog.GetLanguageSupport("assemblyAI", CatalogKind.Voice, "dictation");
        return language != null && support.Codes.Contains(language);
    }

    /// <summary>
    /// Whether another mode already uses the typed name (issue #2478), by the
    /// rule the Local API's MODE_NAME_TAKEN and the macOS editor use.
    /// </summary>
    private bool IsModeNameTaken() =>
        ModeNameRules.IsTaken(ModeNameBox.Text, _storedName, _otherModeNames);

    /// <summary>
    /// Shows or hides the name-taken hint. A WPF LiveSetting alone announces
    /// nothing, so the peer raises LiveRegionChanged when it appears, and the
    /// hint becomes the box's HelpText like the default-mode lock hint.
    /// </summary>
    private void ShowModeNameTakenHint(bool show)
    {
        var wasShown = ModeNameTakenHint.Visibility == Visibility.Visible;
        ModeNameTakenHint.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show == wasShown) return;

        System.Windows.Automation.AutomationProperties.SetHelpText(
            ModeNameBox, show ? ModeNameTakenHint.Text : string.Empty);
        if (show)
        {
            ModeNameTakenHint.UpdateLayout();
            System.Windows.Automation.Peers.UIElementAutomationPeer
                .CreatePeerForElement(ModeNameTakenHint)
                .RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
        }
    }

    private void UpdateSaveButtonState()
    {
        var nameTaken = IsModeNameTaken();
        ShowModeNameTakenHint(nameTaken);

        // Name is required, and no other mode may already use it
        if (string.IsNullOrWhiteSpace(ModeNameBox.Text) || nameTaken || !HasValidDictationLanguage())
        {
            SaveModeButton.IsEnabled = false;
            return;
        }

        // If local provider selected, check if the selected model is downloaded
        var providerType = SelectedProviderType();
        if (providerType == "local")
        {
            var tag = (LocalModelCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            var (engine, modelId) = ParseModelTag(tag);

            if (engine == "parakeet")
            {
                var model = ParakeetModelInfo.AllModels.FirstOrDefault(m => m.Id == modelId);
                if (model != null)
                {
                    var modelService = new ParakeetModelService();
                    if (!modelService.IsModelDownloaded(model))
                    {
                        SaveModeButton.IsEnabled = false;
                        return;
                    }
                }
            }
            else
            {
                var model = WhisperModelInfo.AllModels.FirstOrDefault(m => m.Type == modelId);
                if (model != null)
                {
                    var modelService = new WhisperModelService();
                    if (!modelService.IsModelDownloaded(model))
                    {
                        SaveModeButton.IsEnabled = false;
                        return;
                    }
                }
            }
        }

        if (PostProcessingCheck.IsChecked == true &&
            IsLocalLlmPostProcessingSelected())
        {
            var modelId = (PostProcessingModelCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            var localModel = LocalLlmModelInfo.GetById(modelId);
            if (localModel != null)
            {
                var modelService = new LocalLlmModelService();
                if (!modelService.IsModelDownloaded(localModel))
                {
                    SaveModeButton.IsEnabled = false;
                    return;
                }
            }
        }

        SaveModeButton.IsEnabled = true;
    }

    private bool IsLocalLlmPostProcessingSelected()
    {
        // Local LLM post-processing is now selected via the On-device source segment.
        return SelectedPostProcessingSource() == "ondevice";
    }

    // =========================================================================
    // TAG HELPERS
    // =========================================================================

    /// <summary>
    /// Parses a unified model combo tag like "whisper:base" or "parakeet:parakeet-v2"
    /// into (engine, modelId).
    /// </summary>
    private static (string engine, string modelId) ParseModelTag(string? tag)
    {
        if (string.IsNullOrEmpty(tag))
            return ("whisper", "base");

        var colonIndex = tag.IndexOf(':');
        if (colonIndex < 0)
            return ("whisper", tag);

        return (tag[..colonIndex], tag[(colonIndex + 1)..]);
    }

    // =========================================================================
    // DATA LOADING
    // =========================================================================

    private void LoadLocalModels()
    {
        LocalModelCombo.Items.Clear();

        // Whisper models (skip when the current architecture/runtime cannot load Whisper.net)
        if (PlatformHelper.SupportsWhisperTranscription)
        {
            var whisperService = new WhisperModelService();
            foreach (var model in WhisperModelInfo.AllModels)
            {
                var isDownloaded = whisperService.IsModelDownloaded(model);
                var item = new ComboBoxItem
                {
                    Content = $"{model.DisplayName} ({model.Size}){(isDownloaded ? "" : Loc.S("mode.editor.model.notDownloaded"))}",
                    Tag = $"whisper:{model.Type}"
                };
                LocalModelCombo.Items.Add(item);
            }
        }

        // Parakeet-family models (Parakeet, Qwen3 ASR, Nemotron) hosted by the sherpa-onnx daemon.
        if (PlatformHelper.SupportsParakeetTranscription)
        {
            var parakeetService = new ParakeetModelService();
            foreach (var model in ParakeetModelInfo.AllModels)
            {
                var isDownloaded = parakeetService.IsModelDownloaded(model);
                var langInfo = model.IsEnglishOnly ? "English" : "Multilingual";
                var item = new ComboBoxItem
                {
                    Content = $"{model.ProviderDisplayName} - {model.DisplayName} ({model.Size}, {langInfo}){(isDownloaded ? "" : Loc.S("mode.editor.parakeetModel.notDownloaded"))}",
                    Tag = $"parakeet:{model.Id}"
                };
                LocalModelCombo.Items.Add(item);
            }
        }
    }

    /// <summary>
    /// Loads cloud models for a specific provider into the CloudModelCombo dropdown.
    /// Called when the cloud provider selection changes.
    /// </summary>
    private void LoadCloudModels(CloudTranscriptionProvider provider, string? preferredModelId = null)
    {
        CloudModelCombo.Items.Clear();

        var allModels = CloudTranscriptionModels.GetModelsForProvider(provider);
        var canonicalPreferredModelId = !string.IsNullOrEmpty(preferredModelId)
            ? CloudTranscriptionModels.ResolveModelAlias(preferredModelId, provider)
            : preferredModelId;

        if (!string.IsNullOrEmpty(canonicalPreferredModelId) &&
            CloudModelShowAllCheck.IsChecked != true &&
            !CloudTranscriptionModels.GetPopularModelsForProvider(provider)
                .Any(model => model.Id.Equals(canonicalPreferredModelId, StringComparison.OrdinalIgnoreCase)))
        {
            SetCloudModelShowAll(true);
        }

        var showAll = CloudModelShowAllCheck.IsChecked == true;
        var selectableModels = (showAll ? allModels : CloudTranscriptionModels.GetPopularModelsForProvider(provider))
            .Where(m => m.IsAvailable)
            .ToList();

        // Retired/unavailable models (e.g. ElevenLabs Scribe v1) are excluded from the
        // "pick a new one" list above, but if the Mode's CURRENT selection is one of
        // them, keep it visible here rather than silently falling through to
        // SelectedIndex = 0 below and switching the user's mode to a different model
        // out from under them.
        if (!string.IsNullOrEmpty(canonicalPreferredModelId) &&
            !selectableModels.Any(m => m.Id.Equals(canonicalPreferredModelId, StringComparison.OrdinalIgnoreCase)))
        {
            var currentModel = allModels.FirstOrDefault(
                m => m.Id.Equals(canonicalPreferredModelId, StringComparison.OrdinalIgnoreCase));
            if (currentModel != null)
            {
                selectableModels.Add(currentModel);
            }
        }

        var models = selectableModels;
        foreach (var model in models)
        {
            var item = new ComboBoxItem
            {
                Content = showAll || !model.IsPopular
                    ? model.DisplayName
                    : Loc.S("mode.editor.cloudModel.popularLabel", model.DisplayName),
                Tag = model.Id
            };
            CloudModelCombo.Items.Add(item);
        }

        if (!string.IsNullOrEmpty(canonicalPreferredModelId))
        {
            foreach (ComboBoxItem item in CloudModelCombo.Items)
            {
                if (item.Tag?.ToString()?.Equals(canonicalPreferredModelId, StringComparison.OrdinalIgnoreCase) == true)
                {
                    CloudModelCombo.SelectedItem = item;
                    return;
                }
            }
        }

        if (CloudModelCombo.Items.Count > 0)
        {
            CloudModelCombo.SelectedIndex = 0;
        }
    }

    private void CloudModelShowAllCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _isUpdatingCloudModels) return;

        var providerTag = (CloudProviderCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var provider = CloudTranscriptionProviderExtensions.FromIdentifier(providerTag);
        var selectedModelId = (CloudModelCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        LoadCloudModels(provider, selectedModelId);
    }

    private void SetCloudModelShowAll(bool showAll)
    {
        _isUpdatingCloudModels = true;
        try
        {
            CloudModelShowAllCheck.IsChecked = showAll;
        }
        finally
        {
            _isUpdatingCloudModels = false;
        }
    }

    /// <summary>
    /// Handles cloud model selection change to update description text.
    /// </summary>
    private void CloudModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CloudModelCombo.SelectedItem is ComboBoxItem selected)
        {
            var modelId = selected.Tag?.ToString();
            var providerTag = (CloudProviderCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            var provider = CloudTranscriptionProviderExtensions.FromIdentifier(providerTag);
            var model = CloudTranscriptionModels.GetById(modelId, provider);

            // Show description with pricing if available
            if (model != null)
            {
                var description = model.Description;
                if (model.PricePerMinute.HasValue)
                {
                    if (provider == CloudTranscriptionProvider.AssemblyAI
                        && model.Id.EndsWith("-medical", StringComparison.Ordinal))
                    {
                        description += $" - base ${model.PricePerMinute:F4}/min + separate Medical Mode add-on";
                    }
                    else
                    {
                        description += $" - ${model.PricePerMinute:F4}/min";
                    }
                }
                CloudModelDescText.Text = description;
            }
            else
            {
                CloudModelDescText.Text = "";
            }
        }
        else
        {
            CloudModelDescText.Text = "";
        }

        if (!_isLoading)
        {
            AutoSelectEnglishForModel();
            UpdateLanguagesForSelectedModel();
            UpdateAllWarnings();
        }
    }

    /// <summary>
    /// Handles cloud provider selection change.
    /// Updates the model dropdown, accuracy tier panel, and API key warning for the new provider.
    /// </summary>
    private void CloudProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;

        ApplySelectedCloudProviderPanels();
        UpdateLanguagesForSelectedModel();
        UpdateAllWarnings();
    }

    private static bool IsLocalEngineSupported(string? localEngine)
    {
        return string.Equals(localEngine, "parakeet", StringComparison.OrdinalIgnoreCase)
            ? PlatformHelper.SupportsParakeetTranscription
            : PlatformHelper.SupportsWhisperTranscription;
    }

    /// <summary>
    /// Resolves the BYOK STT provider and model id from the current picker selection.
    /// </summary>
    private void ResolveEffectiveCloudProviderAndModel(out CloudTranscriptionProvider provider, out string modelId)
    {
        var providerTag = (CloudProviderCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        provider = CloudTranscriptionProviderExtensions.FromIdentifier(providerTag);
        modelId = (CloudModelCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Shows the BYOK ("Your provider") transcription panels for the provider selected
    /// in <see cref="CloudProviderCombo"/>.
    /// </summary>
    private void ApplySelectedCloudProviderPanels()
    {
        if (CloudProviderCombo.SelectedItem is ComboBoxItem selected)
        {
            var providerTag = selected.Tag?.ToString();
            var provider = CloudTranscriptionProviderExtensions.FromIdentifier(providerTag);

            // Every provider shows the model row, including Grok, whose single
            // implicit model is listed as the one entry.
            CloudModelPanel.Visibility = Visibility.Visible;
            SetCloudModelShowAll(false);
            LoadCloudModels(provider);

            // Show Gemini custom prompt panel only for Gemini provider
            GeminiCustomPromptPanel.Visibility = provider == CloudTranscriptionProvider.Gemini
                ? Visibility.Visible : Visibility.Collapsed;

            UpdateApiKeyWarning("cloud", provider);
        }
    }

    private void LoadLanguages()
    {
        LanguageCombo.Items.Clear();

        foreach (var lang in LanguageInfo.AllLanguages)
        {
            var item = new ComboBoxItem
            {
                Content = lang.DisplayName,
                Tag = lang.Code
            };
            LanguageCombo.Items.Add(item);
        }
    }

    // =========================================================================
    // EVENT HANDLERS
    // =========================================================================

    private void SourceSegment_Checked(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;

        var source = SelectedTranscriptionSource();
        // UpdateSourcePanels sets the API-key warning with the correct provider for
        // each source (HW Cloud → no key; BYOK → the combo provider; local → cleared).
        UpdateSourcePanels(source);
        // The Language row's visibility follows the SOURCE as well as the local
        // model: leaving an English-only local model (Parakeet v2, Whisper *.en)
        // for a cloud source must bring the picker back, and returning to one must
        // hide it and force English again (#1641). Runs before the list filter, in
        // the same order as LocalModelCombo_SelectionChanged, so the filter sees
        // the forced "en" and keeps it selected.
        AutoSelectEnglishForModel();
        UpdateLanguagesForSelectedModel();
        UpdateAllWarnings();
        UpdateSaveButtonState();
    }

    /// <summary>
    /// Shows the transcription model controls for the selected source segment:
    /// On-device → local model picker; HyperWhisper Cloud → Engine (tier) + Model;
    /// Your provider → BYOK provider + model. When switching INTO a cloud source,
    /// the CloudProvider context is set so downstream resolvers behave correctly.
    /// </summary>
    private void UpdateSourcePanels(string source)
    {
        if (source == "ondevice")
        {
            LocalModelPanel.Visibility = Visibility.Visible;
            CloudProviderPanel.Visibility = Visibility.Collapsed;
            CloudModelPanel.Visibility = Visibility.Collapsed;
            GeminiCustomPromptPanel.Visibility = Visibility.Collapsed;
            UpdateApiKeyWarning("local");
        }
        else // "yourprovider"
        {
            LocalModelPanel.Visibility = Visibility.Collapsed;
            ParakeetLanguageWarning.Visibility = Visibility.Collapsed;
            CloudProviderPanel.Visibility = Visibility.Visible;
            if (CloudProviderCombo.SelectedIndex == -1 && CloudProviderCombo.Items.Count > 0)
                CloudProviderCombo.SelectedIndex = 0;
            ApplySelectedCloudProviderPanels();
        }
    }

    private void UpdateLocalModelStatus()
    {
        if (LocalModelCombo.SelectedItem is not ComboBoxItem selected) return;

        var (engine, modelId) = ParseModelTag(selected.Tag?.ToString());

        if (engine == "parakeet")
        {
            var model = ParakeetModelInfo.AllModels.FirstOrDefault(m => m.Id == modelId);
            if (model != null)
            {
                var modelService = new ParakeetModelService();
                var isDownloaded = modelService.IsModelDownloaded(model);
                LocalModelStatusText.Text = isDownloaded
                    ? ReadyTextForParakeetFamily(model)
                    : "Model not downloaded. Go to Model Library to download.";
            }
        }
        else
        {
            var model = WhisperModelInfo.AllModels.FirstOrDefault(m => m.Type == modelId);
            if (model != null)
            {
                var modelService = new WhisperModelService();
                var isDownloaded = modelService.IsModelDownloaded(model);
                LocalModelStatusText.Text = isDownloaded
                    ? $"Ready - {model.RecommendedVramDisplay} VRAM"
                    : "Model not downloaded. Go to Home to download.";
            }
        }
    }

    private void LocalModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;

        UpdateLocalModelStatus();
        AutoSelectEnglishForModel();
        UpdateLanguagesForSelectedModel();
        UpdateAllWarnings();
        UpdateSaveButtonState();
    }

    private void UpdateParakeetLanguageWarning()
    {
        var tag = (LocalModelCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var (engine, modelId) = ParseModelTag(tag);

        if (engine != "parakeet")
        {
            ParakeetLanguageWarning.Visibility = Visibility.Collapsed;
            return;
        }

        var model = ParakeetModelInfo.AllModels.FirstOrDefault(m => m.Id == modelId);
        var language = (LanguageCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();

        if (model == null)
        {
            ParakeetLanguageWarning.Visibility = Visibility.Collapsed;
            return;
        }

        if (model.IsEnglishOnly && language != "en")
        {
            ParakeetLanguageWarningText.Text = $"{model.DisplayName} supports English only. Language will be set to English.";
            ParakeetLanguageWarning.Visibility = Visibility.Visible;
        }
        else if (!model.IsEnglishOnly && !string.IsNullOrEmpty(language) && language != "auto" && !model.IsLanguageSupported(language))
        {
            ParakeetLanguageWarningText.Text = $"{model.DisplayName} does not support this language. Choose another local model or a cloud provider.";
            ParakeetLanguageWarning.Visibility = Visibility.Visible;
        }
        else
        {
            ParakeetLanguageWarning.Visibility = Visibility.Collapsed;
        }
    }

    private static string ReadyTextForParakeetFamily(ParakeetModelInfo model)
    {
        return model.Engine switch
        {
            ParakeetEngine.Qwen3 => "Ready - CPU inference",
            ParakeetEngine.NemotronMl => "Ready - CPU streaming",
            _ => PlatformHelper.IsArm64
                ? "Ready - CPU inference"
                : "Ready - DirectML acceleration with CPU fallback"
        };
    }

    // =========================================================================
    // PRESET HANDLING
    // =========================================================================

    private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;

        if (PresetCombo.SelectedItem is ComboBoxItem selected)
        {
            var presetTag = selected.Tag?.ToString();
            PresetDescText.Text = PresetTypeExtensions.FromString(presetTag ?? "hyper").ToDescription();

            // Show/hide custom instructions panel
            CustomInstructionsPanel.Visibility = presetTag == "custom" ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // =========================================================================
    // LANGUAGE HANDLING
    // =========================================================================

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        UpdateEnglishSpellingVisibility();
        UpdateNova3Warning();
        UpdateSaveButtonState();
    }

    /// <summary>
    /// Selects the English spelling item for a mode. A mode that has no stored
    /// value falls back to the system region's variant, so a new mode opens on
    /// the user's own spelling instead of always American.
    /// </summary>
    private void SelectEnglishSpelling(string? storedValue)
    {
        if (EnglishSpellingCombo.Items.Count == 0)
        {
            return;
        }

        var value = string.IsNullOrWhiteSpace(storedValue)
            ? EnglishSpellingRegionDefault.ForCurrentRegion()
            : storedValue;

        if (!TrySelectEnglishSpellingItem(value))
        {
            // Unrecognised stored value — fall back to the region default, then
            // to the first item so the combo is never left unselected.
            if (!TrySelectEnglishSpellingItem(EnglishSpellingRegionDefault.ForCurrentRegion()))
            {
                EnglishSpellingCombo.SelectedIndex = 0;
            }
        }
    }

    private bool TrySelectEnglishSpellingItem(string value)
    {
        foreach (ComboBoxItem item in EnglishSpellingCombo.Items)
        {
            if (item.Tag?.ToString() == value)
            {
                EnglishSpellingCombo.SelectedItem = item;
                return true;
            }
        }
        return false;
    }

    private void UpdateEnglishSpellingVisibility()
    {
        if (LanguageCombo.SelectedItem is ComboBoxItem selected)
        {
            var lang = selected.Tag?.ToString();
            // Show English spelling only for English or Auto-detect
            var isEnglishRelated = lang == "en" || lang == "auto" || string.IsNullOrEmpty(lang);
            var isPostProcessingEnabled = PostProcessingCheck.IsChecked == true;

            EnglishSpellingPanel.Visibility = (isEnglishRelated && isPostProcessingEnabled)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    // =========================================================================
    // PUNCTUATION / REMOVE TRAILING PERIOD HANDLING
    // =========================================================================

    private void PunctuationCheck_CheckedChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;

        bool punctuationEnabled = PunctuationCheck.IsChecked == true;

        // Only toggle RemoveTrailingPeriod visibility when post-processing is enabled
        // (when post-processing is off, RemoveTrailingPeriod is always visible standalone)
        if (PostProcessingCheck.IsChecked == true)
        {
            RemoveTrailingPeriodCheck.Visibility = punctuationEnabled ? Visibility.Visible : Visibility.Collapsed;
        }

        if (!punctuationEnabled)
        {
            RemoveTrailingPeriodCheck.IsChecked = false;
        }
    }

    // =========================================================================
    // USER SYSTEM PROMPT HANDLING
    // =========================================================================

    private void UserPromptCheck_CheckedChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;

        UserPromptPanel.Visibility = UserPromptCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        if (UserPromptCheck.IsChecked != true)
        {
            UserPromptBox.Text = "";
        }
    }

    private void ClearPromptButton_Click(object sender, RoutedEventArgs e)
    {
        UserPromptBox.Text = "";
    }

    private void UserPromptBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UserPromptCharCount.Text = $"{UserPromptBox.Text.Length}/8000";
    }

    private void GeminiCustomPromptBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        GeminiCustomPromptCharCount.Text = $"{GeminiCustomPromptBox.Text.Length}/2000";
        GeminiCustomPromptPlaceholder.Visibility = string.IsNullOrEmpty(GeminiCustomPromptBox.Text)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Updates the API key warning based on provider type and selected cloud provider.
    /// Shows a warning if the required API key is not configured.
    /// </summary>
    private void UpdateApiKeyWarning(string providerType, CloudTranscriptionProvider? cloudProvider = null)
    {
        if (providerType != "cloud")
        {
            ApiKeyWarning.Visibility = Visibility.Collapsed;
            return;
        }

        // Get selected cloud provider if not passed
        if (cloudProvider == null)
        {
            var selectedItem = CloudProviderCombo.SelectedItem as ComboBoxItem;
            var providerTag = selectedItem?.Tag?.ToString();
            cloudProvider = CloudTranscriptionProviderExtensions.FromIdentifier(providerTag);
        }

        // HyperWhisper Cloud and the HW-Cloud-routed providers (Azure MAI,
        // Google Chirp) don't require an API key.
        if (cloudProvider.HasValue && !cloudProvider.Value.RequiresApiKey())
        {
            ApiKeyWarning.Visibility = Visibility.Collapsed;
            return;
        }

        // Check if API key is configured for this provider (the shared key
        // map, issue #1220: shared post-processing key or own slot).
        bool hasKey = cloudProvider is { } provider && ApiKeyService.Instance.HasApiKey(provider);

        if (!hasKey)
        {
            var providerName = cloudProvider?.GetDisplayName() ?? "Cloud";
            ApiKeyWarning.Visibility = Visibility.Visible;
            ApiKeyWarningText.Text = $"{providerName} API key not configured. Add it in the Model Library API keys manager before using this provider.";
        }
        else
        {
            ApiKeyWarning.Visibility = Visibility.Collapsed;
        }
    }

    private void PostProcessingCheck_CheckedChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;

        var ppEnabled = PostProcessingCheck.IsChecked == true;
        PostProcessingSettingsPanel.Visibility = ppEnabled ? Visibility.Visible : Visibility.Collapsed;

        if (ppEnabled)
        {
            if (PresetCombo.SelectedIndex == -1 && PresetCombo.Items.Count > 0)
                PresetCombo.SelectedIndex = 0;
            // Show the controls for the currently selected post-processing source.
            ApplyPostProcessingSourcePanels();
        }

        // Punctuation & capitalization are LLM instructions — hide when post-processing is off
        PunctuationCheck.Visibility = ppEnabled ? Visibility.Visible : Visibility.Collapsed;
        CapitalizationCheck.Visibility = ppEnabled ? Visibility.Visible : Visibility.Collapsed;

        if (!ppEnabled)
        {
            // Show remove trailing period standalone when post-processing is off
            RemoveTrailingPeriodCheck.Visibility = Visibility.Visible;
            PunctuationCheck.IsChecked = false;
            CapitalizationCheck.IsChecked = false;
        }
        else
        {
            // When post-processing is on, remove trailing period follows punctuation toggle
            RemoveTrailingPeriodCheck.Visibility = PunctuationCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        // Update English spelling visibility when post-processing toggle changes
        UpdateEnglishSpellingVisibility();
        UpdateSaveButtonState();
    }

    /// <summary>
    /// Drives the post-processing model controls from the selected source segment:
    /// On-device → Local LLM model picker; HyperWhisper Cloud → Engine + Model;
    /// Your provider → BYOK provider + model. The PostProcessingProvider context is
    /// set here so the save path and warnings resolve correctly.
    /// </summary>
    private void PpSourceSegment_Checked(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        ApplyPostProcessingSourcePanels();
        UpdateSaveButtonState();
    }

    private void ApplyPostProcessingSourcePanels()
    {
        var source = SelectedPostProcessingSource();
        if (source == "ondevice")
        {
            PostProcessingProviderPanel.Visibility = Visibility.Collapsed;
            PostProcessingModelPanel.Visibility = Visibility.Visible;
            LoadPostProcessingModels(PostProcessingProvider.LocalLlm);
        }
        else // "yourprovider"
        {
            PostProcessingProviderPanel.Visibility = Visibility.Visible;
            if (PostProcessingProviderCombo.SelectedIndex == -1 && PostProcessingProviderCombo.Items.Count > 0)
                PostProcessingProviderCombo.SelectedIndex = 0;
            ApplySelectedPostProcessingProviderPanels();
        }
    }

    private void PostProcessingProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        // Only relevant for the "Your provider" source — the BYOK list excludes
        // HyperWhisper Cloud and Local LLM (both reached via source segments).
        if (SelectedPostProcessingSource() == "yourprovider")
        {
            ApplySelectedPostProcessingProviderPanels();
        }
        UpdateSaveButtonState();
    }

    /// <summary>
    /// Shows/populates the BYOK post-processing model picker for the provider selected
    /// in <see cref="PostProcessingProviderCombo"/>. Custom endpoints carry their model
    /// in the endpoint config, so the picker is hidden for them.
    /// </summary>
    private void ApplySelectedPostProcessingProviderPanels()
    {
        if (PostProcessingProviderCombo.SelectedItem is not ComboBoxItem selected) return;

        var providerStr = selected.Tag?.ToString();

        // Hide model selection for custom endpoints (model is in endpoint config)
        if (CustomPostProcessingEndpoint.IsCustomProviderString(providerStr))
        {
            PostProcessingModelPanel.Visibility = Visibility.Collapsed;
            LocalLlmOpenModelsSettingsButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            PostProcessingModelPanel.Visibility = Visibility.Visible;
            var provider = PostProcessingProviderExtensions.FromString(providerStr ?? "");
            LoadPostProcessingModels(provider);
        }
    }

    /// <summary>
    /// Populates the post-processing provider ComboBox dynamically,
    /// including custom endpoints after a separator.
    /// </summary>
    private void LoadPostProcessingProviders()
    {
        _isLoading = true;

        // Keep the static XAML items (they are already in the ComboBox)
        // Just append custom endpoints after them

        // Remove any previously added custom endpoint items (separator + items)
        var itemsToRemove = new List<object>();
        bool foundSeparator = false;
        foreach (var item in PostProcessingProviderCombo.Items)
        {
            if (item is Separator)
            {
                foundSeparator = true;
                itemsToRemove.Add(item);
            }
            else if (foundSeparator)
            {
                itemsToRemove.Add(item);
            }
        }
        foreach (var item in itemsToRemove)
        {
            PostProcessingProviderCombo.Items.Remove(item);
        }

        // Add custom endpoints if any exist
        var customEndpoints = CustomEndpointManager.Instance.GetAllEndpoints();
        if (customEndpoints.Count > 0)
        {
            PostProcessingProviderCombo.Items.Add(new Separator());
            foreach (var endpoint in customEndpoints)
            {
                PostProcessingProviderCombo.Items.Add(new ComboBoxItem
                {
                    Content = endpoint.Name,
                    Tag = endpoint.ProviderString
                });
            }
        }

        _isLoading = false;
    }

    private void LoadPostProcessingModels(PostProcessingProvider provider)
    {
        PostProcessingModelCombo.Items.Clear();
        
        var models = LanguageModelInfo.GetModelsForProvider(provider);
        foreach (var model in models)
        {
            var content = model.DisplayName;
            if (provider == PostProcessingProvider.LocalLlm)
            {
                var localModel = LocalLlmModelInfo.GetById(model.Id);
                if (localModel != null)
                {
                    var modelService = new LocalLlmModelService();
                    var isDownloaded = modelService.IsModelDownloaded(localModel);
                    // Plan-aware: drops "(Recommended)" on machines whose local-LLM plan is CPU-only.
                    content = $"{LocalLlmGpuHelper.DisplayNameFor(localModel)} ({localModel.Size}){(isDownloaded ? "" : Loc.S("settings.models.localLlm.notDownloadedSuffix"))}";
                }
            }

            var item = new ComboBoxItem
            {
                Content = content,
                Tag = model.Id
            };
            PostProcessingModelCombo.Items.Add(item);
        }

        if (PostProcessingModelCombo.Items.Count > 0)
        {
            PostProcessingModelCombo.SelectedIndex = 0;
        }
    }

    private void PostProcessingModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PostProcessingModelCombo.SelectedItem is ComboBoxItem selected)
        {
            var modelId = selected.Tag?.ToString();
            var model = LanguageModelInfo.GetById(modelId);
            // Local LLM is selected via the On-device source segment, not a provider-combo
            // item (ConfigureForArchitecture removes "local_llm" from the combo). Detect it
            // the same way as IsLocalLlmPostProcessingSelected() so the download-required
            // description + Open Models Settings button render for an undownloaded GGUF model.
            if (IsLocalLlmPostProcessingSelected())
            {
                var localModel = LocalLlmModelInfo.GetById(modelId);
                if (localModel != null)
                {
                    var modelService = new LocalLlmModelService();
                    var isDownloaded = modelService.IsModelDownloaded(localModel);
                    PostProcessingModelDescText.Text = isDownloaded
                        ? Loc.S("mode.editor.localLlm.readyDescription", localModel.Description)
                        : Loc.S("mode.editor.localLlm.downloadDescription", localModel.Description);
                    LocalLlmOpenModelsSettingsButton.Visibility = isDownloaded ? Visibility.Collapsed : Visibility.Visible;
                    UpdateSaveButtonState();
                    return;
                }
            }

            PostProcessingModelDescText.Text = model?.Description ?? "";
            LocalLlmOpenModelsSettingsButton.Visibility = Visibility.Collapsed;
            UpdateSaveButtonState();
        }
        else
        {
            PostProcessingModelDescText.Text = "";
            LocalLlmOpenModelsSettingsButton.Visibility = Visibility.Collapsed;
            UpdateSaveButtonState();
        }
    }

    private void LocalLlmOpenModelsSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedModelId = (PostProcessingModelCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var settingsWindow = new Window
        {
            Title = Loc.S("settings.section.models"),
            Width = 720,
            Height = 760,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new ModelsSettingsPage()
        };

        // 760 DIP is taller than a 1080p work area at 175% (issue #1500).
        WindowWorkAreaFit.Attach(settingsWindow);
        settingsWindow.ShowDialog();

        LoadPostProcessingModels(PostProcessingProvider.LocalLlm);
        if (!string.IsNullOrEmpty(selectedModelId))
        {
            foreach (ComboBoxItem item in PostProcessingModelCombo.Items)
            {
                if (item.Tag?.ToString() == selectedModelId)
                {
                    PostProcessingModelCombo.SelectedItem = item;
                    break;
                }
            }
        }
        UpdateSaveButtonState();
    }

    private void SaveModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (!HasValidDictationLanguage())
        {
            LanguageCombo.Focus();
            return;
        }

        // Re-read: the Local API can add a mode while this window is open.
        ReloadOtherModeNames();
        if (IsModeNameTaken())
        {
            UpdateSaveButtonState();
            ModeNameBox.Focus();
            return;
        }

        _mode.Name = ModeNameBox.Text.Trim();

        // Save preset
        if (PresetCombo.SelectedItem is ComboBoxItem presetItem)
        {
            _mode.Preset = presetItem.Tag?.ToString() ?? "hyper";
        }
        else if (string.IsNullOrEmpty(_mode.Preset) || _mode.Preset == "voiceToText")
        {
            _mode.Preset = "hyper";
        }

        // Save custom instructions (only relevant for Custom preset, but always save)
        _mode.CustomInstructions = CustomInstructionsBox.Text.Trim();

        var transcriptionSource = SelectedTranscriptionSource();
        _mode.ProviderType = transcriptionSource == "ondevice" ? "local" : "cloud";

        if (_mode.ProviderType == "cloud")
        {
            // The On-device arm below writes a local id into Model; put the cloud
            // sentinel back so macOS does not restore this mode as local (#1477).
            LocalModeModel.ApplyCloud(_mode);

            if (CloudProviderCombo.SelectedItem is ComboBoxItem providerComboItem)
            {
                _mode.CloudProvider = providerComboItem.Tag?.ToString() ?? "openai";
            }

            if (CloudModelCombo.SelectedItem is ComboBoxItem cloudItem)
            {
                _mode.CloudTranscriptionModel = cloudItem.Tag?.ToString() ?? "whisper-1";
            }
            _mode.CloudTranscriptionDomain = null;
            _mode.CloudAccuracyTier = string.Empty;

            // Save Gemini custom prompt
            var geminiPromptText = GeminiCustomPromptBox.Text?.Trim() ?? "";
            _mode.GeminiCustomPrompt = string.IsNullOrEmpty(geminiPromptText) ? null : geminiPromptText;
        }
        else
        {
            var tag = (LocalModelCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            var (engine, modelId) = ParseModelTag(tag);
            _mode.LocalEngine = engine;

            if (engine == "parakeet")
            {
                _mode.LocalParakeetModel = modelId;
            }
            else
            {
                _mode.ModelType = modelId;
            }

            // A new mode is seeded Model = "cloud" / CloudProvider = "hyperwhisper"
            // (constructor above). Rewrite both so the row stops saying "cloud":
            // macOS and Linux route on those two fields when a backup crosses (#1477).
            LocalModeModel.ApplyOnDevice(_mode);
        }

        if (LanguageCombo.SelectedItem is ComboBoxItem langItem)
        {
            _mode.Language = langItem.Tag?.ToString() ?? "auto";
        }

        // Save text processing toggles (always save, regardless of post-processing state)
        _mode.Punctuation = PunctuationCheck.IsChecked == true;
        _mode.Capitalization = CapitalizationCheck.IsChecked == true;
        _mode.ProfanityFilter = ProfanityFilterCheck.IsChecked == true;
        _mode.EnableScreenOCR = false;
        _mode.RemoveTrailingPeriod = RemoveTrailingPeriodCheck.IsChecked == true;

        // Save English spelling
        if (EnglishSpellingCombo.SelectedItem is ComboBoxItem spellingItem)
        {
            _mode.EnglishSpelling = spellingItem.Tag?.ToString() ?? "american";
        }

        // Save user system prompt
        _mode.UserSystemPrompt = UserPromptCheck.IsChecked == true ? UserPromptBox.Text.Trim() : "";

        if (PostProcessingCheck.IsChecked == true)
        {
            var ppSource = SelectedPostProcessingSource();
            if (ppSource == "ondevice")
            {
                _mode.PostProcessingProvider = PostProcessingProvider.LocalLlm.ToStringValue();
            }
            else if (PostProcessingProviderCombo.SelectedItem is ComboBoxItem ppProviderItem)
            {
                _mode.PostProcessingProvider = ppProviderItem.Tag?.ToString();
            }

            _mode.PostProcessingMode = _mode.PostProcessingProvider == PostProcessingProvider.LocalLlm.ToStringValue()
                ? 2
                : 1;

            if (PostProcessingModelCombo.SelectedItem is ComboBoxItem ppModelItem)
            {
                if (ppSource == "ondevice")
                {
                    _mode.LocalPostProcessingModel = ppModelItem.Tag?.ToString();
                }
                else
                {
                    _mode.LanguageModel = ppModelItem.Tag?.ToString();
                }
            }
        }
        else
        {
            _mode.PostProcessingMode = 0;
            _mode.EnableScreenOCR = false;
        }

        _mode.ModifiedDate = DateTime.UtcNow;

        if (_isCreateMode)
        {
            ModeService.Instance.SaveMode(_mode);
        }
        else
        {
            ModeService.Instance.UpdateMode(_mode);
        }

        DialogResult = true;
        Close();
    }

    private void DeleteModeButton_Click(object sender, RoutedEventArgs e)
    {
        var result = WpfMessageBox.Show(
            Loc.S("mode.editor.delete.confirm.message", _mode.Name),
            Loc.S("mode.editor.delete.confirm.title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        var deleted = ModeService.Instance.DeleteMode(_mode.Id);

        if (!deleted)
        {
            WpfMessageBox.Show(
                Loc.S("mode.editor.delete.cannotDelete.message"),
                Loc.S("mode.editor.delete.cannotDelete.title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        DialogResult = true; // Treating delete as a successful "change" that requires parent refresh
        Close();
    }

    // =========================================================================
    // VOCABULARY & MODEL WARNINGS
    // =========================================================================

    /// <summary>
    /// Updates vocabulary warning visibility based on selected provider/model.
    /// Shows warning for providers that don't support custom vocabulary.
    /// </summary>
    private void UpdateVocabularyWarning()
    {
        bool showWarning = false;
        string warningText = Loc.S("mode.editor.warning.vocabularyUnsupported");

        var providerType = SelectedProviderType();

        if (providerType == "local")
        {
            var tag = (LocalModelCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            var (engine, _) = ParseModelTag(tag);
            if (engine == "parakeet")
            {
                showWarning = true;
                warningText = Loc.S("mode.editor.warning.vocabularyUnsupported.parakeet");
            }
        }
        else if (providerType == "cloud")
        {
            var cloudProvider = (CloudProviderCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            var cloudModel = (CloudModelCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();

            // Your own provider (BYOK): the shared models catalog says per model (#681).
            if (ByokVocabularyUnsupported(
                CloudTranscriptionProviderExtensions.FromIdentifier(cloudProvider), cloudModel))
            {
                showWarning = true;
            }
        }

        VocabularyWarning.Visibility = showWarning ? Visibility.Visible : Visibility.Collapsed;
        VocabularyWarningText.Text = warningText;
    }

    /// <summary>
    /// Whether a BYOK (your own provider) transcription model does NOT support
    /// custom vocabulary, read from <c>shared-models/models-catalog.json</c> (#681).
    ///
    /// True only when the catalog HAS a voice row for (provider, model) and that
    /// row says vocabulary is unsupported. <see cref="SharedModelsCatalog.SupportsCustomVocabulary"/>
    /// answers false on a catalog MISS, so asking it alone would warn on an empty
    /// or uncatalogued id; this suppresses the warning instead of guessing, the
    /// same rule the HyperWhisper Cloud branch follows. Row existence is read
    /// through <see cref="SharedModelsCatalog.DisplayName"/>, which is null only
    /// when the row is not catalogued (every cloud voice row carries a name, and
    /// SmokeTests asserts that for every registry row) or the core cannot answer.
    /// HyperWhisper Cloud and <see cref="CloudTranscriptionProvider.None"/> are
    /// never BYOK and always answer false.
    ///
    /// Static so SmokeTests can check it against the catalog without a window.
    /// </summary>
    internal static bool ByokVocabularyUnsupported(CloudTranscriptionProvider provider, string? modelId)
    {
        if (provider == CloudTranscriptionProvider.None)
            return false;
        if (string.IsNullOrEmpty(modelId))
            return false;

        var key = SharedModelsCatalog.CatalogKey(provider);
        if (SharedModelsCatalog.DisplayName(key, CatalogKind.Voice, modelId) is null)
            return false;
        return !SharedModelsCatalog.SupportsCustomVocabulary(key, CatalogKind.Voice, modelId);
    }

    /// <summary>
    /// Updates Nova-3 auto-detect warning visibility.
    /// Shows warning when using Deepgram Nova models with auto-detect language.
    /// </summary>
    private void UpdateNova3Warning()
    {
        var providerType = SelectedProviderType();
        var cloudProvider = (CloudProviderCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var cloudModel = (CloudModelCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var language = (LanguageCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();

        bool show = providerType == "cloud"
            && cloudProvider == "deepgram" && cloudModel?.Contains("nova") == true
            && (language == "auto" || string.IsNullOrEmpty(language));

        Nova3Warning.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Updates all provider/model warnings.
    /// </summary>
    private void UpdateAllWarnings()
    {
        UpdateVocabularyWarning();
        UpdateParakeetLanguageWarning();
        UpdateNova3Warning();
    }

    /// <summary>
    /// Auto-selects English language for English-only local models and hides the
    /// language row; shows and enables it again for every other model and source.
    /// Every handler that can change the source or the local model must call this
    /// (#1641: the source segment did not, so the row stayed hidden on Cloud).
    /// </summary>
    private void AutoSelectEnglishForModel()
    {
        var providerType = SelectedProviderType();
        bool isEnglishOnly = false;

        if (providerType == "local")
        {
            var tag = (LocalModelCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            var (engine, modelId) = ParseModelTag(tag);

            if (engine == "parakeet")
            {
                var model = ParakeetModelInfo.AllModels.FirstOrDefault(m => m.Id == modelId);
                if (model?.IsEnglishOnly == true)
                    isEnglishOnly = true;
            }
            else if (modelId?.EndsWith(".en") == true)
            {
                isEnglishOnly = true;
            }
        }

        if (isEnglishOnly)
        {
            SelectLanguage("en");
            LanguageCombo.IsEnabled = false;
            LanguagePanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            LanguageCombo.IsEnabled = true;
            LanguagePanel.Visibility = Visibility.Visible;
        }
    }

    private void ReplaceLanguageItems(IEnumerable<LanguageInfo> languages)
    {
        var currentLang = (LanguageCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();

        LanguageCombo.Items.Clear();
        foreach (var lang in languages)
        {
            LanguageCombo.Items.Add(new ComboBoxItem { Content = lang.DisplayName, Tag = lang.Code });
        }

        bool found = false;
        if (!string.IsNullOrEmpty(currentLang))
        {
            foreach (ComboBoxItem item in LanguageCombo.Items)
            {
                if (item.Tag?.ToString() == currentLang) { LanguageCombo.SelectedItem = item; found = true; break; }
            }
        }
        if (!found) SelectLanguage("auto");
    }

    /// <summary>
    /// Filters the language dropdown based on the selected model.
    /// Parakeet v3 shows only its supported languages + Automatic.
    /// Whisper / Cloud restores the full 126-language list.
    /// English-only models are handled separately by AutoSelectEnglishForModel.
    /// </summary>
    private void UpdateLanguagesForSelectedModel()
    {
        var providerType = SelectedProviderType();

        if (providerType == "local")
        {
            var tag = (LocalModelCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            var (engine, modelId) = ParseModelTag(tag);

            if (engine == "parakeet")
            {
                var model = ParakeetModelInfo.AllModels.FirstOrDefault(m => m.Id == modelId);
                if (model != null && !model.IsEnglishOnly)
                {
                    // Multilingual Parakeet: filter to supported languages
                    var supportedCodes = model.SupportedLanguages;
                    var filteredLanguages = LanguageInfo.AllLanguages.Where(lang =>
                        lang.Code == "auto" || supportedCodes.Contains(lang.Code, StringComparer.OrdinalIgnoreCase));
                    ReplaceLanguageItems(filteredLanguages);
                    return;
                }
                // English-only Parakeet: language picker is disabled by AutoSelectEnglishForModel,
                // but keep the full list so switching back to a multilingual model works
            }
        }

        if (providerType == "cloud")
        {
            ResolveEffectiveCloudProviderAndModel(out var cloudProvider, out var effectiveModelId);

            if (cloudProvider == CloudTranscriptionProvider.Soniox)
            {
                var supportedCodes = new HashSet<string>(LanguageInfo.SonioxAsyncLanguageCodes, StringComparer.OrdinalIgnoreCase);
                var filteredLanguages = LanguageInfo.AllLanguages.Where(lang => supportedCodes.Contains(lang.Code));
                ReplaceLanguageItems(filteredLanguages);
                return;
            }

            if (cloudProvider == CloudTranscriptionProvider.AssemblyAI)
            {
                var modelId = effectiveModelId;
                if (modelId == "dictation")
                {
                    var support = Services.SharedModelsCatalog.GetLanguageSupport("assemblyAI", CatalogKind.Voice, "dictation");
                    ReplaceLanguageItems(support.Codes.Select(code => new LanguageInfo(code, LanguageInfo.GetDisplayName(code))));
                    return;
                }

                string[]? filteredCodes = null;

                // Medical Mode add-on only supports EN/ES/DE/FR — for other
                // languages AssemblyAI silently skips the domain correction.
                if (modelId.EndsWith("-medical", StringComparison.Ordinal))
                {
                    filteredCodes = new[] { "auto", "en", "es", "de", "fr" };
                }
                if (filteredCodes != null)
                {
                    var supportedCodes = new HashSet<string>(filteredCodes, StringComparer.OrdinalIgnoreCase);
                    var filteredLanguages = LanguageInfo.AllLanguages.Where(lang => supportedCodes.Contains(lang.Code));
                    ReplaceLanguageItems(filteredLanguages);
                    return;
                }
            }

            if (cloudProvider == CloudTranscriptionProvider.Grok)
            {
                var filteredLanguages = LanguageInfo.AllLanguages.Where(lang =>
                    lang.Code == "auto" || GrokSttService.TryGetSupportedFormattingLanguageCode(lang.Code, out _));
                ReplaceLanguageItems(filteredLanguages);
                return;
            }
        }

        // Whisper or Cloud: restore full language list if it was filtered
        if (LanguageCombo.Items.Count < LanguageInfo.AllLanguages.Length)
        {
            ReplaceLanguageItems(LanguageInfo.AllLanguages);
        }
    }

    private void SelectLanguage(string languageCode)
    {
        foreach (ComboBoxItem item in LanguageCombo.Items)
        {
            if (item.Tag?.ToString() == languageCode)
            {
                LanguageCombo.SelectedItem = item;
                break;
            }
        }
    }
}
