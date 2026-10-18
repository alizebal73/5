using System.Globalization;

namespace GameNet.Desktop.Infrastructure;

public static class DesktopCulture
{
    public const string DefaultCulture = "fa-IR";
    public const string EnglishCulture = "en-US";

    public static CultureInfo Resolve(string? requested)
        => string.Equals(requested, EnglishCulture, StringComparison.OrdinalIgnoreCase)
            ? CultureInfo.GetCultureInfo(EnglishCulture)
            : CultureInfo.GetCultureInfo(DefaultCulture);

    public static string GetResourceDictionaryName(CultureInfo culture)
        => culture.Name.Equals(EnglishCulture, StringComparison.OrdinalIgnoreCase)
            ? "Strings.en-US.xaml"
            : "Strings.fa-IR.xaml";

    public static void Apply(CultureInfo culture)
    {
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
