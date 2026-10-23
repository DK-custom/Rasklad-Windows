using System.Globalization;

namespace RaskladWindows {
    static class UiText {
        internal static bool Russian { get { return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru"; } }
        internal static string Get(string russian, string english) { return Russian ? russian : english; }
    }
}
