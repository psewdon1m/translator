using System.Text;
using System.Text.Json;

namespace TranslatorTray.Services;

public sealed class PuntoDataService
{
    private readonly Dictionary<string, string> _enToRu = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _ruToEn = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _triggers = new(StringComparer.OrdinalIgnoreCase);

    public PuntoDataService(string baseDirectory)
    {
        TryLoadFrom(Path.Combine(baseDirectory, "example", "Punto Switcher", "Data"));
        if (!IsLoaded)
        {
            TryLoadFrom(Path.Combine(Directory.GetCurrentDirectory(), "example", "Punto Switcher", "Data"));
        }
    }

    public int EnRuCount => _enToRu.Count;
    public int RuEnCount => _ruToEn.Count;
    public int TriggerCount => _triggers.Count;

    public bool IsLoaded => _enToRu.Count > 0 || _ruToEn.Count > 0 || _triggers.Count > 0;

    public bool TryWholeWordMapping(string sourceWord, LanguageScript sourceScript, out string mapped)
    {
        mapped = sourceWord;
        var key = sourceWord.Trim();
        if (key.Length == 0) return false;

        return sourceScript switch
        {
            LanguageScript.English => _enToRu.TryGetValue(key, out mapped!),
            LanguageScript.Russian => _ruToEn.TryGetValue(key, out mapped!),
            _ => false
        };
    }

    public bool IsTrigger(string sourceWord)
    {
        var key = sourceWord.Trim();
        return key.Length > 0 && _triggers.Contains(key);
    }

    private void TryLoadFrom(string dataDir)
    {
        if (!Directory.Exists(dataDir))
        {
            return;
        }

        var cp1251 = Encoding.GetEncoding(1251);
        LoadMappings(Path.Combine(dataDir, "translit-en.dat"), _enToRu, cp1251);
        LoadMappings(Path.Combine(dataDir, "translit-ru.dat"), _ruToEn, cp1251);
        LoadTriggers(Path.Combine(dataDir, "triggers.dat"), cp1251);
    }

    private static void LoadMappings(string path, Dictionary<string, string> target, Encoding enc)
    {
        if (!File.Exists(path)) return;

        foreach (var raw in File.ReadLines(path, enc))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var idx = line.IndexOf('=');
            if (idx <= 0 || idx >= line.Length - 1) continue;

            var key = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim();
            if (key.Length == 0 || value.Length == 0) continue;

            target[key] = value;
        }
    }

    private void LoadTriggers(string path, Encoding enc)
    {
        if (!File.Exists(path)) return;

        foreach (var raw in File.ReadLines(path, enc))
        {
            var line = raw.Trim();
            if (line.Length < 2) continue;
            _triggers.Add(line);
        }
    }
}
