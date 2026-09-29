namespace HoodieCompanion.Companion.Perception;

/// <summary>What kind of program the user is in. Judged only from the process name (never titles or content).</summary>
public enum AppCategory
{
    Unknown,
    Browser,
    Code,
    Office,
    Chat,
    Media,
    Creative,
    Terminal,
    Files,
    Game,
}

public static class AppCategories
{
    private static readonly Dictionary<string, AppCategory> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome"] = AppCategory.Browser, ["msedge"] = AppCategory.Browser, ["firefox"] = AppCategory.Browser, ["opera"] = AppCategory.Browser,
        ["brave"] = AppCategory.Browser, ["browser"] = AppCategory.Browser, ["vivaldi"] = AppCategory.Browser, ["arc"] = AppCategory.Browser,
        ["devenv"] = AppCategory.Code, ["code"] = AppCategory.Code, ["rider64"] = AppCategory.Code, ["idea64"] = AppCategory.Code,
        ["pycharm64"] = AppCategory.Code, ["webstorm64"] = AppCategory.Code, ["sublime_text"] = AppCategory.Code, ["notepad++"] = AppCategory.Code,
        ["cursor"] = AppCategory.Code, ["clion64"] = AppCategory.Code, ["goland64"] = AppCategory.Code, ["android studio"] = AppCategory.Code,
        ["winword"] = AppCategory.Office, ["excel"] = AppCategory.Office, ["powerpnt"] = AppCategory.Office, ["onenote"] = AppCategory.Office,
        ["outlook"] = AppCategory.Office, ["olk"] = AppCategory.Office, ["notion"] = AppCategory.Office, ["obsidian"] = AppCategory.Office,
        ["acrobat"] = AppCategory.Office, ["acrord32"] = AppCategory.Office, ["notepad"] = AppCategory.Office, ["soffice.bin"] = AppCategory.Office,
        ["telegram"] = AppCategory.Chat, ["discord"] = AppCategory.Chat, ["slack"] = AppCategory.Chat, ["teams"] = AppCategory.Chat,
        ["ms-teams"] = AppCategory.Chat, ["whatsapp"] = AppCategory.Chat, ["zoom"] = AppCategory.Chat, ["skype"] = AppCategory.Chat, ["viber"] = AppCategory.Chat,
        ["vlc"] = AppCategory.Media, ["spotify"] = AppCategory.Media, ["mpc-hc64"] = AppCategory.Media, ["mpc-be64"] = AppCategory.Media,
        ["potplayermini64"] = AppCategory.Media, ["music.ui"] = AppCategory.Media, ["video.ui"] = AppCategory.Media, ["wmplayer"] = AppCategory.Media,
        ["photoshop"] = AppCategory.Creative, ["blender"] = AppCategory.Creative, ["figma"] = AppCategory.Creative, ["illustrator"] = AppCategory.Creative,
        ["krita"] = AppCategory.Creative, ["gimp-2.10"] = AppCategory.Creative, ["obs64"] = AppCategory.Creative, ["premiere"] = AppCategory.Creative,
        ["windowsterminal"] = AppCategory.Terminal, ["cmd"] = AppCategory.Terminal, ["powershell"] = AppCategory.Terminal, ["pwsh"] = AppCategory.Terminal,
        ["explorer"] = AppCategory.Files, ["totalcmd64"] = AppCategory.Files,
        ["steam"] = AppCategory.Game, ["epicgameslauncher"] = AppCategory.Game, ["battle.net"] = AppCategory.Game,
    };

    /// <summary>Category for a process. An unknown app running fullscreen is treated as a game.</summary>
    public static AppCategory Of(string? process, bool fullscreen)
    {
        if (string.IsNullOrEmpty(process)) return AppCategory.Unknown;
        if (Known.TryGetValue(process, out var c)) return c;
        return fullscreen ? AppCategory.Game : AppCategory.Unknown;
    }

    /// <summary>Is this a "work" context (Hoodie should be considerate)?</summary>
    public static bool IsWork(AppCategory c) => c is AppCategory.Code or AppCategory.Office or AppCategory.Terminal or AppCategory.Creative;
}
