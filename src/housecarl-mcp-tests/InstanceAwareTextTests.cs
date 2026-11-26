using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using HousecarlCore;
using HousecarlMcp;
using Xunit;

namespace HousecarlMcpTests;

/// <summary>The user-facing text names the manager the active instance is: MO2 wording unchanged for an MO2 instance, Amethyst
/// wording for an Amethyst profile; static text carries both.</summary>
[Trait("tier", "unit")]
public sealed class InstanceAwareTextTests
{
    static Mo2InstancePaths Paths(string source) =>
        new("/i", "Default", "/i/profiles/Default", "/i/mods", "/g/Data", "/g", "/i/overwrite") { Source = source };

    [Fact]
    public void AnMo2InstanceIsReportedWithItsOriginalSetupWording()
    {
        var text = SetupTools.Render(Paths("MO2"), true, null, null);

        Assert.Contains("configured houseCARL -> MO2 instance '/i'", text);
        Assert.Contains("(auto-detected from ModOrganizer.ini)", text);
        Assert.Contains("MO2 creates it when it next starts", text);
        Assert.DoesNotContain("Amethyst", text);
    }

    [Fact]
    public void AnAmethystProfileIsReportedAsAnAmethystProfile()
    {
        var text = SetupTools.Render(Paths("Amethyst"), true, null, null);

        Assert.Contains("configured houseCARL -> Amethyst profile '/i'", text);
        Assert.Contains("read as empty until Amethyst creates it", text);
        Assert.DoesNotContain("MO2", text);
    }

    [Fact]
    public void AnMo2PatchFolderGetsTheEnableInMo2Hint_AndTheOverwriteFolderTheAmethystOne()
    {
        var mo2 = WriteSentences.NewOrExtendedArtifact(false, "P.esp", 10, "houseCARL - P");
        var amethyst = WriteSentences.NewOrExtendedArtifact(false, "P.esp", 10, "overwrite");

        Assert.Contains("enable it in MO2 to use the patch; MO2 adds a newly activated plugin at the END of the load order", mo2);
        Assert.DoesNotContain("Amethyst", mo2);
        Assert.Contains("Amethyst registers a new plugin from its overwrite folder on the next refresh or deploy", amethyst);
        Assert.DoesNotContain("MO2", amethyst);
    }

    [Fact]
    public void TheUnconfiguredPromptKeepsTheMo2AskAndAddsTheAmethystOne()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hc-itext-" + Guid.NewGuid().ToString("N"));
        using var svc = LoadOrderService.WithInstance(null, 0, new UserConfigStore(Path.Combine(dir, "user.json")));

        var prompt = svc.ConfigPromptOrNull()!;

        Assert.Contains("no Mod Organizer 2 instance configured yet", prompt);
        Assert.Contains("the folder that contains ModOrganizer.ini", prompt);
        Assert.Contains("Amethyst profile folder", prompt);
    }

    [Fact]
    public void TheSetInstanceToolDescriptionKeepsItsMo2SentencesAndDocumentsAmethyst()
    {
        var m = typeof(SetupTools).GetMethod(nameof(SetupTools.SetMo2Instance))!;
        var d = m.GetCustomAttribute<DescriptionAttribute>()!.Description;

        Assert.StartsWith("Point houseCARL at your Mod Organizer 2 instance folder. houseCARL derives the mods folder", d);
        Assert.Contains("A folder that is not an MO2 instance changes nothing.", d);
        Assert.Contains("Amethyst Mod Manager profile", d);
        Assert.Contains("profile=", d);
    }
}

[Collection("server")]
[Trait("tier", "stdio")]
public sealed class InstanceAwareInstructionsTests
{
    readonly ServerFixture _s;
    public InstanceAwareInstructionsTests(ServerFixture s) { _s = s; }

    [Fact]
    public void TheStandingInstructionsKeepTheMo2WordingAndAddTheAmethystEquivalent()
    {
        var t = _s.PublishedInstructions;

        Assert.Contains("via a live MO2 instance (or Amethyst)", t);
        Assert.Contains("until the user enables its mod in MO2 (Amethyst: it lands in overwrite; refresh or deploy picks it up)", t);
    }
}

[Trait("tier", "unit")]
public sealed class PluginManifestUserConfigTests
{
    static JsonElement UserConfig() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(HarnessPaths.RepoRoot, "plugin", ".claude-plugin", "plugin.json")))
            .RootElement.GetProperty("userConfig");

    [Fact]
    public void TheMo2InstanceFolderOptionStaysAndNoLongerBlocksAnAmethystInstall()
    {
        var mo2 = UserConfig().GetProperty("mo2_instance_dir");

        Assert.Equal("directory", mo2.GetProperty("type").GetString());
        Assert.Contains("ModOrganizer.ini", mo2.GetProperty("description").GetString());
        Assert.False(mo2.GetProperty("required").GetBoolean());
    }

    [Fact]
    public void AnOptionalAmethystProfileFolderOptionExists()
    {
        var a = UserConfig().GetProperty("amethyst_profile_dir");

        Assert.Equal("directory", a.GetProperty("type").GetString());
        Assert.False(a.GetProperty("required").GetBoolean());
    }

    [Fact]
    public void TheServerReadsTheAmethystProfileFolderFromTheSameConfigSectionAsTheMo2One()
    {
        var program = File.ReadAllText(Path.Combine(HarnessPaths.RepoRoot, "src", "housecarl-mcp", "Program.cs"));

        Assert.Contains("cfg[\"Mo2InstanceDir\"]", program);
        Assert.Contains("cfg[\"AmethystProfileDir\"]", program);
    }
}
