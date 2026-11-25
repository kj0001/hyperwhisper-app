using HyperWhisper.Data.Entities;
using HyperWhisper.PortableApplication.Persistence;

namespace HyperWhisper.Data;

/// <summary>
/// MODE DEFAULTS
///
/// The Windows fork's first-install mode seeder. Seeds a single local mode
/// (<see cref="DefaultModeId"/>) using Whisper <c>large-v3-turbo</c> with
/// post-processing disabled so fresh installs make zero outbound network
/// requests unless the user explicitly configures an API key or downloads a
/// local model.
/// </summary>
public static class ModeDefaults
{
    /// <summary>
    /// The seeded mode's well-known id, shared with macOS and anchored by
    /// onboarding restore-point lookups.
    /// </summary>
    public static readonly Guid DefaultModeId = PortableModeDefaults.HyperModeId;

    /// <summary>
    /// The modes a brand-new install starts with: exactly one local mode
    /// carrying the system region's spelling variant.
    /// </summary>
    public static List<Mode> GetDefaultModes()
    {
        var now = DateTime.UtcNow;
        return
        [
            new Mode
            {
                Id = DefaultModeId,
                Name = "Hyper",
                Preset = "hyper",
                Language = "auto",
                ProviderType = "local",
                LocalEngine = "whisper",
                Model = "large-v3-turbo",
                ModelType = "large-v3-turbo",
                CloudProvider = null,
                CloudAccuracyTier = string.Empty,
                CloudTranscriptionModel = null,
                PostProcessingMode = 0,
                PostProcessingProvider = null,
                CloudPostProcessingModel = null,
                EnglishSpelling = PortableModeDefaults.EnglishSpellingForCurrentRegion(),
                Punctuation = true,
                Capitalization = true,
                ProfanityFilter = false,
                CustomInstructions = null,
                EnableScreenOCR = false,
                IsDefault = true,
                IsSystemProvided = true,
                SortOrder = 0,
                CreatedDate = now,
                ModifiedDate = now
            }
        ];
    }
}
