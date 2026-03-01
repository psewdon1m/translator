namespace TranslatorTray.Services;

public sealed class SpellCorrectionService
{
    private readonly LexiconService _lexicon;
    private readonly HunspellDictionaryService _hunspell;
    private readonly WindowsSpellCheckerService _windowsSpellChecker;
    private readonly ProtectedTermsService _protectedTerms;

    public SpellCorrectionService(
        LexiconService lexicon,
        HunspellDictionaryService hunspell,
        WindowsSpellCheckerService windowsSpellChecker,
        ProtectedTermsService protectedTerms)
    {
        _lexicon = lexicon;
        _hunspell = hunspell;
        _windowsSpellChecker = windowsSpellChecker;
        _protectedTerms = protectedTerms;
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

        if (_protectedTerms.Contains(trimmed))
        {
            return false;
        }

        language = DetectSimpleScript(trimmed);
        if (language == LanguageScript.Unknown)
        {
            return false;
        }

        if (_protectedTerms.Contains(trimmed))
        {
            return false;
        }

        if (language == LanguageScript.Russian && trimmed.Length < 5)
        {
            return false;
        }

        var detectedLanguage = language;
        if (TryFastAdjacentSwapCorrection(trimmed, detectedLanguage, SuggestionMode.Direct, out var swappedSuggestion))
        {
            corrected = TextTransformService.ApplyCasePattern(trimmed, swappedSuggestion);
            return corrected != word;
        }

        if (_hunspell.IsCorrect(language, trimmed))
        {
            return false;
        }

        if (_hunspell.TrySuggest(language, trimmed,
                candidate => _lexicon.IsSafeSuggestion(detectedLanguage, trimmed, candidate, SuggestionMode.Direct),
                SuggestionMode.Direct,
                out var hunspellSuggestion))
        {
            corrected = TextTransformService.ApplyCasePattern(trimmed, hunspellSuggestion);
            return corrected != word;
        }

        if (_windowsSpellChecker.IsCorrect(language, trimmed))
        {
            return false;
        }

        if (_windowsSpellChecker.TrySuggest(language, trimmed,
                candidate => _lexicon.IsSafeSuggestion(detectedLanguage, trimmed, candidate, SuggestionMode.Direct),
                out var windowsSuggestion))
        {
            corrected = TextTransformService.ApplyCasePattern(trimmed, windowsSuggestion);
            return corrected != word;
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

        if (TryFastAdjacentSwapCorrection(trimmed, language, SuggestionMode.PostLayout, out var swappedSuggestion))
        {
            corrected = TextTransformService.ApplyCasePattern(trimmed, swappedSuggestion);
            return corrected != word;
        }

        if (_hunspell.IsCorrect(language, trimmed))
        {
            return false;
        }

        if (_hunspell.TrySuggest(language, trimmed,
                candidate => _lexicon.IsSafeSuggestion(language, trimmed, candidate, SuggestionMode.PostLayout),
                SuggestionMode.PostLayout,
                out var hunspellSuggestion))
        {
            corrected = TextTransformService.ApplyCasePattern(trimmed, hunspellSuggestion);
            return corrected != word;
        }

        if (_windowsSpellChecker.IsCorrect(language, trimmed))
        {
            return false;
        }

        if (_windowsSpellChecker.TrySuggest(language, trimmed,
                candidate => _lexicon.IsSafeSuggestion(language, trimmed, candidate, SuggestionMode.PostLayout),
                out var windowsSuggestion))
        {
            corrected = TextTransformService.ApplyCasePattern(trimmed, windowsSuggestion);
            return corrected != word;
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

    private bool TryFastAdjacentSwapCorrection(string word, LanguageScript language, SuggestionMode mode, out string corrected)
    {
        corrected = word;
        if (word.Length < 4)
        {
            return false;
        }

        string? best = null;
        for (var i = 0; i < word.Length - 1; i++)
        {
            if (word[i] == word[i + 1])
            {
                continue;
            }

            var chars = word.ToCharArray();
            (chars[i], chars[i + 1]) = (chars[i + 1], chars[i]);
            var candidate = new string(chars);

            if (!_lexicon.IsSafeSuggestion(language, word, candidate, mode))
            {
                continue;
            }

            if (_lexicon.Contains(language, candidate) || _hunspell.IsCorrect(language, candidate))
            {
                if (best is not null && !string.Equals(best, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                best = candidate;
            }
        }

        if (string.IsNullOrWhiteSpace(best))
        {
            return false;
        }

        corrected = best;
        return true;
    }
}
