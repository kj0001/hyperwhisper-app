using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyperWhisper.Localization;
using HyperWhisper.Models;
using HyperWhisper.ViewModels.Base;

namespace HyperWhisper.ViewModels.Onboarding;

public sealed partial class OnboardingFlowViewModel : ViewModelBase
{
    // =========================================================================
    // SETUP STEP
    // =========================================================================

    private string? _setupErrorMessage;

    /// <summary>
    /// Bug 2: the single error surface for the setup step, fed by Whisper AND
    /// Parakeet download failures and credential write failures,
    /// whichever matches the selected source.
    /// </summary>
    public string? SetupErrorMessage
    {
        get => _setupErrorMessage;
        private set
        {
            if (SetProperty(ref _setupErrorMessage, value))
                OnPropertyChanged(nameof(HasSetupError));
        }
    }

    public bool HasSetupError => !string.IsNullOrEmpty(SetupErrorMessage);

    /// <summary>The curated on-device shortlist, resolved live from the catalog.</summary>
    public IReadOnlyList<OnboardingModelSelection> AvailableModels => _catalog.Models;

    public bool IsInstalled(OnboardingModelSelection model) => _catalog.IsInstalled(model);

    public bool IsSelectedModelInstalled => SelectedModel is not null && _catalog.IsInstalled(SelectedModel);

    public bool IsSelectedModelDownloading => SelectedModel is not null && _catalog.IsDownloading(SelectedModel);

    public double SelectedModelProgress => SelectedModel is null ? 0 : _catalog.Progress(SelectedModel);

    [RelayCommand]
    public void StartSelectedModelDownload()
    {
        if (!_isLive || SelectedModel is not { } model)
            return;

        _catalog.StartDownload(model);
        RefreshSetupError();
        OnDownloadActivity(this, EventArgs.Empty);
    }

    [RelayCommand]
    public void SaveProviderKey() => ProbeAndPersistProviderKey();

    /// <summary>
    /// One error property for the setup step, per selected source. The on-device
    /// branch reads whichever engine the SELECTED model belongs to, which is what
    /// makes Parakeet failures visible.
    /// </summary>
    private void RefreshSetupError()
    {
        SetupErrorMessage = SelectedSource switch
        {
            OnboardingSourceKind.OnDevice =>
                SelectedModel is null ? null : _downloadErrors.Message(SelectedModel.Kind),
            OnboardingSourceKind.YourProvider => _providerErrorMessage,
            _ => null
        };

        RaiseGateChanged();
    }

}
