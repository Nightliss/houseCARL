using System.Text.Json;

namespace HousecarlCore;

// An Amethyst Mod Manager profile as a load-order source; the layout and what is and is not read are in docs/architecture/amethyst-instance.md.

/// <summary>The load-order roots of one Amethyst profile folder (<c>&lt;profile root&gt;/profiles/&lt;name&gt;</c>), as Amethyst deploys them.</summary>
public static class AmethystInstance
{
    public const string StateFileName = "profile_state.json";
    const string GameConfigFolder = "AmethystModManager";

    /// <summary>The user config folder holding <c>games/&lt;game&gt;/paths.json</c>; a test points it elsewhere.</summary>
    internal static Func<string> ConfigRoot = () =>
        Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg ? xdg
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), GameConfigFolder);

    /// <summary>Is this folder an Amethyst profile: it holds Amethyst's own profile state file beside the modlist.</summary>
    public static bool IsProfileDir(string dir) =>
        !string.IsNullOrWhiteSpace(dir) && File.Exists(LooseListings.OnDisk(dir, StateFileName)) && File.Exists(LooseListings.OnDisk(dir, "modlist.txt"));

    /// <summary>The profile folder <paramref name="path"/> and <paramref name="profile"/> name: the path itself when it is a profile, else the named profile under a profile root, else the profile Amethyst last had active for that root; or null with the reason in <paramref name="problem"/> when the path is no Amethyst layout.</summary>
    public static string? Locate(string path, string? profile, out string? problem)
    {
        problem = null;
        path = path.Trim().TrimEnd('\\', '/');
        if (IsProfileDir(path)) return path;
        var profiles = LooseListings.OnDisk(path, "profiles");
        if (!Directory.Exists(profiles)) { problem = "it is neither an Amethyst profile folder (no profile_state.json beside modlist.txt) nor a profile root (no profiles folder)"; return null; }
        var names = Directory.EnumerateDirectories(profiles).Select(Path.GetFileName).Where(n => IsProfileDir(LooseListings.OnDisk(profiles, n!))).Order(StringComparer.OrdinalIgnoreCase).ToList();
        var pick = string.IsNullOrWhiteSpace(profile) ? ActiveProfile(path) : profile.Trim();
        if (pick is null)
        {
            problem = "name the profile (profile=): " + (names.Count == 0 ? "the profile root holds none" : "it holds " + string.Join(", ", names.Select(n => $"'{n}'")));
            return null;
        }
        var folder = names.FirstOrDefault(n => string.Equals(n, pick, StringComparison.OrdinalIgnoreCase));
        if (folder is null) { problem = $"there is no profile '{pick}' under '{profiles}'" + (names.Count == 0 ? "" : ": " + string.Join(", ", names.Select(n => $"'{n}'"))); return null; }
        return LooseListings.OnDisk(profiles, folder);
    }

    /// <summary>The profile Amethyst last had active for the game whose staging root is <paramref name="profileRoot"/>, or null.</summary>
    static string? ActiveProfile(string profileRoot)
    {
        var dir = GameConfigDir(profileRoot);
        if (dir is null || Json(LooseListings.OnDisk(dir, "deploy_state.json")) is not { } state) return null;
        return state.TryGetProperty("last_active_profile", out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;
    }

    /// <summary>The <c>games/&lt;game&gt;</c> config folder whose paths.json names <paramref name="profileRoot"/> as its staging path, or null.</summary>
    static string? GameConfigDir(string profileRoot)
    {
        var games = LooseListings.OnDisk(ConfigRoot(), "games");
        if (!Directory.Exists(games)) return null;
        foreach (var dir in Directory.EnumerateDirectories(games).Order(StringComparer.Ordinal))
            if (Json(Path.Combine(dir, "paths.json")) is { } p && Str(p, "staging_path") is { } staging && SamePath(staging, profileRoot)) return dir;
        return null;
    }

    static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.Ordinal);

    /// <summary>The roots Amethyst deploys for this profile folder, or null with the problems named. A profile with <c>profile_specific_mods</c> keeps mods, overwrite and Root_Folder inside itself; any other keeps them beside <c>profiles</c>. The game folder is the profile's own <c>game_path</c> when it sets one (a Wabbajack install's stock game), else the game config's.</summary>
    public static Mo2InstancePaths? Derive(string profileDir, List<string>? problems)
    {
        profileDir = profileDir.Trim().TrimEnd('\\', '/');
        if (!IsProfileDir(profileDir)) { problems?.Add($"'{profileDir}' is not an Amethyst profile folder (no profile_state.json beside modlist.txt)"); return null; }
        if (Json(LooseListings.OnDisk(profileDir, StateFileName)) is not { } state) { problems?.Add($"cannot read {StateFileName} in '{profileDir}'"); return null; }

        var name = Path.GetFileName(profileDir);
        var root = Path.GetDirectoryName(Path.GetDirectoryName(profileDir)!)!;
        var settings = state.TryGetProperty("profile_settings", out var s) && s.ValueKind == JsonValueKind.Object ? s : (JsonElement?)null;
        var config = GameConfigDir(root) is { } cfg ? Json(Path.Combine(cfg, "paths.json")) : null;
        // The default profile keeps its settings global; only a profile of another name overrides them.
        bool overrides = !name.Equals("default", StringComparison.OrdinalIgnoreCase);
        string? Setting(string key) => overrides && settings is { } st ? Str(st, key) : null;

        bool specific = overrides && settings is { } sp && sp.TryGetProperty("profile_specific_mods", out var b) && b.ValueKind == JsonValueKind.True;
        var holder = specific ? profileDir : root;
        var modsDir = LooseListings.OnDisk(holder, "mods");
        var overwriteDir = LooseListings.OnDisk(holder, "overwrite");
        var rootFolder = LooseListings.OnDisk(holder, "Root_Folder");

        var gamePath = Setting("game_path") ?? (config is { } c1 ? Str(c1, "game_path") : null);
        if (string.IsNullOrWhiteSpace(gamePath))
        {
            problems?.Add($"no game folder: profile '{name}' sets no game_path and no Amethyst game config (paths.json under '{ConfigRoot()}') names '{root}' as its staging path");
            return null;
        }
        var dataDir = LooseListings.OnDisk(gamePath, "Data");
        if (!Directory.Exists(modsDir)) problems?.Add($"the mods folder is missing: '{modsDir}'");
        if (!Directory.Exists(dataDir)) problems?.Add($"the game Data folder is missing: '{dataDir}' (from game_path '{gamePath}')");
        if (!Directory.Exists(modsDir) || !Directory.Exists(dataDir)) return null;

        bool profileInis = overrides && settings is { } si && si.TryGetProperty("profile_ini_files", out var pi) && pi.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? pi.ValueKind == JsonValueKind.True
            : config is { } c2 && c2.TryGetProperty("profile_ini_files", out var gi) && gi.ValueKind == JsonValueKind.True;
        var prefix = Setting("prefix_path") ?? (config is { } c3 ? Str(c3, "prefix_path") : null);
        return new Mo2InstancePaths(profileDir, name, profileDir, modsDir, dataDir, gamePath, overwriteDir)
        {
            RootFolderDir = rootFolder,
            IniFolder = profileInis ? LooseListings.OnDisk(profileDir, "ini files") : null,
            PrefixDir = string.IsNullOrWhiteSpace(prefix) ? null : prefix,
            Source = "Amethyst",
        };
    }

    /// <summary>Amethyst's own visibility rules for this profile: what is switched off per mod, what is routed to the game root, and its bookkeeping names; it honours no <c>.mohidden</c> suffix or <c>.git</c> folder skip.</summary>
    public static Mo2SkipRules ReadSkipRules(string profileDir)
    {
        var state = Json(LooseListings.OnDisk(profileDir, StateFileName));
        return new Mo2SkipRules([], [])
        {
            Amethyst = true,
            ExcludedByMod = PathSets(state, "excluded_mod_files"),
            RootRoutedByMod = PathSets(state, "root_mod_files"),
        };
    }

    static IReadOnlyDictionary<string, IReadOnlySet<string>> PathSets(JsonElement? state, string key)
    {
        var map = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase);
        if (state is { } st && st.TryGetProperty(key, out var table) && table.ValueKind == JsonValueKind.Object)
            foreach (var mod in table.EnumerateObject().Where(m => m.Value.ValueKind == JsonValueKind.Array))
                map[mod.Name] = mod.Value.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.String)
                    .Select(p => p.GetString()!.Replace('\\', '/').ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        return map;
    }

    static JsonElement? Json(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path), new JsonDocumentOptions { AllowTrailingCommas = true });
            return doc.RootElement.Clone();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    static string? Str(JsonElement obj, string key) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;
}
