using System.Text;

namespace TranslatorTray.Services;

public sealed class TextTransformService
{
    private readonly LexiconService? _lexicon;
    private readonly PuntoDataService? _puntoData;
    private readonly ProtectedTermsService? _protectedTerms;

    public TextTransformService()
    {
    }

    public TextTransformService(LexiconService lexicon)
    {
        _lexicon = lexicon;
    }

    public TextTransformService(LexiconService lexicon, PuntoDataService puntoData, ProtectedTermsService protectedTerms)
    {
        _lexicon = lexicon;
        _puntoData = puntoData;
        _protectedTerms = protectedTerms;
    }

    private static readonly Dictionary<char, char> EnToRu = new()
    {
        ['`'] = 'ё', ['q'] = 'й', ['w'] = 'ц', ['e'] = 'у', ['r'] = 'к', ['t'] = 'е', ['y'] = 'н', ['u'] = 'г',
        ['i'] = 'ш', ['o'] = 'щ', ['p'] = 'з', ['['] = 'х', [']'] = 'ъ', ['a'] = 'ф', ['s'] = 'ы', ['d'] = 'в',
        ['f'] = 'а', ['g'] = 'п', ['h'] = 'р', ['j'] = 'о', ['k'] = 'л', ['l'] = 'д', [';'] = 'ж', ['\''] = 'э',
        ['z'] = 'я', ['x'] = 'ч', ['c'] = 'с', ['v'] = 'м', ['b'] = 'и', ['n'] = 'т', ['m'] = 'ь', [','] = 'б',
        ['.'] = 'ю', ['/'] = '.'
    };

    private static readonly Dictionary<char, char> RuToEn = new()
    {
        ['ё'] = '`', ['й'] = 'q', ['ц'] = 'w', ['у'] = 'e', ['к'] = 'r', ['е'] = 't', ['н'] = 'y', ['г'] = 'u',
        ['ш'] = 'i', ['щ'] = 'o', ['з'] = 'p', ['х'] = '[', ['ъ'] = ']', ['ф'] = 'a', ['ы'] = 's', ['в'] = 'd',
        ['а'] = 'f', ['п'] = 'g', ['р'] = 'h', ['о'] = 'j', ['л'] = 'k', ['д'] = 'l', ['ж'] = ';', ['э'] = '\'',
        ['я'] = 'z', ['ч'] = 'x', ['с'] = 'c', ['м'] = 'v', ['и'] = 'b', ['т'] = 'n', ['ь'] = 'm', ['б'] = ',',
        ['ю'] = '.', ['.'] = '/'
    };

    public string ConvertLayout(string input, out bool changed)
    {
        var sb = new StringBuilder(input.Length);
        changed = false;
        foreach (var ch in input)
        {
            var converted = ConvertChar(ch, out var cChanged);
            changed |= cChanged;
            sb.Append(converted);
        }

        return sb.ToString();
    }

