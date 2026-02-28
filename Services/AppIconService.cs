using System.Drawing;

namespace TranslatorTray.Services;

public static class AppIconService
{
    public static Icon LoadTrayIcon(string baseDirectory)
    {
        var icoPath = FindIconFile(baseDirectory);
        if (icoPath is not null)
        {
            try
            {
                return new Icon(icoPath);
            }
            catch
            {
                // fall through
            }
        }

        var pngPath = FindPngFile(baseDirectory);
        if (pngPath is not null)
        {
            try
            {
                using var bitmap = new Bitmap(pngPath);
                using var resized = new Bitmap(bitmap, new Size(32, 32));
                var handle = resized.GetHicon();
                return Icon.FromHandle(handle);
            }
            catch
            {
                // fall through
            }
        }

        return SystemIcons.Application;
    }

    private static string? FindIconFile(string baseDirectory)
    {
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "src", "images", "App.ico"),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "images", "App.ico")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? FindPngFile(string baseDirectory)
    {
        var directories = new[]
        {
            Path.Combine(baseDirectory, "src", "images"),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "images"),
            Path.Combine(baseDirectory, "src"),
            Path.Combine(Directory.GetCurrentDirectory(), "src")
        };

        foreach (var dir in directories)
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            var png = Directory.GetFiles(dir, "*.png", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (png is not null)
            {
                return png;
            }
        }

        return null;
    }
}
