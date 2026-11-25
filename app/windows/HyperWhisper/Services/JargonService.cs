using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace HyperWhisper.Services;

/// <summary>
/// Manages the user-editable Documents\HyperWhisper\jargon.md file used for
/// Stage 2 LLM post-processing spelling and terminology guidance.
/// </summary>
public sealed class JargonService
{
    public const int MaxCharacters = 4000;

    public const string DefaultContent = """
# Jargon & Spelling Guide

Edit this file to help post-processing spell technical terms, names, and acronyms accurately.
Changes take effect on your next dictation (no restart needed).

## Formatting Preferences
- Preserve exact casing for technical identifiers, CLI flags, file paths, and code symbols.
- Keep common acronyms uppercase (API, CLI, SDK, JSON, YAML, SQL, HTTP, HTTPS, SSH, URL, DNS, CPU, GPU, RAM, OS, UI, UX, PR, CI/CD, WSL, VM, OAuth, JWT, LLM).

## Common Speech-to-Text Corrections
- "see sharp" / "sea sharp" -> C#
- "dot net" -> .NET
- "get hub" / "git hub" -> GitHub
- "jay son" -> JSON
- "sequel" -> SQL
- "sequel lite" / "see quill light" -> SQLite
- "double you ess ell" -> WSL
- "pie thon" -> Python
- "type script" -> TypeScript
- "node jay ess" -> Node.js
- "next jay ess" -> Next.js
- "kube control" / "cube cuttle" -> kubectl
- "k8s" / "kuber net ease" -> Kubernetes
- "en pee em" -> npm
- "pee en pee em" -> pnpm
- "pie test" -> pytest
- "reg ex" -> regex
- "power shell" -> PowerShell
- "hyper whisper" -> HyperWhisper

## Personal & Project Terms
<!-- Add your own names, company terms, project names, or acronyms below -->
- 
""";

    private static readonly Regex PromptDelimiterRegex = new(
        @"--TRANSCRIPT--|--ENDTRANSCRIPT--|<<CLEANED>>|<<END>>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex UnsafeAngleBracketRegex = new(
        @"<|(?<!-)>",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static JargonService? _instance;
    private static readonly object _instanceLock = new();

    public static JargonService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_instanceLock)
                {
                    _instance ??= new JargonService();
                }
            }
            return _instance;
        }
    }

    private readonly object _cacheLock = new();
    private string? _cachedPath;
    private DateTime _cachedLastWriteUtc;
    private long _cachedLength;
    private string _cachedSanitized = string.Empty;

    private JargonService() { }

    internal static string Sanitize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var normalized = raw.Replace("\r\n", "\n", StringComparison.Ordinal)
                            .Replace('\r', '\n');
        var withoutDelimiters = PromptDelimiterRegex.Replace(normalized, string.Empty);
        var withoutTags = UnsafeAngleBracketRegex.Replace(withoutDelimiters, string.Empty).Trim();

        if (withoutTags.Length > MaxCharacters)
        {
            return withoutTags.Substring(0, MaxCharacters).TrimEnd();
        }

        return withoutTags;
    }

    public void EnsureSeededIfNeeded()
    {
        if (SettingsService.Instance.JargonFileSeeded)
        {
            return;
        }

        var path = AppPaths.JargonFilePath;
        lock (_cacheLock)
        {
            if (SettingsService.Instance.JargonFileSeeded)
            {
                return;
            }

            try
            {
                if (!File.Exists(path))
                {
                    WriteFileContents(path, DefaultContent);
                }

                SettingsService.Instance.JargonFileSeeded = true;
            }
            catch (Exception ex)
            {
                LoggingService.Warn($"JargonService: Failed to seed jargon file ({ex.GetType().Name})");
            }
        }
    }

    public void EnsureFileExistsForEdit()
    {
        var path = AppPaths.JargonFilePath;
        lock (_cacheLock)
        {
            if (!File.Exists(path))
            {
                WriteFileContents(path, DefaultContent);
                InvalidateCacheUnlocked();
            }

            SettingsService.Instance.JargonFileSeeded = true;
        }
    }

    public void ResetToDefaults()
    {
        var path = AppPaths.JargonFilePath;
        lock (_cacheLock)
        {
            WriteFileContents(path, DefaultContent);
            InvalidateCacheUnlocked();
            SettingsService.Instance.JargonFileSeeded = true;
        }
    }

    public string GetSanitizedContent(string? overridePath = null)
    {
        if (overridePath == null)
        {
            EnsureSeededIfNeeded();
        }

        var path = overridePath ?? AppPaths.JargonFilePath;

        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                lock (_cacheLock)
                {
                    if (string.Equals(_cachedPath, path, StringComparison.OrdinalIgnoreCase))
                    {
                        InvalidateCacheUnlocked();
                    }
                }
                return string.Empty;
            }

            var lastWriteUtc = info.LastWriteTimeUtc;
            var length = info.Length;

            lock (_cacheLock)
            {
                if (string.Equals(_cachedPath, path, StringComparison.OrdinalIgnoreCase) &&
                    _cachedLastWriteUtc == lastWriteUtc &&
                    _cachedLength == length)
                {
                    return _cachedSanitized;
                }
            }

            var raw = File.ReadAllText(path, Encoding.UTF8);
            var sanitized = Sanitize(raw);

            lock (_cacheLock)
            {
                _cachedPath = path;
                _cachedLastWriteUtc = lastWriteUtc;
                _cachedLength = length;
                _cachedSanitized = sanitized;
            }

            LoggingService.Debug($"JargonService: Loaded jargon guide ({sanitized.Length} chars)");
            return sanitized;
        }
        catch (Exception ex)
        {
            LoggingService.Warn($"JargonService: Failed to read jargon file ({ex.GetType().Name})");
            return string.Empty;
        }
    }

    public int GetCurrentCharacterCount(string? overridePath = null)
        => GetSanitizedContent(overridePath).Length;

    private static void WriteFileContents(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        AppPaths.ClearReadOnlyAttribute(path, nameof(JargonService));
        File.WriteAllText(path, content, Utf8NoBom);
    }

    private void InvalidateCacheUnlocked()
    {
        _cachedPath = null;
        _cachedLastWriteUtc = default;
        _cachedLength = 0;
        _cachedSanitized = string.Empty;
    }
}
