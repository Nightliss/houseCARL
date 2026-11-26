namespace HousecarlCore;

// GameIni — the one home for "the INI the game reads" under an MO2 profile: the profile's own INIs when MO2 runs it on
// them (settings.ini LocalSettings), else Documents\My Games\Skyrim Special Edition; SkyrimCustom.ini over Skyrim.ini,
// key by key. The game language and the base archive list both read through it. Contract in
// docs/architecture/load-order-resolver.md.

/// <summary>The Skyrim.ini and SkyrimCustom.ini the game reads for one profile, and one key's value out of them.</summary>
public sealed class GameIni
{
    /// <summary>The folder the INIs are read from.</summary>
    public string Folder { get; }

    /// <summary>Whether that folder is the profile's own (MO2 runs the game on profile INIs) rather than My Games.</summary>
    public bool ProfileLocal { get; }

    GameIni(string folder, bool profileLocal) { Folder = folder; ProfileLocal = profileLocal; }

    /// <summary>The INIs in <paramref name="folder"/>, which the manager runs the game on (a profile's own INI folder).</summary>
    public static GameIni Local(string folder) => new(folder, true);

    /// <summary>The INIs for <paramref name="profileDir"/>: its own when settings.ini sets LocalSettings=true, My Games when it sets false; a profile with no such flag (not one MO2 wrote) uses its own Skyrim.ini when it has one.</summary>
    public static GameIni For(string profileDir)
    {
        var local = profileDir.Length > 0 && (ReadFile(LooseListings.OnDisk(profileDir, "settings.ini"), "General", "LocalSettings") is { } flag
            ? flag.Equals("true", StringComparison.OrdinalIgnoreCase)
            : File.Exists(LooseListings.OnDisk(profileDir, "Skyrim.ini")));
        return local ? new GameIni(profileDir, true) : new GameIni(MyGamesFolder(), false);
    }

    /// <summary>Documents\My Games\Skyrim Special Edition (<see cref="GamePrefix.MyGames"/>), where the game keeps its INIs when the manager does not redirect them.</summary>
    static string MyGamesFolder() => GamePrefix.MyGames();

    /// <summary>The value of <paramref name="key"/> in <paramref name="section"/> and the INI it came from: SkyrimCustom.ini's when it sets one, else Skyrim.ini's; null when neither does.</summary>
    public (string? Value, string Ini) Value(string section, string key)
    {
        foreach (var name in new[] { "SkyrimCustom.ini", "Skyrim.ini" })
        {
            var ini = LooseListings.OnDisk(Folder, name);
            if (ReadFile(ini, section, key) is { } value) return (value, ini);
        }
        return (null, LooseListings.OnDisk(Folder, "Skyrim.ini"));
    }

    /// <summary>The first non-empty value of <paramref name="key"/> under <paramref name="section"/> in one INI file, or null.</summary>
    static string? ReadFile(string ini, string section, string key)
    {
        string[] lines;
        try { if (!File.Exists(ini)) return null; lines = File.ReadAllLines(ini); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }

        bool inSection = false;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == ';') continue;
            if (line[0] == '[') { inSection = line.Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase); continue; }
            if (!inSection) continue;
            var eq = line.IndexOf('=');
            if (eq < 0 || !line[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            var value = line[(eq + 1)..].Trim();
            if (value.Length > 0) return value;
        }
        return null;
    }
}
