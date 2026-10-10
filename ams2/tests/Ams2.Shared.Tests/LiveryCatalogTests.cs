using Ams2.Shared.Liveries;

namespace Ams2.Shared.Tests;

public sealed class LiveryCatalogTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "ams2-liv-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    void Write(string rel, string xml)
    {
        string p = Path.Combine(_root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, xml, new System.Text.UTF8Encoding(true)); // com BOM, como os do jogo
    }

    void Game()
    {
        Write(Path.Combine("UserData", "CustomAIDrivers", "F-V8_Gen1.xml"), """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <custom_ai_drivers>
              <driver livery_name="Kimi Raikkonen #3"><name>Kimi Räikkönen</name><country>FIN</country></driver>
              <driver livery_name="Sem Override #9"><name>Fulano de Tal</name><country>BRA</country></driver>
            </custom_ai_drivers>
            """.TrimStart());
        Write(Path.Combine("UserData", "CustomAIDrivers", "F-V8_Gen1_2006_Equal.xml"), """
            <custom_ai_drivers><driver livery_name="Kimi Raikkonen #3"><name>Outro</name><country>XXX</country></driver></custom_ai_drivers>
            """);
        Write(Path.Combine("UserData", "CustomAIDrivers", "quebrado.xml"), "<custom_ai_drivers><driver");
        Write(Path.Combine("Vehicles", "Textures", "CustomLiveries", "Overrides", "formula_v8_g1_m", "formula_v8_g1_m.xml"), """
            <USER_OVERRIDES>
              <LIVERY_OVERRIDE LIVERY="51" NAME="Kimi Raikkonen #3" BASELIVERY="SilverChrome">
                <PREVIEWIMAGE PATH="F1 2006 Skinpack\McLaren\Preview Raikkonen.dds" />
                <TEXTURE NAME="BODY" PATH="F1 2006 Skinpack\McLaren\Livery Raikkonen.dds" />
              </LIVERY_OVERRIDE>
              <LIVERY_OVERRIDE LIVERY="52" NAME="Mod Driver #77">
                <TEXTURE NAME="BODY" PATH="Mods/Ferrari/Livery Mod.dds" />
              </LIVERY_OVERRIDE>
              <OUTFIT_OVERRIDE LIVERY="51" BASEOUTFIT="DEFAULT" />
            </USER_OVERRIDES>
            """);
    }

    [Fact]
    public void Joins_overrides_with_ai_drivers_and_takes_the_team_from_the_body_texture_folder()
    {
        Game();
        var all = LiveryCatalog.Load(_root);
        var kimi = Assert.Single(all, e => e.Livery == "Kimi Raikkonen #3");
        Assert.Equal(("Kimi Räikkönen", "FIN", "McLaren", "formula_v8_g1_m"), (kimi.Driver, kimi.Country, kimi.Team, kimi.Model)); // o primeiro arquivo de IA ganha
    }

    [Fact]
    public void Livery_without_an_ai_entry_uses_the_name_without_the_number_and_slash_paths_work()
    {
        Game();
        var mod = Assert.Single(LiveryCatalog.Load(_root), e => e.Livery == "Mod Driver #77");
        Assert.Equal(("Mod Driver", "", "Ferrari"), (mod.Driver, mod.Country, mod.Team));
    }

    [Fact]
    public void Ai_drivers_without_override_are_listed_with_the_class_as_model_and_broken_files_are_skipped()
    {
        Game();
        var all = LiveryCatalog.Load(_root);
        var fulano = Assert.Single(all, e => e.Livery == "Sem Override #9");
        Assert.Equal(("Fulano de Tal", "BRA", "", "F-V8_Gen1"), (fulano.Driver, fulano.Country, fulano.Team, fulano.Model));
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public void Filter_requires_every_term_in_any_column_ignoring_case()
    {
        Game();
        var all = LiveryCatalog.Load(_root);
        Assert.Single(LiveryCatalog.Filter(all, "mclaren fin"));
        Assert.Single(LiveryCatalog.Filter(all, "  FERRARI "));
        Assert.Empty(LiveryCatalog.Filter(all, "mclaren ferrari"));
        Assert.Equal(3, LiveryCatalog.Filter(all, "").Count());
    }

    [Fact]
    public void Game_root_is_recognised_by_its_data_folders()
    {
        Assert.False(LiveryCatalog.IsGameRoot(_root));
        Game();
        Assert.True(LiveryCatalog.IsGameRoot(_root));
        Assert.Equal(_root, LiveryCatalog.FindGameRoot(_root));
        Assert.False(LiveryCatalog.IsGameRoot(null));
    }

    [Fact]
    public void Loads_the_real_game_catalog_when_installed()
    {
        var root = LiveryCatalog.FindGameRoot();
        if (root is null) return;
        var all = LiveryCatalog.Load(root);
        Assert.NotEmpty(all);
        Assert.Contains(all, e => e.Team.Length > 0 && e.Country.Length == 3);
    }
}
