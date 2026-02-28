using System.Media;
using TranslatorTray.Models;

namespace TranslatorTray.Services;

public sealed class ToneService
{
    public const string BuiltinLayoutSwitchId = "builtin:layout-switch";
    public const string BuiltinAutoToggleId = "builtin:auto-toggle";
    public const string BuiltinTextActionId = "builtin:text-action";
    public const string BuiltinSoftClickId = "builtin:soft-click";
    public const string BuiltinBrightPingId = "builtin:bright-ping";
    public const string BuiltinDoubleTickId = "builtin:double-tick";

    private readonly string[] _soundDirectories;

    public ToneService(string baseDirectory)
    {
        _soundDirectories =
        [
            Path.Combine(baseDirectory, "src", "sounds"),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "sounds")
        ];
    }

    public IReadOnlyList<SoundOption> GetAvailableSounds()
    {
        var options = new List<SoundOption>
        {
            new(BuiltinLayoutSwitchId, "Built-in: Layout Switch", true),
            new(BuiltinAutoToggleId, "Built-in: Auto Toggle", true),
            new(BuiltinTextActionId, "Built-in: Text Action", true),
            new(BuiltinSoftClickId, "Built-in: Soft Click", true),
            new(BuiltinBrightPingId, "Built-in: Bright Ping", true),
            new(BuiltinDoubleTickId, "Built-in: Double Tick", true)
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in _soundDirectories)
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var path in Directory.GetFiles(dir, "*.wav", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileName(path);
                if (!seen.Add(fileName))
                {
                    continue;
                }

                options.Add(new($"file:{fileName}", fileName, false));
            }
        }

        return options;
    }

    public void Play(string? soundId)
    {
        var id = string.IsNullOrWhiteSpace(soundId) ? BuiltinLayoutSwitchId : soundId;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                if (TryPlayCustom(id))
                {
                    return;
                }

                PlayBuiltin(id);
            }
            catch
            {
                // Ignore audio failures.
            }
        });
    }

    private bool TryPlayCustom(string soundId)
    {
        if (!soundId.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fileName = soundId["file:".Length..];
        foreach (var dir in _soundDirectories)
        {
            var path = Path.Combine(dir, fileName);
            if (!File.Exists(path))
            {
                continue;
            }

            using var player = new SoundPlayer(path);
            player.PlaySync();
            return true;
        }

        return false;
    }

    private static void PlayBuiltin(string soundId)
    {
        switch (soundId)
        {
            case BuiltinLayoutSwitchId:
                Console.Beep(1046, 40);
                break;
            case BuiltinAutoToggleId:
                Console.Beep(698, 55);
                break;
            case BuiltinTextActionId:
                Console.Beep(1397, 45);
                break;
            case BuiltinSoftClickId:
                Console.Beep(880, 30);
                break;
            case BuiltinBrightPingId:
                Console.Beep(1568, 55);
                break;
            case BuiltinDoubleTickId:
                Console.Beep(784, 22);
                Thread.Sleep(16);
                Console.Beep(988, 22);
                break;
            default:
                Console.Beep(1046, 40);
                break;
        }
    }
}

public sealed record SoundOption(string Id, string DisplayName, bool IsBuiltIn)
{
    public override string ToString() => DisplayName;
}
