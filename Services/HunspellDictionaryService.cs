using System.Collections.Concurrent;
using WeCantSpell.Hunspell;

namespace TranslatorTray.Services;

public sealed class HunspellDictionaryService
{
    private readonly Dictionary<LanguageScript, WordList?> _wordLists = [];
    private readonly ConcurrentDictionary<(LanguageScript Language, string Word), bool> _isCorrectCache = new();
    private readonly ConcurrentDictionary<(LanguageScript Language, string Word, SuggestionMode Mode), string?> _suggestionCache = new();

    public HunspellDictionaryService(string baseDirectory)
    {
        var hunspellDir = Path.Combine(baseDirectory, "Data", "hunspell");
        _wordLists[LanguageScript.English] = TryLoad(Path.Combine(hunspellDir, "en_US.dic"));
        _wordLists[LanguageScript.Russian] = TryLoad(Path.Combine(hunspellDir, "ru_RU.dic"));
    }

    public bool IsAvailable => _wordLists.Values.Any(w => w is not null);

    public bool IsCorrect(LanguageScript language, string word)
    {
        var normalized = Normalize(word);
        if (string.IsNullOrEmpty(normalized))
        {
            return false;
        }

        return _isCorrectCache.GetOrAdd((language, normalized), key =>
        {
            var wordList = GetWordList(key.Language);
            return wordList is not null && wordList.Check(key.Word);
        });
    }

    public bool TrySuggest(LanguageScript language, string word, Func<string, bool> validator, SuggestionMode mode, out string suggestion)
    {
        suggestion = word;
        var normalized = Normalize(word);
        if (string.IsNullOrEmpty(normalized))
        {
            return false;
        }

        var cacheKey = (language, normalized, mode);
        var cached = _suggestionCache.GetOrAdd(cacheKey, key =>
        {
            var wordList = GetWordList(key.Language);
            if (wordList is null)
            {
                return null;
            }

            foreach (var candidate in wordList.Suggest(key.Word))
            {
                if (!string.IsNullOrWhiteSpace(candidate) && validator(candidate))
                {
                    return candidate;
                }
            }

            return null;
        });

        if (string.IsNullOrWhiteSpace(cached))
        {
            return false;
        }

        suggestion = cached;
        return true;
    }

    private WordList? GetWordList(LanguageScript language) => _wordLists.GetValueOrDefault(language);

    private static WordList? TryLoad(string dictionaryPath)
    {
        if (!File.Exists(dictionaryPath))
        {
            return null;
        }

        try
        {
            return WordList.CreateFromFiles(dictionaryPath);
        }
        catch
        {
            return null;
        }
    }

    private static string Normalize(string value) =>
        new(value.Trim().ToLowerInvariant().Where(c => char.IsLetter(c) || c is '\'' or '-').ToArray());
}
