using System.Text;
using System.Windows.Forms;

namespace TranslatorTray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayAppContext());
    }
}
