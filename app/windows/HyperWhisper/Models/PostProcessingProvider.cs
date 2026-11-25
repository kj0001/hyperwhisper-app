// POST-PROCESSING PROVIDER ENUM
// Defines the AI providers available for post-processing transcriptions.
// Each provider has a corresponding API endpoint and authentication method.

using HyperWhisper.Localization;

namespace HyperWhisper.Models;

/// <summary>
/// AI providers that can be used for post-processing transcriptions.
/// </summary>
public enum PostProcessingProvider
{
    /// <summary>No post-processing - returns raw transcription.</summary>
    None,

    // HyperWhisperCloud removed (legacy values map to None for runtime, while
    // NormalizeStorageValue / ToUniversalStorageValue preserve strings for backup parity).

    /// <summary>OpenAI API (GPT-4.1 models).</summary>
    OpenAI,

    /// <summary>Anthropic API (Claude models).</summary>
    Anthropic,

    /// <summary>Groq API (Llama/Mixtral models - fast inference).</summary>
    Groq,

    /// <summary>xAI Grok API (Grok models - fast inference).</summary>
    Grok,

    /// <summary>Google Gemini API (Gemini models - fast and efficient).</summary>
    Gemini,

    /// <summary>Cerebras API (Llama models - ultra-fast inference).</summary>
    Cerebras,

    /// <summary>Mistral API (Mistral models - fast, multilingual). Shares the Mistral STT key.</summary>
    Mistral,

    /// <summary>Local on-device LLM (offline post-processing).</summary>
    LocalLlm
}

/// <summary>
/// Extension methods for PostProcessingProvider enum.
/// Provides conversion utilities for display, serialization, and configuration.
/// </summary>
public static class PostProcessingProviderExtensions
{
    /// <summary>
    /// Gets the human-readable display name for the provider.
    /// Used in UI dropdowns and labels.
    /// </summary>
    public static string ToDisplayName(this PostProcessingProvider provider) => provider switch
    {
        PostProcessingProvider.None => Loc.S("common.off"),
        PostProcessingProvider.OpenAI => Loc.S("provider.openai"),
        PostProcessingProvider.Anthropic => Loc.S("provider.anthropic"),
        PostProcessingProvider.Groq => Loc.S("provider.groq"),
        PostProcessingProvider.Grok => Loc.S("provider.grok"),
        PostProcessingProvider.Gemini => Loc.S("provider.gemini"),
        PostProcessingProvider.Cerebras => Loc.S("provider.cerebras"),
        PostProcessingProvider.Mistral => Loc.S("provider.mistral"),
        PostProcessingProvider.LocalLlm => Loc.S("provider.localLlm"),
        _ => provider.ToString()
    };

    /// <summary>
    /// Gets the string value for JSON serialization.
    /// Used when saving mode settings to disk.
    /// </summary>
    public static string ToStringValue(this PostProcessingProvider provider) => provider switch
    {
        PostProcessingProvider.None => "none",
        PostProcessingProvider.OpenAI => "openai",
        PostProcessingProvider.Anthropic => "anthropic",
        PostProcessingProvider.Groq => "groq",
        PostProcessingProvider.Grok => "grok",
        PostProcessingProvider.Gemini => "gemini",
        PostProcessingProvider.Cerebras => "cerebras",
        PostProcessingProvider.Mistral => "mistral",
        PostProcessingProvider.LocalLlm => "local_llm",
        _ => "none"
    };

    /// <summary>
    /// Parses a string value back to the enum.
    /// Used when loading mode settings from disk.
    /// </summary>
    public static PostProcessingProvider FromString(string? value) => value?.ToLowerInvariant() switch
    {
        "openai" => PostProcessingProvider.OpenAI,
        "anthropic" => PostProcessingProvider.Anthropic,
        "groq" => PostProcessingProvider.Groq,
        "grok" => PostProcessingProvider.Grok,
        "gemini" => PostProcessingProvider.Gemini,
        "cerebras" => PostProcessingProvider.Cerebras,
        "mistral" => PostProcessingProvider.Mistral,
        "local_llm" => PostProcessingProvider.LocalLlm,
        // `localLLM` is the shared catalog's provider id
        // (`SharedModelsCatalog.LocalLlmKey`); Local API callers send it.
        "localllm" => PostProcessingProvider.LocalLlm,
        "local" => PostProcessingProvider.LocalLlm,
        _ => PostProcessingProvider.None
    };

    private static bool IsLegacyHyperWhisperCloudValue(string value) =>
        value.ToLowerInvariant() is "hyperwhispercloud" or "hyperwhisper" or "hyperwhisper_cloud";

    /// <summary>
    /// Normalizes provider strings from other platforms while preserving custom endpoint IDs
    /// and legacy backup storage strings.
    /// </summary>
    public static string? NormalizeStorageValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        if (CustomPostProcessingEndpoint.IsCustomProviderString(value))
            return value;

        if (IsLegacyHyperWhisperCloudValue(value))
            return "hyperwhispercloud";

        var provider = FromString(value);
        return provider == PostProcessingProvider.None ? value : provider.ToStringValue();
    }

    /// <summary>
    /// Converts Windows provider strings to the shared backup/schema value.
    /// </summary>
    public static string? ToUniversalStorageValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        if (CustomPostProcessingEndpoint.IsCustomProviderString(value))
            return value;

        if (IsLegacyHyperWhisperCloudValue(value))
            return "hyperwhisper";

        var provider = FromString(value);
        return provider == PostProcessingProvider.None ? value : provider.ToStringValue();
    }

    /// <summary>
    /// Gets the setting name used for storing the API key.
    /// Each provider has a unique key stored via ApiKeyService.
    /// </summary>
    public static string GetApiKeySettingName(this PostProcessingProvider provider) => provider switch
    {
        PostProcessingProvider.OpenAI => "OpenAIApiKey",
        PostProcessingProvider.Anthropic => "AnthropicApiKey",
        PostProcessingProvider.Groq => "GroqApiKey",
        PostProcessingProvider.Grok => "GrokApiKey",
        PostProcessingProvider.Gemini => "GeminiApiKey",
        PostProcessingProvider.Cerebras => "CerebrasApiKey",
        // Mistral post-processing reuses the existing Mistral STT key store
        // (TranscriptionApiKeyType.Mistral => "MistralApiKey"), mirroring how macOS
        // shares `mistralAPIKey` across transcription and post-processing.
        PostProcessingProvider.Mistral => "MistralApiKey",
        PostProcessingProvider.LocalLlm => "",
        _ => ""
    };

    /// <summary>
    /// Returns true if this provider requires an API key.
    /// </summary>
    public static bool RequiresApiKey(this PostProcessingProvider provider) => provider switch
    {
        PostProcessingProvider.None => false,
        PostProcessingProvider.LocalLlm => false,
        _ => true
    };
}
