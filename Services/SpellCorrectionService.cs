namespace TranslatorTray.Services;

public sealed class SpellCorrectionService
{
    private readonly LexiconService _lexicon;

    public SpellCorrectionService(LexiconService lexicon)
    {
        _lexicon = lexicon;
    }

    public bool TryAutoCorrectWord(string word, IReadOnlySet<string> protectedWords, out string corrected, out LanguageScript language)
    {
        corrected = word;
        language = LanguageScript.Unknown;

        var trimmed = word.Trim();
        if (trimmed.Length < 4 || trimmed.Length > 16)
        {
            return false;
        }

        if (protectedWords.Contains(trimmed))
        {
            return false;
        }

        language = DetectSimpleScript(trimmed);
        if (language == LanguageScript.Unknown)
        {
            return false;
        }

        if (language == LanguageScript.Russian && trimmed.Length < 5)
        {
            return false;
        }

        if (!_lexicon.TrySuggest(language, trimmed, out var suggestion, SuggestionMode.Direct))
        {
            return false;
        }

        corrected = TextTransformService.ApplyCasePattern(trimmed, suggestion);
        return corrected != word;
    }

    public bool TryAutoCorrectWord(string word, LanguageScript language, out string corrected)
    {
        corrected = word;
        var trimmed = word.Trim();
        if (trimmed.Length < 4 || trimmed.Length > 16)
        {
            return false;
        }

        if (language == LanguageScript.Unknown)
        {
            return false;
        }

        if (_lexicon.Contains(language, trimmed))
        {
            return false;
        }

        if (!_lexicon.TrySuggest(language, trimmed, out var suggestion, SuggestionMode.PostLayout))
        {
            return false;
        }

        corrected = TextTransformService.ApplyCasePattern(trimmed, suggestion);
        return corrected != word;
    }

    private static LanguageScript DetectSimpleScript(string text)
    {
        var hasEn = false;
        var hasRu = false;
        foreach (var c in text)
        {
            var lower = char.ToLowerInvariant(c);
            if (lower is >= 'a' and <= 'z')
            {
                hasEn = true;
            }
            else if (lower is >= 'а' and <= 'я' || lower == 'ё')
            {
                hasRu = true;
            }
            else if (c is '\'' or '-')
            {
                continue;
            }
            else
            {
                return LanguageScript.Unknown;
            }

            if (hasEn && hasRu)
            {
                return LanguageScript.Unknown;
            }
        }

        if (hasEn) return LanguageScript.English;
        if (hasRu) return LanguageScript.Russian;
        return LanguageScript.Unknown;
    }
}
