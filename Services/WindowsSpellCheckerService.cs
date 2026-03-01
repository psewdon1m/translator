using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using TranslatorTray.Native;

namespace TranslatorTray.Services;

public sealed class WindowsSpellCheckerService : IDisposable
{
    private static readonly Guid FactoryClsid = new("7AB36653-1796-484B-BDFA-E74F1DB7C1DC");

    private readonly object _sync = new();
    private readonly Dictionary<LanguageScript, ISpellChecker?> _checkers = [];
    private readonly bool _available;

    public WindowsSpellCheckerService()
    {
        try
        {
            var factoryType = Type.GetTypeFromCLSID(FactoryClsid, throwOnError: false);
            if (factoryType is null)
            {
                return;
            }

            var factory = Activator.CreateInstance(factoryType) as ISpellCheckerFactory;
            if (factory is null)
            {
                return;
            }

            _checkers[LanguageScript.English] = CreateChecker(factory, "en-US", "en");
            _checkers[LanguageScript.Russian] = CreateChecker(factory, "ru-RU", "ru");
            _available = _checkers.Values.Any(c => c is not null);
        }
        catch
        {
            _available = false;
        }
    }

    public bool IsAvailable => _available;

    public bool IsCorrect(LanguageScript language, string word)
    {
        var checker = GetChecker(language);
        if (checker is null)
        {
            return false;
        }

        try
        {
            var errors = checker.Check(word);
            try
            {
                var first = errors.Next();
                if (first is null)
                {
                    return true;
                }

                Marshal.ReleaseComObject(first);
                return false;
            }
            finally
            {
                Marshal.ReleaseComObject(errors);
            }
        }
        catch
        {
            return false;
        }
    }

    public bool TrySuggest(LanguageScript language, string word, Func<string, bool> validator, out string suggestion)
    {
        suggestion = word;
        var checker = GetChecker(language);
        if (checker is null)
        {
            return false;
        }

        try
        {
            var suggestions = checker.Suggest(word);
            try
            {
                var buffer = new string[1];
                while (suggestions.Next(1, buffer, IntPtr.Zero) == 0)
                {
                    var candidate = buffer[0];
                    if (!string.IsNullOrWhiteSpace(candidate) && validator(candidate))
                    {
                        suggestion = candidate;
                        return true;
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(suggestions);
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private ISpellChecker? GetChecker(LanguageScript language)
    {
        lock (_sync)
        {
            return _checkers.GetValueOrDefault(language);
        }
    }

    private static ISpellChecker? CreateChecker(ISpellCheckerFactory factory, params string[] languageTags)
    {
        foreach (var tag in languageTags)
        {
            try
            {
                if (factory.IsSupported(tag, out var supported) == 0 && supported)
                {
                    return factory.CreateSpellChecker(tag);
                }
            }
            catch
            {
                // Ignore and try the next language tag.
            }
        }

        return null;
    }

    public void Dispose()
    {
        foreach (var checker in _checkers.Values)
        {
            if (checker is not null)
            {
                Marshal.ReleaseComObject(checker);
            }
        }

        _checkers.Clear();
        GC.SuppressFinalize(this);
    }
}

