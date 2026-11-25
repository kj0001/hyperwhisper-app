using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using HyperWhisper.Data.Entities;
using HyperWhisper.Models;
using HyperWhisper.Services;
using HyperWhisper.Utilities;

namespace HyperWhisper.Views.Windows;

public partial class ModeEditorWindow
{
    private void LoadModeIntoEditor(Mode mode)
    {
        _isLoading = true;

        try
        {
            ModeNameBox.Text = mode.Name;

            // Load preset (migrate legacy voiceToText)
            var presetValue = mode.Preset ?? "hyper";
            if (presetValue == "voiceToText")
                presetValue = "hyper";
            foreach (ComboBoxItem item in PresetCombo.Items)
            {
                if (item.Tag?.ToString() == presetValue)
                {
                    PresetCombo.SelectedItem = item;
                    break;
                }
            }
            if (PresetCombo.SelectedIndex == -1 && PresetCombo.Items.Count > 0)
            {
                PresetCombo.SelectedIndex = 0; // Default to Hyper
            }
            PresetDescText.Text = PresetTypeExtensions.FromString(presetValue).ToDescription();

            // Show custom instructions if preset is Custom
            CustomInstructionsPanel.Visibility = presetValue == "custom" ? Visibility.Visible : Visibility.Collapsed;
            CustomInstructionsBox.Text = mode.CustomInstructions ?? "";

            // Select saved model in unified combo using prefixed tag
            var savedEngine = mode.LocalEngine ?? "whisper";
            var savedModelId = savedEngine == "parakeet"
                ? (mode.LocalParakeetModel ?? "parakeet-v2")
                : (mode.ModelType ?? "base");
            var savedTag = $"{savedEngine}:{savedModelId}";

            bool foundLocalModel = false;
            foreach (ComboBoxItem item in LocalModelCombo.Items)
            {
                if (item.Tag?.ToString() == savedTag)
                {
                    LocalModelCombo.SelectedItem = item;
                    foundLocalModel = true;
                    break;
                }
            }
            if (!foundLocalModel && LocalModelCombo.Items.Count > 0)
            {
                LocalModelCombo.SelectedIndex = 0;
            }
            UpdateLocalModelStatus();

            // CLOUD PROVIDER AND MODEL SELECTION (BYOK only; legacy HyperWhisper Cloud modes
            // fall back gracefully to On-device).
            var cloudProviderTag = mode.CloudProvider ?? "";
            var cloudProvider = CloudTranscriptionProviderExtensions.FromIdentifier(cloudProviderTag);
            var transcriptionSource = LocalModeModel.IsCloud(mode) && cloudProvider.RequiresApiKey()
                ? "yourprovider"
                : "ondevice";

            if (transcriptionSource == "yourprovider")
            {
                bool foundCloudProvider = false;
                foreach (ComboBoxItem item in CloudProviderCombo.Items)
                {
                    // Case-INSENSITIVE: shared core folds cloud provider ids to lowercase
                    // (e.g. "geminitranscribe" vs XAML Tag "geminiTranscribe").
                    if (string.Equals(item.Tag?.ToString(), cloudProviderTag, StringComparison.OrdinalIgnoreCase))
                    {
                        CloudProviderCombo.SelectedItem = item;
                        foundCloudProvider = true;
                        break;
                    }
                }
                if (!foundCloudProvider && CloudProviderCombo.Items.Count > 0)
                {
                    CloudProviderCombo.SelectedIndex = 0;
                }
            }

            // Reflect the source in the segmented control (gated by _isLoading).
            SetTranscriptionSourceSegment(transcriptionSource);

            LocalModelPanel.Visibility = transcriptionSource == "ondevice" ? Visibility.Visible : Visibility.Collapsed;
            CloudProviderPanel.Visibility = transcriptionSource == "yourprovider" ? Visibility.Visible : Visibility.Collapsed;
            CloudModelPanel.Visibility = transcriptionSource == "yourprovider" ? Visibility.Visible : Visibility.Collapsed;

            if (transcriptionSource == "yourprovider")
            {
                // Select the saved cloud model (resolve legacy provider-specific IDs)
                var rawCloudModelId = mode.CloudTranscriptionModel ?? CloudTranscriptionModels.GetModelsForProvider(cloudProvider).FirstOrDefault()?.Id ?? "whisper-1";
                var cloudModelId = CloudTranscriptionModels.ResolveModelAlias(rawCloudModelId, cloudProvider);
                LoadCloudModels(cloudProvider, cloudModelId);

                bool foundCloudModel = false;
                foreach (ComboBoxItem item in CloudModelCombo.Items)
                {
                    if (item.Tag?.ToString() == cloudModelId)
                    {
                        CloudModelCombo.SelectedItem = item;
                        foundCloudModel = true;
                        break;
                    }
                }
                if (!foundCloudModel && CloudModelCombo.Items.Count > 0)
                {
                    CloudModelCombo.SelectedIndex = 0;
                }
            }

            // Filter language list for selected model before selecting saved language
            UpdateLanguagesForSelectedModel();

            // Load Gemini custom prompt
            var geminiPrompt = mode.GeminiCustomPrompt ?? "";
            GeminiCustomPromptBox.Text = geminiPrompt;
            GeminiCustomPromptCharCount.Text = $"{geminiPrompt.Length}/2000";
            GeminiCustomPromptPlaceholder.Visibility = string.IsNullOrEmpty(geminiPrompt)
                ? Visibility.Visible : Visibility.Collapsed;
            GeminiCustomPromptPanel.Visibility = cloudProvider == CloudTranscriptionProvider.Gemini && transcriptionSource == "yourprovider"
                ? Visibility.Visible : Visibility.Collapsed;

            bool foundLanguage = false;
            foreach (ComboBoxItem item in LanguageCombo.Items)
            {
                if (item.Tag?.ToString() == mode.Language)
                {
                    LanguageCombo.SelectedItem = item;
                    foundLanguage = true;
                    break;
                }
            }
            if (!foundLanguage && LanguageCombo.Items.Count > 0)
            {
                LanguageCombo.SelectedIndex = 0;
            }

            // Load punctuation toggles
            PunctuationCheck.IsChecked = mode.Punctuation;
            RemoveTrailingPeriodCheck.IsChecked = mode.RemoveTrailingPeriod;
            CapitalizationCheck.IsChecked = mode.Capitalization;

            var ppProvider = PostProcessingProviderExtensions.NormalizeStorageValue(mode.PostProcessingProvider);
            var isLegacyHwCloudPp = string.IsNullOrEmpty(ppProvider)
                || string.Equals(ppProvider, "hyperwhispercloud", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ppProvider, "hyperwhisper", StringComparison.OrdinalIgnoreCase);
            var ppEnabled = mode.PostProcessingMode != 0 && !isLegacyHwCloudPp;

            PostProcessingCheck.IsChecked = ppEnabled;
            PostProcessingSettingsPanel.Visibility = ppEnabled ? Visibility.Visible : Visibility.Collapsed;

            // Punctuation & capitalization are LLM instructions — only show when post-processing is enabled
            PunctuationCheck.Visibility = ppEnabled ? Visibility.Visible : Visibility.Collapsed;
            CapitalizationCheck.Visibility = ppEnabled ? Visibility.Visible : Visibility.Collapsed;
            RemoveTrailingPeriodCheck.Visibility = ppEnabled
                ? (mode.Punctuation ? Visibility.Visible : Visibility.Collapsed)
                : Visibility.Visible;

            if (ppEnabled)
            {
                // Map the stored PP provider to one of the two source segments.
                var ppSource = ppProvider == PostProcessingProvider.LocalLlm.ToStringValue() ? "ondevice" : "yourprovider";
                SetPostProcessingSourceSegment(ppSource);

                // Local LLM is reached via the On-device segment (the provider
                // panel stays hidden); BYOK shows the provider picker.
                PostProcessingProviderPanel.Visibility = ppSource == "yourprovider"
                    ? Visibility.Visible : Visibility.Collapsed;
                PostProcessingModelPanel.Visibility = Visibility.Visible;

                if (ppSource == "yourprovider")
                {
                    foreach (var item in PostProcessingProviderCombo.Items.OfType<ComboBoxItem>())
                    {
                        if (item.Tag?.ToString() == ppProvider)
                        {
                            PostProcessingProviderCombo.SelectedItem = item;
                            break;
                        }
                    }
                    if (PostProcessingProviderCombo.SelectedIndex == -1 && PostProcessingProviderCombo.Items.Count > 0)
                        PostProcessingProviderCombo.SelectedIndex = 0;
                }

                // Populate the model picker for the resolved provider (Local LLM
                // models for On-device; the BYOK provider's models otherwise).
                var modelProvider = ppSource == "ondevice"
                    ? PostProcessingProvider.LocalLlm
                    : PostProcessingProviderExtensions.FromString(
                        (PostProcessingProviderCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? ppProvider!);
                LoadPostProcessingModels(modelProvider);

                var ppModelSource = ppSource == "ondevice"
                    ? mode.LocalPostProcessingModel ?? mode.LanguageModel
                    : mode.LanguageModel;
                var ppModel = LanguageModelInfo.MigrateModelId(ppModelSource);
                bool foundPPModel = false;
                foreach (ComboBoxItem item in PostProcessingModelCombo.Items)
                {
                    if (item.Tag?.ToString() == ppModel)
                    {
                        PostProcessingModelCombo.SelectedItem = item;
                        foundPPModel = true;
                        break;
                    }
                }
                if (!foundPPModel && PostProcessingModelCombo.Items.Count > 0)
                {
                    PostProcessingModelCombo.SelectedIndex = 0;
                }

                // Load English spelling
                SelectEnglishSpelling(mode.EnglishSpelling);

                ProfanityFilterCheck.IsChecked = mode.ProfanityFilter;

                // Load user system prompt
                var userPrompt = mode.UserSystemPrompt ?? "";
                var hasUserPrompt = !string.IsNullOrWhiteSpace(userPrompt);
                UserPromptCheck.IsChecked = hasUserPrompt;
                UserPromptPanel.Visibility = hasUserPrompt ? Visibility.Visible : Visibility.Collapsed;
                UserPromptBox.Text = userPrompt;
                UserPromptCharCount.Text = $"{userPrompt.Length}/8000";
            }
            else
            {
                // Post-processing is off - set defaults for when it's enabled.
                SetPostProcessingSourceSegment(PlatformHelper.SupportsLocalLlmPostProcessing ? "ondevice" : "yourprovider");
                ApplyPostProcessingSourcePanels();

                SelectEnglishSpelling(mode.EnglishSpelling);
                ProfanityFilterCheck.IsChecked = mode.ProfanityFilter;
                UserPromptCheck.IsChecked = false;
                UserPromptPanel.Visibility = Visibility.Collapsed;
                UserPromptBox.Text = mode.UserSystemPrompt ?? "";
            }

            // Update English spelling visibility based on language
            UpdateEnglishSpellingVisibility();

            UpdateApiKeyWarning(SelectedProviderType());

            // Update vocabulary and model warnings
            UpdateAllWarnings();

            // Update language dropdown state for English-only models
            AutoSelectEnglishForModel();
        }
        finally
        {
            _isLoading = false;
        }
    }

}
