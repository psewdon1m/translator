using System.Text;

namespace TranslatorTray.Services;

public sealed class WordTracker
{
    private readonly object _sync = new();
    private readonly char[] _buffer;
    private int _head;
    private int _count;

    private static readonly HashSet<char> Boundaries =
    [
        ' ', '\t', '\r', '\n',
        '!', '?',
        '(', ')',
        '/', '\\', '|',
        '@', '#', '$', '%', '^', '&', '*',
        '+', '=', '-', '_'
    ];

    public WordTracker(int maxSize = 64)
    {
        _buffer = new char[Math.Max(8, maxSize)];
    }

    public void Add(char ch)
    {
        lock (_sync)
        {
            _buffer[_head] = ch;
            _head = (_head + 1) % _buffer.Length;
            if (_count < _buffer.Length)
            {
                _count++;
            }
        }
    }

    public void Backspace()
    {
        lock (_sync)
        {
            if (_count == 0) return;
            _head = (_head - 1 + _buffer.Length) % _buffer.Length;
            _count--;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _count = 0;
        }
    }

    public bool IsBoundary(char ch) => char.IsWhiteSpace(ch) || Boundaries.Contains(ch);

    public string Consume()
    {
        lock (_sync)
        {
            if (_count == 0) return string.Empty;

            var sb = new StringBuilder(_count);
            for (var i = 0; i < _count; i++)
            {
                var idx = (_head - _count + i + _buffer.Length) % _buffer.Length;
                sb.Append(_buffer[idx]);
            }

            _count = 0;
            return sb.ToString();
        }
    }
}