    public string InvertCase(string input)
    {
        var chars = input.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (char.IsLetter(c))
            {
                chars[i] = char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c);
            }
        }

        return new string(chars);
    }

    public bool TryAutoConvertWord(string word, IReadOnlySet<string> protectedWords, out string converted, out LanguageScript target)
    {
        converted = word;
        target = LanguageScript.Unknown;
        if (string.IsNullOrWhiteSpace(word))
        {
            return false;
        }

        var trimmed = word.Trim();
        if (trimmed.Length < 3)
        {
            return false;
        }

        if (protectedWords.Contains(trimmed))
        {
            return false;
        }

        if (_protectedTerms?.Contains(trimmed) == true)
        {
            return false;
        }

        var sourceScript = DetectDominantScript(word);
        if (sourceScript == LanguageScript.Unknown)
        {
            return false;
        }

        if (_puntoData is not null && _puntoData.TryWholeWordMapping(word, sourceScript, out var mappedByDictionary))
        {
            converted = ApplyCasePattern(word, mappedByDictionary);
            target = sourceScript == LanguageScript.English ? LanguageScript.Russian : LanguageScript.English;
            return converted != word;
        }

        converted = ConvertLayout(word, out var changed);
        if (!changed || converted == word)
        {
            return false;
        }

        target = sourceScript == LanguageScript.English ? LanguageScript.Russian : LanguageScript.English;

        if (_puntoData is not null && _puntoData.IsTrigger(word))
        {
            // Punto trigger lists encode many common bad patterns; trust them as an early signal.
            return true;
        }

        if (_lexicon is not null)
        {
            var sourceKnown = _lexicon.Contains(sourceScript, word);
            var targetKnown = _lexicon.Contains(target, converted);
            if (targetKnown && !sourceKnown)
            {
                return true;
            }

            if (sourceKnown && !targetKnown)
            {
                return false;
            }
        }

        var sourceScore = sourceScript switch
        {
            LanguageScript.English => ScoreEnglishLike(word),
            LanguageScript.Russian => ScoreRussianLike(word),
            _ => 0
        };

        var targetScore = target switch
        {
            LanguageScript.English => ScoreEnglishLike(converted),
            LanguageScript.Russian => ScoreRussianLike(converted),
            _ => 0
        };

        // Convert only when the converted word looks materially more plausible.
        return targetScore >= sourceScore + 2;
    }

    public LanguageScript DetectDominantScript(string text)
    {
        var en = 0;
        var ru = 0;
        foreach (var c in text)
        {
            var lower = char.ToLowerInvariant(c);
            if ((lower >= 'a' && lower <= 'z') || EnToRu.ContainsKey(lower))
            {
                en++;
                continue;
            }

            if ((lower >= 'а' && lower <= 'я') || lower == 'ё' || RuToEn.ContainsKey(lower))
            {
                ru++;
            }
        }

        if (en == 0 && ru == 0) return LanguageScript.Unknown;
        return en >= ru ? LanguageScript.English : LanguageScript.Russian;
    }

    public static int GetPlausibilityScore(LanguageScript language, string text) => language switch
    {
        LanguageScript.English => ScoreEnglishLike(text),
        LanguageScript.Russian => ScoreRussianLike(text),
        _ => 0
    };

    private static char ConvertChar(char ch, out bool changed)
    {
        var isUpper = char.IsLetter(ch) && char.IsUpper(ch);
        var lower = char.ToLowerInvariant(ch);

        if (EnToRu.TryGetValue(lower, out var ru))
        {
            changed = true;
            return isUpper ? char.ToUpper(ru) : ru;
        }

        if (RuToEn.TryGetValue(lower, out var en))
        {
            changed = true;
            return isUpper ? char.ToUpper(en) : en;
        }

        changed = false;
        return ch;
    }

    public static string ApplyCasePattern(string source, string target)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target))
        {
            return target;
        }

        var sourceLetters = source.Where(char.IsLetter).ToArray();
        if (sourceLetters.Length == 0)
        {
            return target;
        }

        if (sourceLetters.All(char.IsUpper))
        {
            return target.ToUpperInvariant();
        }

        if (char.IsUpper(sourceLetters[0]) && sourceLetters.Skip(1).All(char.IsLower))
        {
            return char.ToUpperInvariant(target[0]) + target[1..].ToLowerInvariant();
        }

        var targetChars = target.ToCharArray();
        var sourceLetterIndex = 0;
        for (var i = 0; i < targetChars.Length; i++)
        {
            if (!char.IsLetter(targetChars[i]))
            {
                continue;
            }

            var patternSource = sourceLetterIndex < sourceLetters.Length
                ? sourceLetters[sourceLetterIndex]
                : sourceLetters[^1];
            targetChars[i] = char.IsUpper(patternSource)
                ? char.ToUpperInvariant(targetChars[i])
                : char.ToLowerInvariant(targetChars[i]);
            sourceLetterIndex++;
        }

        return new string(targetChars);
    }

    private static int ScoreEnglishLike(string text)
    {
        var lower = text.ToLowerInvariant();
        var score = 0;
        var letters = lower.Count(c => c is >= 'a' and <= 'z');
        if (letters == 0) return 0;

        var vowels = lower.Count(c => "aeiouy".Contains(c));
        if (vowels > 0) score++;
        if (letters >= 4 && vowels > 0 && vowels < letters) score++;

        foreach (var bigram in new[] { "th", "he", "in", "er", "an", "re", "on", "at", "en", "st", "ll", "oo" })
        {
            if (lower.Contains(bigram, StringComparison.Ordinal)) score++;
        }

        if (lower.Contains("q") && !lower.Contains("qu")) score--;
        if (lower.Contains("jj") || lower.Contains("qq") || lower.Contains("ww")) score--;
        return score;
    }

    private static int ScoreRussianLike(string text)
    {
        var lower = text.ToLowerInvariant();
        var score = 0;
        var letters = lower.Count(c => c is >= 'а' and <= 'я' || c == 'ё');
        if (letters == 0) return 0;

        var vowels = lower.Count(c => "аеёиоуыэюя".Contains(c));
        if (vowels > 0) score++;
        if (letters >= 4 && vowels > 0 && vowels < letters) score++;

        foreach (var bigram in new[] { "пр", "ст", "то", "но", "ни", "ра", "ко", "по", "ен", "ро", "ов", "не" })
        {
            if (lower.Contains(bigram, StringComparison.Ordinal)) score++;
        }

        if (lower.Contains("ьы") || lower.Contains("ъь") || lower.Contains("йй")) score--;
        return score;
    }
}

public enum LanguageScript
{
    Unknown = 0,
    English = 1,
    Russian = 2
}
