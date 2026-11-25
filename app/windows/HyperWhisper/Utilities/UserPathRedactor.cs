using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HyperWhisper.Utilities;

/// <summary>
/// Replaces the signed-in user's Windows identifiers (profile directory,
/// local-app-data directory, and bare account name) with fixed tokens before
/// messages or paths are written to disk logs.
/// </summary>
internal static class UserPathRedactor
{
    internal static string RedactUserIdentifiers(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return Redact(value, BuildLiveRedactionRules());
    }

    internal static string RedactUserIdentifiers(
        string? value,
        string? userProfileDirectory,
        string? localAppDataDirectory,
        string? userName)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return Redact(
            value,
            BuildRedactionRules(userProfileDirectory, localAppDataDirectory, userName));
    }

    private enum MatchBoundary
    {
        DirectorySegment,
        WholeWord
    }

    private readonly record struct RedactionRule(string Identifier, string Token, MatchBoundary Boundary);

    private static IReadOnlyList<RedactionRule> BuildLiveRedactionRules()
    {
        return BuildRedactionRules(
            ReadIdentifier(static () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
            ReadIdentifier(static () => Environment.GetEnvironmentVariable("LOCALAPPDATA")),
            ReadIdentifier(static () => Environment.UserName));

        static string? ReadIdentifier(Func<string?> read)
        {
            try
            {
                return read();
            }
            catch
            {
                return null;
            }
        }
    }

    private static IReadOnlyList<RedactionRule> BuildRedactionRules(
        string? userProfileDirectory,
        string? localAppDataDirectory,
        string? userName)
    {
        var rules = new List<RedactionRule>(3);

        AddDirectory(userProfileDirectory, "%USERPROFILE%");
        AddDirectory(localAppDataDirectory, "%LOCALAPPDATA%");

        if (!string.IsNullOrWhiteSpace(userName))
        {
            rules.Add(new RedactionRule(userName, "%USER%", MatchBoundary.WholeWord));
        }

        return rules.OrderByDescending(rule => rule.Identifier.Length).ToList();

        void AddDirectory(string? directory, string token)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            var trimmed = directory.TrimEnd('\\', '/');
            if (trimmed.Length <= 3 || string.IsNullOrWhiteSpace(trimmed))
            {
                return;
            }

            rules.Add(new RedactionRule(trimmed, token, MatchBoundary.DirectorySegment));
        }
    }

    private static string Redact(string value, IReadOnlyList<RedactionRule> rules)
    {
        if (string.IsNullOrEmpty(value) || rules.Count == 0)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        var index = 0;

        while (index < value.Length)
        {
            var matchLength = MatchRule(value, index, rules, out var token);
            if (matchLength > 0)
            {
                builder.Append(token);
                index += matchLength;
                continue;
            }

            builder.Append(value[index]);
            index++;
        }

        return builder.ToString();
    }

    private static int MatchRule(
        string value,
        int index,
        IReadOnlyList<RedactionRule> rules,
        out string token)
    {
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            var length = rule.Identifier.Length;

            if (index + length > value.Length)
            {
                continue;
            }

            if (string.Compare(value, index, rule.Identifier, 0, length, StringComparison.OrdinalIgnoreCase) != 0)
            {
                continue;
            }

            if (!IsBoundedMatch(value, index, length, rule.Boundary))
            {
                continue;
            }

            token = rule.Token;
            return length;
        }

        token = string.Empty;
        return 0;
    }

    private static bool IsBoundedMatch(string value, int index, int length, MatchBoundary boundary)
        => boundary == MatchBoundary.DirectorySegment
            ? index + length == value.Length || IsDirectorySeparator(value[index + length])
            : (index == 0 || IsAccountNameBoundary(value[index - 1]))
                && (index + length == value.Length || IsAccountNameBoundary(value[index + length]));

    private static bool IsDirectorySeparator(char character)
        => character == '\\' || character == '/';

    private static bool IsAccountNameBoundary(char character)
        => !char.IsLetterOrDigit(character);
}
