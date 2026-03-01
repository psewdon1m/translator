using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace TranslatorTray.Native;

[ComImport]
[Guid("8E018A9D-2415-4677-BF08-794EA61F94BB")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISpellCheckerFactory
{
    IEnumString SupportedLanguages { get; }

    [PreserveSig]
    int IsSupported([MarshalAs(UnmanagedType.LPWStr)] string languageTag, [MarshalAs(UnmanagedType.Bool)] out bool value);

    ISpellChecker CreateSpellChecker([MarshalAs(UnmanagedType.LPWStr)] string languageTag);
}

[ComImport]
[Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISpellChecker
{
    [DispId(1)]
    string LanguageTag { get; }

    IEnumSpellingError Check([MarshalAs(UnmanagedType.LPWStr)] string text);

    IEnumString Suggest([MarshalAs(UnmanagedType.LPWStr)] string word);
}

[ComImport]
[Guid("803E3BD4-2828-4410-8290-418D1D73C762")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEnumSpellingError
{
    ISpellingError? Next();
}

[ComImport]
[Guid("B7C82D61-FBE8-4B47-9B27-6C0D2E0DE0A3")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISpellingError
{
    [DispId(1)]
    uint StartIndex { get; }
    [DispId(2)]
    uint Length { get; }
    [DispId(3)]
    CorrectiveAction CorrectiveAction { get; }
    [DispId(4)]
    string Replacement { get; }
}

internal enum CorrectiveAction
{
    None = 0,
    GetSuggestions = 1,
    Replace = 2,
    Delete = 3
}
