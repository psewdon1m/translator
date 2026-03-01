namespace TranslatorTray.Services;

public sealed class ProtectedTermsService
{
    private readonly HashSet<string> _terms;

    public ProtectedTermsService(string baseDirectory)
    {
        _terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var builtIn in GetBuiltInTerms())
        {
            var normalized = Normalize(builtIn);
            if (!string.IsNullOrEmpty(normalized))
            {
                _terms.Add(normalized);
            }
        }

        var filePath = Path.Combine(baseDirectory, "Data", "protected_terms.txt");
        if (!File.Exists(filePath))
        {
            return;
        }

        foreach (var line in File.ReadLines(filePath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
            {
                continue;
            }

            var normalized = Normalize(trimmed);
            if (!string.IsNullOrEmpty(normalized))
            {
                _terms.Add(normalized);
            }
        }
    }

    public bool Contains(string word)
    {
        var normalized = Normalize(word);
        return !string.IsNullOrEmpty(normalized) && _terms.Contains(normalized);
    }

    private static IEnumerable<string> GetBuiltInTerms()
    {
        return
        [
            "ctrl", "alt", "shift", "enter", "tab", "esc", "escape", "space", "del", "delete"
        ];
    }

    private static string Normalize(string value) =>
        new(value.Trim().ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c is '\'' or '-' or '+' or '#' or '.').ToArray());
}

