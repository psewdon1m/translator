namespace TranslatorTray.Services;

public sealed class ToneService
{
    public void PlayLayoutSwitch() => PlayAsync(1046, 40);
    public void PlayAutoToggle() => PlayAsync(698, 55);
    public void PlayTextAction() => PlayAsync(1397, 45);

    private static void PlayAsync(int frequency, int durationMs)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                Console.Beep(frequency, durationMs);
            }
            catch
            {
                // Ignore audio failures.
            }
        });
    }
}
