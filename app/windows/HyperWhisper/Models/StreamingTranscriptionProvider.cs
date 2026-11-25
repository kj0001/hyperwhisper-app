namespace HyperWhisper.Models;

/// <summary>
/// Streaming transcription providers supported by the Windows settings surface.
/// Storage values intentionally match macOS AppStorage values for parity.
///
/// Adding a member is SIX edits in this file and none of them is compiler-enforced:
/// every switch below ends in a `_ =>` arm. The quiet one is IsValidStorageValue -
/// SettingsService.StreamingProvider's setter resets any value it rejects back to
/// hyperwhisperCloud, so forgetting it there makes the user's selection silently
/// revert the next time settings are saved. Then StreamingTranscriptionSessionFactory
/// (three switches) and StreamingSettingsPage (the hard-coded ComboBox list plus two
/// switches) - see the checklist in StreamingSettingsPage.xaml.cs.
/// </summary>
public enum StreamingTranscriptionProvider
{
    Deepgram,
    ElevenLabs,
    OpenAI,
    Xai,
    GeminiTranscribe
}

public static class StreamingTranscriptionProviderExtensions
{
    public static string StorageValue(this StreamingTranscriptionProvider provider) => provider switch
    {
        StreamingTranscriptionProvider.Deepgram => "deepgram",
        StreamingTranscriptionProvider.ElevenLabs => "elevenLabs",
        StreamingTranscriptionProvider.OpenAI => "openAI",
        StreamingTranscriptionProvider.Xai => "xai",
        StreamingTranscriptionProvider.GeminiTranscribe => "geminiTranscribe",
        _ => "deepgram"
    };

    public static string DisplayName(this StreamingTranscriptionProvider provider) => provider switch
    {
        StreamingTranscriptionProvider.Deepgram => "Deepgram",
        StreamingTranscriptionProvider.ElevenLabs => "ElevenLabs",
        StreamingTranscriptionProvider.OpenAI => "OpenAI",
        StreamingTranscriptionProvider.Xai => "SpaceXAI",
        StreamingTranscriptionProvider.GeminiTranscribe => "Gemini 3.5 Transcribe",
        _ => "Deepgram"
    };

    public static bool RequiresApiKey(this StreamingTranscriptionProvider provider) => true;

    public static bool IsValidStorageValue(string? value) =>
        value is "hyperwhisperCloud" or "deepgram" or "elevenLabs" or "openAI" or "xai" or "geminiTranscribe";

    public static bool TryFromStorageValue(string? value, out StreamingTranscriptionProvider provider)
    {
        switch (value)
        {
            case "deepgram":
                provider = StreamingTranscriptionProvider.Deepgram;
                return true;
            case "elevenLabs":
                provider = StreamingTranscriptionProvider.ElevenLabs;
                return true;
            case "openAI":
                provider = StreamingTranscriptionProvider.OpenAI;
                return true;
            case "xai":
                provider = StreamingTranscriptionProvider.Xai;
                return true;
            case "geminiTranscribe":
                provider = StreamingTranscriptionProvider.GeminiTranscribe;
                return true;
            default:
                provider = default;
                return false;
        }
    }

    public static StreamingTranscriptionProvider FromStorageValue(string? value) =>
        TryFromStorageValue(value, out var provider)
            ? provider
            : StreamingTranscriptionProvider.Deepgram;
}
