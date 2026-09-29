using System;
using System.Collections.Generic;
using System.Globalization;

namespace HoodieCompanion.UI;

/// <summary>
/// Minimal localization. UI strings are written in English in code and looked up here; missing
/// translations fall back to English. Languages: English, Russian.
/// </summary>
public static partial class L
{
    public static string Language { get; private set; } = "en";

    public static event Action? Changed;

    /// <summary>"auto" uses the Windows display language (Russian if it is Russian, otherwise English).</summary>
    public static void Set(string setting)
    {
        var lang = setting == "auto"
            ? (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en")
            : setting;
        if (lang != "ru") lang = "en";
        if (lang == Language) return;
        Language = lang;
        Changed?.Invoke();
    }

    /// <summary>Translate.</summary>
    /// <summary>Culture for dates in the current UI language.</summary>
    public static CultureInfo Culture => Language == "ru" ? CultureInfo.GetCultureInfo("ru-RU") : CultureInfo.GetCultureInfo("en-GB");

    public static string T(string en) => Language == "ru" && Ru.TryGetValue(en, out var ru) ? ru : en;

    /// <summary>Translate a format string, then format.</summary>
    public static string F(string en, params object[] args) => string.Format(CultureInfo.CurrentCulture, T(en), args);

    public static IReadOnlyDictionary<string, string> Russian => Ru;
}
