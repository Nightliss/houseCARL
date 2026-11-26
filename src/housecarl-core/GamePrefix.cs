namespace HousecarlCore;

/// <summary>Where the game's Windows-side folders are: Documents\My Games on Windows, the Proton or Wine prefix elsewhere.</summary>
public static class GamePrefix
{
    public const string MyGamesVariable = "HOUSECARL_MY_GAMES";
    public const string PrefixVariable = "HOUSECARL_PROTON_PREFIX";
    const string SkyrimSeAppId = "489830";

    /// <summary>The prefix (<c>pfx</c>) the loaded instance runs the game in, set when an instance resolves; the environment variable outranks it.</summary>
    public static string? Hint { get; set; }

    /// <summary>The Proton prefix beside a Steam library game folder (<c>steamapps/common/&lt;game&gt;</c> to <c>steamapps/compatdata/&lt;appid&gt;/pfx</c>), or null when the folder is not in a library or the prefix is not there.</summary>
    public static string? FromSteamGameDir(string gameDir)
    {
        if (string.IsNullOrWhiteSpace(gameDir)) return null;
        var common = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(gameDir));
        if (common is null || !Path.GetFileName(common).Equals("common", StringComparison.OrdinalIgnoreCase)) return null;
        var apps = Path.GetDirectoryName(common);
        if (apps is null) return null;
        var pfx = LooseListings.OnDisk(apps, "compatdata", SkyrimSeAppId + "/pfx");
        return Directory.Exists(pfx) ? pfx : null;
    }

    /// <summary>The prefix in use: the environment variable, else the instance's hint, else null.</summary>
    public static string? Current => Environment.GetEnvironmentVariable(PrefixVariable) is { Length: > 0 } env ? env : Hint;

    /// <summary>The game's <c>Documents\My Games\Skyrim Special Edition</c>; the environment variable overrides it so tests never read the machine's own.</summary>
    public static string MyGames()
    {
        if (Environment.GetEnvironmentVariable(MyGamesVariable) is { Length: > 0 } over) return over;
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Skyrim Special Edition");
        return LooseListings.OnDisk(UserFolder(), "Documents/My Games/Skyrim Special Edition");
    }

    /// <summary>The prefix's Windows <c>System32</c>, or null off Windows with no prefix.</summary>
    public static string? System32() =>
        OperatingSystem.IsWindows() ? Environment.GetFolderPath(Environment.SpecialFolder.System)
        : Current is { } pfx ? LooseListings.OnDisk(pfx, "drive_c/windows/system32") : null;

    /// <summary>Proton runs the game as <c>steamuser</c>; plain Wine as the login name. The first that has a folder wins, else <c>steamuser</c>.</summary>
    static string UserFolder()
    {
        var users = Current is { } pfx ? LooseListings.OnDisk(pfx, "drive_c/users") : Path.Combine(Path.GetTempPath(), "housecarl-no-prefix");
        foreach (var name in new[] { "steamuser", Environment.UserName })
            if (Directory.Exists(LooseListings.OnDisk(users, name))) return LooseListings.OnDisk(users, name);
        return Path.Combine(users, "steamuser");
    }
}
