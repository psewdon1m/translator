namespace TranslatorTray.Services;

public sealed class LexiconService
{
    private readonly HashSet<string> _englishWords;
    private readonly HashSet<string> _russianWords;
    private readonly Dictionary<int, List<string>> _englishByLength;
    private readonly Dictionary<int, List<string>> _russianByLength;

    public LexiconService(string baseDirectory)
    {
        var dataDir = Path.Combine(baseDirectory, "Data");
        _englishWords = Load(Path.Combine(dataDir, "en_top10k.txt"), IsEnglishWord, out _englishByLength);
        _russianWords = Load(Path.Combine(dataDir, "ru_top10k.txt"), IsRussianWord, out _russianByLength);
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

    public bool TrySuggest(LanguageScript language, string word, out string suggestion, SuggestionMode mode = SuggestionMode.Direct)
    {
        suggestion = word;
        var normalized = Normalize(word);
        if (normalized.Length < 4 || normalized.Length > 16)
        {
            return false;
        }

        var set = language switch
        {
            LanguageScript.English => _englishWords,
            LanguageScript.Russian => _russianWords,
            _ => null
        };
        var buckets = language switch
        {
            LanguageScript.English => _englishByLength,
            LanguageScript.Russian => _russianByLength,
            _ => null
        };

        if (set is null || buckets is null || set.Contains(normalized))
        {
            return false;
        }

        var bestCandidate = string.Empty;
        var bestDistance = int.MaxValue;
        var bestMatchingPositions = -1;
        var maxDistance = Math.Max(1, normalized.Length / 2);
        var minMatchingPositions = (normalized.Length + 1) / 2;
        var len = normalized.Length;
        if (buckets.TryGetValue(len, out var candidates))
        {
            foreach (var candidate in candidates)
            {
                if (!SharesSafeShape(normalized, candidate, mode))
                {
                    continue;
                }

                var matchingPositions = CountMatchingPositions(normalized, candidate);
                if (matchingPositions < minMatchingPositions)
                {
                    continue;
                }

                var distance = BoundedEditDistanceOrTransposition(normalized, candidate, maxDistance);
                if (distance < 0)
                {
                    continue;
                }

                if (distance < bestDistance || (distance == bestDistance && matchingPositions > bestMatchingPositions))
                {
                    bestDistance = distance;
                    bestMatchingPositions = matchingPositions;
                    bestCandidate = candidate;
                }
            }
        }

        if (bestDistance > maxDistance || string.IsNullOrEmpty(bestCandidate))
        {
            return false;
        }

        suggestion = bestCandidate;
        return true;
    }

    private static HashSet<string> Load(string path, Func<string, bool> validator, out Dictionary<int, List<string>> byLength)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        byLength = new Dictionary<int, List<string>>();
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
            if (!byLength.TryGetValue(word.Length, out var bucket))
            {
                bucket = [];
                byLength[word.Length] = bucket;
            }
            bucket.Add(word);
        }

        return set;
    }

    private static bool SharesSafeShape(string source, string candidate, SuggestionMode mode)
    {
        if (source.Length >= 1 && candidate.Length >= 1 && source[0] != candidate[0])
        {
            return false;
        }

        if (source.Length >= 4 && candidate.Length >= 4 && source[1] != candidate[1])
        {
            return false;
        }

        if (mode == SuggestionMode.Direct &&
            source.Length >= 8 &&
            candidate.Length >= 8 &&
            source[2] != candidate[2])
        {
            return false;
        }

        return true;
    }

    private static int CountMatchingPositions(string source, string candidate)
    {
        var count = 0;
        var len = Math.Min(source.Length, candidate.Length);
        for (var i = 0; i < len; i++)
        {
            if (source[i] == candidate[i])
            {
                count++;
            }
        }

        return count;
    }

    private static int BoundedEditDistanceOrTransposition(string source, string target, int maxDistance)
    {
        if (source == target)
        {
            return 0;
        }

        if (Math.Abs(source.Length - target.Length) > maxDistance)
        {
            return -1;
        }

        if (source.Length == target.Length && source.Length >= 2)
        {
            var mismatches = new List<int>(2);
            for (var i = 0; i < source.Length; i++)
            {
                if (source[i] != target[i])
                {
                    mismatches.Add(i);
                    if (mismatches.Count > 2)
                    {
                        break;
                    }
                }
            }

            if (mismatches.Count == 2)
            {
                var a = mismatches[0];
                var b = mismatches[1];
                if (b == a + 1 &&
                    source[a] == target[b] &&
                    source[b] == target[a])
                {
                    return 1;
                }
            }
        }

        var previous = new int[target.Length + 1];
        var current = new int[target.Length + 1];
        for (var j = 0; j <= target.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= source.Length; i++)
        {
            current[0] = i;
            var rowMin = current[0];
            for (var j = 1; j <= target.Length; j++)
            {
                var cost = source[i - 1] == target[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(previous[j] + 1, current[j - 1] + 1),
                    previous[j - 1] + cost);
                rowMin = Math.Min(rowMin, current[j]);
            }

            if (rowMin > maxDistance)
            {
                return -1;
            }

            (previous, current) = (current, previous);
        }

        return previous[target.Length] <= maxDistance ? previous[target.Length] : -1;
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

public enum SuggestionMode
{
    Direct = 0,
    PostLayout = 1
}
