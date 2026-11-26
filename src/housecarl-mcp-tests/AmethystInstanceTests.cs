using HousecarlCore;
using HousecarlMcp;
using Xunit;

namespace HousecarlMcpTests;

/// <summary>An Amethyst profile as a load-order source: the roots it derives, how a profile is found, and what the asset and plugin reads see
/// through its modlist, per-mod switches and profile INIs. Fixtures are temp trees laid out as Amethyst writes them.</summary>
[Trait("tier", "integration")]
[Collection(SerialCollection.Name)]
public sealed class AmethystInstanceTests : IDisposable
{
    const string Rel = @"meshes\actors\hc\thing.nif";
    readonly string _root = Path.Combine(Path.GetTempPath(), "hc-amethyst-" + Guid.NewGuid().ToString("N"));
    readonly string _stage, _game, _data, _config;
    readonly Func<string> _realConfigRoot = AmethystInstance.ConfigRoot;
    readonly List<LoadOrderService> _services = new();

    public AmethystInstanceTests()
    {
        _stage = Path.Combine(_root, "stage");
        _game = Path.Combine(_root, "Steam", "steamapps", "common", "Skyrim Special Edition");
        _data = Path.Combine(_game, "Data");
        _config = Path.Combine(_root, "config");
        Directory.CreateDirectory(_data);
        FaceNpcPlugin.Write(_data, "Skyrim.esm", 0x800);
        AmethystInstance.ConfigRoot = () => _config;
    }

    public void Dispose()
    {
        AmethystInstance.ConfigRoot = _realConfigRoot;
        foreach (var s in _services) s.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { /* temp scratch */ }
    }

