namespace TranslatorTray.Services;

public sealed class LexiconService
{
    private readonly HashSet<string> _englishWords;
    private readonly HashSet<string> _russianWords;

    public LexiconService(string baseDirectory)
    {
        var dataDir = Path.Combine(baseDirectory, "Data");
        _englishWords = Load(Path.Combine(dataDir, "en_top10k.txt"), IsEnglishWord);
        _russianWords = Load(Path.Combine(dataDir, "ru_top10k.txt"), IsRussianWord);
    }

    public bool Contains(LanguageScript language, string word)
    {
        var normalized = Normalize(word);
        if (normalized.Length < 2)
        {
            return false;
        }

        return language switch
        {
            LanguageScript.English => _englishWords.Contains(normalized),
            LanguageScript.Russian => _russianWords.Contains(normalized),
            _ => false
        };
    }

    public int EnglishCount => _englishWords.Count;
    public int RussianCount => _russianWords.Count;

    private static HashSet<string> Load(string path, Func<string, bool> validator)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
        {
            return set;
        }

        foreach (var line in File.ReadLines(path))
        {
            var word = Normalize(line);
            if (word.Length < 2 || !validator(word))
            {
                continue;
            }

            set.Add(word);
        }

        return set;
    }

    private static string Normalize(string value)
    {
        var chars = value
            .Trim()
            .ToLowerInvariant()
            .Where(c => char.IsLetter(c) || c == '\'' || c == '-')
            .ToArray();
        return new string(chars);
    }

    private static bool IsEnglishWord(string word) => word.All(c => (c >= 'a' && c <= 'z') || c == '\'' || c == '-');
    private static bool IsRussianWord(string word) => word.All(c => (c >= 'а' && c <= 'я') || c == 'ё' || c == '-');
}