    /// <summary>A profile folder under the staging root with its text files and profile_state.json; returns the profile folder.</summary>
    string Profile(string name, string[] modlist, string[] plugins, string state = "{}", bool specific = false, string? gamePath = null)
    {
        var dir = Path.Combine(_stage, "profiles", name);
        Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir, "modlist.txt"), modlist);
        File.WriteAllLines(Path.Combine(dir, "plugins.txt"), plugins.Where(p => p.StartsWith('*') || p.Length > 0));
        File.WriteAllLines(Path.Combine(dir, "loadorder.txt"), new[] { "Skyrim.esm" }.Concat(plugins.Select(p => p.TrimStart('*'))));
        var settings = $"{{\"profile_specific_mods\": {(specific ? "true" : "false")}" + (gamePath is null ? "" : $", \"game_path\": \"{gamePath.Replace("\\", "\\\\")}\"") + "}";
        File.WriteAllText(Path.Combine(dir, AmethystInstance.StateFileName), state == "{}" ? $"{{\"profile_settings\": {settings}}}" : state.Replace("\"@settings\"", settings));
        Directory.CreateDirectory(specific ? Path.Combine(dir, "mods") : Path.Combine(_stage, "mods"));
        return dir;
    }

    string Mods(string profile, bool specific) => specific ? Path.Combine(profile, "mods") : Path.Combine(_stage, "mods");

    static void Put(string modDir, string rel, string text = "x")
    {
        var p = DiskPath.Join(modDir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text);
    }

    LoadOrderService Open(string profileDir)
    {
        var svc = LoadOrderService.WithInstance(profileDir, 0, new UserConfigStore(Path.Combine(_root, "user-" + _services.Count + ".json")));
        _services.Add(svc);
        return svc;
    }

    [Fact]
    public void AProfileSpecificProfileKeepsModsOverwriteAndRootFolderInsideItself()
    {
        var profile = Profile("P1", ["+A"], [], specific: true, gamePath: _game);

        var p = Mo2Instance.Resolve(profile);

        Assert.Equal("Amethyst", p.Source);
        Assert.Equal(Path.Combine(profile, "mods"), p.ModsDir);
        Assert.Equal(Path.Combine(profile, "overwrite"), p.OverwriteDir);
        Assert.Equal(Path.Combine(profile, "Root_Folder"), p.RootFolderDir);
        Assert.Equal(_data, p.DataDir);
    }

    [Fact]
    public void AProfileThatSharesModsKeepsThemBesideTheProfilesFolder()
    {
        var profile = Profile("P2", ["+A"], [], specific: false, gamePath: _game);

        var p = Mo2Instance.Resolve(profile);

        Assert.Equal(Path.Combine(_stage, "mods"), p.ModsDir);
        Assert.Equal(Path.Combine(_stage, "overwrite"), p.OverwriteDir);
    }

    [Fact]
    public void TheGameFolderComesFromTheGameConfigWhenTheProfileSetsNone()
    {
        var profile = Profile("P3", ["+A"], []);
        Directory.CreateDirectory(Path.Combine(_config, "games", "Skyrim Special Edition"));
        File.WriteAllText(Path.Combine(_config, "games", "Skyrim Special Edition", "paths.json"),
            "{\"game_path\": \"" + _game + "\", \"staging_path\": \"" + _stage + "\", \"prefix_path\": \"/pfx\"}");

        var p = Mo2Instance.Resolve(profile);

        Assert.Equal(_data, p.DataDir);
        Assert.Equal("/pfx", p.PrefixDir);
    }

    [Fact]
    public void AProfileWithNoGameFolderAnywhereIsRefusedNamingIt()
    {
        var profile = Profile("P4", ["+A"], []);

        var ex = Assert.Throws<InvalidOperationException>(() => Mo2Instance.Resolve(profile));

        Assert.Contains("game_path", ex.Message);
    }

    [Fact]
    public void AProfileRootNeedsAProfileNameOrTheOneAmethystLastHadActive()
    {
        Profile("One", ["+A"], [], gamePath: _game);
        Profile("Two", ["+A"], [], gamePath: _game);

        Assert.Equal(Path.Combine(_stage, "profiles", "Two"), Mo2Instance.Locate(_stage, "two"));
        var ex = Assert.Throws<InvalidOperationException>(() => Mo2Instance.Locate(_stage, null));
        Assert.Contains("'One'", ex.Message);
        Assert.Contains("profile=", ex.Message);

        var games = Path.Combine(_config, "games", "Skyrim Special Edition");
        Directory.CreateDirectory(games);
        File.WriteAllText(Path.Combine(games, "paths.json"), "{\"staging_path\": \"" + _stage + "\"}");
        File.WriteAllText(Path.Combine(games, "deploy_state.json"), "{\"last_active_profile\": \"One\"}");
        Assert.Equal(_stage, Mo2Instance.Locate(_stage, null));
        Assert.Equal("One", Mo2Instance.ReadSelectedProfile(_stage));
        Assert.Equal(Path.Combine(_stage, "profiles", "Two"), Mo2Instance.Locate(_stage, "Two"));
    }

    [Fact]
    public void AStagingRootFollowsTheProfileAmethystHasActiveBetweenCalls()
    {
        Profile("Alpha", ["+ModA"], [], gamePath: _game);
        Profile("Beta", ["+ModB"], [], gamePath: _game);
        var mods = Path.Combine(_stage, "mods");
        Put(Path.Combine(mods, "ModA"), Rel);
        Put(Path.Combine(mods, "ModB"), Rel);
        var games = Path.Combine(_config, "games", "Skyrim Special Edition");
        Directory.CreateDirectory(games);
        File.WriteAllText(Path.Combine(games, "paths.json"), "{\"staging_path\": \"" + _stage + "\"}");
        var state = Path.Combine(games, "deploy_state.json");
        File.WriteAllText(state, "{\"last_active_profile\": \"Alpha\"}");
        var svc = Open(_stage);

        Assert.Equal("ModA", svc.AssetStatus([Rel]).Results[0].Hit!.Winner!.Source);

        File.WriteAllText(state, "{\"last_active_profile\":   \"Beta\", \"x\": 1}");

        Assert.Equal("ModB", svc.AssetStatus([Rel]).Results[0].Hit!.Winner!.Source);
    }

    [Fact]
    public void SetInstanceOnAStagingRootWithNoProfileKeepsTheRootAndFollowsTheActiveProfile()
    {
        Profile("Act", ["+A"], [], gamePath: _game);
        var games = Path.Combine(_config, "games", "Skyrim Special Edition");
        Directory.CreateDirectory(games);
        File.WriteAllText(Path.Combine(games, "paths.json"), "{\"staging_path\": \"" + _stage + "\"}");
        File.WriteAllText(Path.Combine(games, "deploy_state.json"), "{\"last_active_profile\": \"Act\"}");

        var (paths, _, _, _) = Open(_stage).SetInstance(_stage);

        Assert.Equal(_stage, paths.InstanceDir);
        Assert.Equal("Act", paths.ProfileName);
        Assert.Equal("Amethyst", paths.Source);
    }

    [Fact]
    public void AnUnknownProfileNamesTheOnesThatExist()
    {
        Profile("Only", ["+A"], [], gamePath: _game);

        var ex = Assert.Throws<InvalidOperationException>(() => Mo2Instance.Locate(_stage, "Nope"));

        Assert.Contains("'Only'", ex.Message);
    }

    [Fact]
    public void ModPriorityRunsTopFirstAndAnAlwaysOnMarkerIsEnabled()
    {
        var profile = Profile("Prio", ["+High", "*Pinned", "-Off", "+Low", "-Section_separator"], [], gamePath: _game);
        var mods = Mods(profile, false);
        foreach (var m in new[] { "High", "Pinned", "Off", "Low" }) Put(Path.Combine(mods, m), Rel);
        var svc = Open(profile);

        var hit = svc.AssetStatus([Rel]).Results[0].Hit!;

        Assert.Equal("High", hit.Winner!.Source);
        Assert.Equal(new[] { "High", "Pinned", "Low" }, hit.Providers.Select(x => x.Source));
    }

    [Fact]
    public void OverwriteBeatsEveryModAndTheGameDataIsTheFloor()
    {
        var profile = Profile("Ov", ["+Mod"], [], specific: true, gamePath: _game);
        Put(Path.Combine(profile, "mods", "Mod"), Rel);
        Put(Path.Combine(profile, "overwrite"), Rel);
        Put(_data, Rel);
        var svc = Open(profile);

        var hit = svc.AssetStatus([Rel]).Results[0].Hit!;

        Assert.Equal(new[] { "overwrite", "Mod", "Data" }, hit.Providers.Select(x => x.Source));
    }

    [Fact]
    public void ALooseFileInASubfolderIsFoundWhateverTheCasingOfThePathOrTheFolder()
    {
        var profile = Profile("Case", ["+Mod"], [], gamePath: _game);
        Put(Path.Combine(Mods(profile, false), "Mod"), "SKSE/Plugins/HcPlugin.dll");
        var svc = Open(profile);

        foreach (var asked in new[] { @"skse\plugins\hcplugin.dll", "SKSE/Plugins/HCPLUGIN.DLL", @"Skse\Plugins\HcPlugin.dll" })
            Assert.Equal("Mod", svc.AssetStatus([asked]).Results[0].Hit!.Winner?.Source);
    }

    [Fact]
    public void AFileSwitchedOffOrRoutedToTheGameRootIsNotInData_AndOnlyForThatMod()
    {
        const string off = "textures/a/off.dds", root = "root/d3d11.dll";
        var state = "{\"profile_settings\": {}, \"excluded_mod_files\": {\"Mod\": [\"" + off + "\"]}, \"root_mod_files\": {\"Mod\": [\"" + root + "\"]}}";
        var profile = Profile("Vis", ["+Mod", "+Other"], [], state, gamePath: _game);
        var mods = Mods(profile, false);
        foreach (var rel in new[] { off, root, "meta.ini", "textures/a/kept.dds" }) Put(Path.Combine(mods, "Mod"), rel);
        Put(Path.Combine(mods, "Other"), off);
        File.WriteAllText(Path.Combine(profile, AmethystInstance.StateFileName), state.Replace("\"profile_settings\": {}", "\"profile_settings\": {\"game_path\": \"" + _game + "\"}"));
        var svc = Open(profile);

        Assert.Equal("Other", svc.AssetStatus([off]).Results[0].Hit!.Winner?.Source);
        Assert.False(svc.AssetStatus([root]).Results[0].Hit!.Exists);
        Assert.False(svc.AssetStatus(["meta.ini"]).Results[0].Hit!.Exists);
        Assert.Equal("Mod", svc.AssetStatus(["textures/a/kept.dds"]).Results[0].Hit!.Winner?.Source);
    }

    [Fact]
    public void AMohiddenSuffixIsNotHiddenBecauseAmethystDeploysIt()
    {
        var profile = Profile("Moh", ["+Mod"], [], gamePath: _game);
        Put(Path.Combine(Mods(profile, false), "Mod"), "textures/a/x.dds.mohidden");
        var svc = Open(profile);

        Assert.True(svc.AssetStatus(["textures/a/x.dds.mohidden"]).Results[0].Hit!.Exists);
    }

    [Fact]
    public void PluginsResolveByNameInAnyCaseAndTheModsComeFromASymlinkedMods()
    {
        if (OperatingSystem.IsWindows()) return;
        var profile = Profile("Link", ["+Mod"], ["*dummy.esp"], specific: true, gamePath: _game);
        var store = Path.Combine(_root, "store", "mods");
        Directory.Delete(Path.Combine(profile, "mods"));
        FaceNpcPlugin.Write(Path.Combine(store, "Mod"), "Dummy.esp", 0x801);
        Directory.CreateSymbolicLink(Path.Combine(profile, "mods"), store);
        var svc = Open(profile);

        var status = StatusTools.LoadOrderStatus(svc, new ToolPathResolver(new UserConfigStore(Path.Combine(_root, "tools.json"))));

        Assert.Contains("2 plugins resolved", status.Replace("\r", ""), StringComparison.Ordinal);
    }

    [Fact]
    public void TheProfilesOwnIniFolderIsReadByAnyCasingOfItsFileNames()
    {
        var dir = Path.Combine(_root, "ini files");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "skyrim.ini"), "[Archive]\nsResourceArchiveList=A.bsa, B.bsa\n");

        var (value, ini) = GameIni.Local(dir).Value("Archive", "sResourceArchiveList");

        Assert.Equal("A.bsa, B.bsa", value);
        Assert.Equal(Path.Combine(dir, "skyrim.ini"), ini);
    }

    [Fact]
    public void ProfileIniFilesAreUsedOnlyWhenTheProfileRunsTheGameOnThem()
    {
        var on = Profile("IniOn", ["+A"], [], "{\"profile_settings\": {\"game_path\": \"" + _game + "\", \"profile_ini_files\": true}}");
        var off = Profile("IniOff", ["+A"], [], "{\"profile_settings\": {\"game_path\": \"" + _game + "\"}}");

        Assert.Equal(Path.Combine(on, "ini files"), Mo2Instance.Resolve(on).IniFolder);
        Assert.Null(Mo2Instance.Resolve(off).IniFolder);
    }

    [Fact]
    public void SetInstanceTakesAProfileRootWithAProfileNameAndNamesTheLayoutItIsNot()
    {
        Profile("Set", ["+A"], [], gamePath: _game);
        var svc = Open(Path.Combine(_stage, "profiles", "Set"));

        var (paths, _, _, _) = svc.SetInstance(_stage, "Set");

        Assert.Equal("Amethyst", paths.Source);
        Assert.Equal(Path.Combine(_stage, "profiles", "Set"), paths.InstanceDir);
        var notEither = Path.Combine(_root, "empty");
        Directory.CreateDirectory(notEither);
        Assert.Contains("ModOrganizer.ini", Assert.Throws<InvalidOperationException>(() => svc.SetInstance(notEither)).Message);
    }

    [Fact]
    public void ANewPluginLandsInTheOverwriteFolderNotInANewModFolder_AndNeverInTheModsStaging()
    {
        var profile = Profile("Write", ["+Mod"], ["*Dummy.esp"], specific: true, gamePath: _game);
        FaceNpcPlugin.Write(Path.Combine(profile, "mods", "Mod"), "Dummy.esp", 0x801);
        var svc = Open(profile);

        var made = svc.CreatePlugin("HcTrigger");

        Assert.True(made.Success, made.Error);
        Assert.Equal(Path.Combine(profile, "overwrite", "HcTrigger.esp"), made.OutputPath);
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(profile, "mods"), "houseCARL*"));
        Assert.False(File.Exists(Path.Combine(profile, "overwrite", "meta.ini")));
        var again = svc.CreatePlugin("HcTrigger");
        Assert.False(again.Success);
        Assert.Contains("overwrite", again.Error);
        Assert.True(File.Exists(made.OutputPath));
    }

    [Fact]
    public void IntoNamesNoFolderUnderAmethystAndSaysWhere()
    {
        var profile = Profile("Into", ["+Mod"], [], gamePath: _game);
        var svc = Open(profile);

        var ex = Assert.Throws<InvalidOperationException>(() => svc.ResolvePatchModFolder(null, "Anything", "houseCARL_X", null));

        Assert.Contains("overwrite", ex.Message);
    }
}

/// <summary>The file version of a real PE is read without Windows; a missing or non-PE file has none.</summary>
[Trait("tier", "unit")]
public sealed class PeFileVersionTests
{
    [Fact]
    public void ADotnetAssemblyCarriesItsVersionResource()
    {
        var v = PeFileVersion.Read(typeof(object).Assembly.Location);

        Assert.NotNull(v);
        Assert.Matches(@"^\d+\.\d+\.\d+\.\d+$", v);
    }

    [Fact]
    public void ANonPeFileAndAMissingFileHaveNone()
    {
        var text = Path.Combine(Path.GetTempPath(), "hc-pe-" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllText(text, "not a pe");
        try { Assert.Null(PeFileVersion.Read(text)); }
        finally { File.Delete(text); }
        Assert.Null(PeFileVersion.Read(text));
    }
}

