using Ams2.Shared.Ipc;
using Ams2.Shared.Victory;

namespace Ams2.Shared.Tests;

public class VictoryTests
{
    [Theory]
    [InlineData("Ayrton Senna", VictoryTheme.Senna)]
    [InlineData("AYRTON SENNA", VictoryTheme.Senna)]
    [InlineData("A. Senna", VictoryTheme.Senna)]
    [InlineData("Senna", VictoryTheme.Senna)]
    [InlineData("Ayrton da Silva SENNA", VictoryTheme.Senna)]
    [InlineData("AyrtonSenna", VictoryTheme.Senna)]
    [InlineData("Rubens Barrichello", VictoryTheme.Barrichello)]
    [InlineData("R. BARRICHELLO", VictoryTheme.Barrichello)]
    [InlineData("barrichello", VictoryTheme.Barrichello)]
    [InlineData("Felipe Massa", VictoryTheme.Massa)]
    [InlineData("Massa", VictoryTheme.Massa)]
    [InlineData("F. MASSA", VictoryTheme.Massa)]
    [InlineData("  felipe   massa  ", VictoryTheme.Massa)]
    public void Surname_selects_the_theme(string name, VictoryTheme expected) => Assert.Equal(expected, VictoryThemeSelector.Select(name));

    [Theory]
    [InlineData("Ayrton")]
    [InlineData("Rubens")]
    [InlineData("Felipe")]
    [InlineData("Ayrton Costa")]
    [InlineData("Vitor COSTA")]
    [InlineData("Massaro")]          // "massa" so como palavra inteira
    [InlineData("Massachusetts Racer")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void First_name_alone_or_other_names_use_default(string? name) => Assert.Equal(VictoryTheme.Default, VictoryThemeSelector.Select(name));

    [Fact]
    public void Accents_and_case_are_ignored() => Assert.Equal(VictoryTheme.Senna, VictoryThemeSelector.Select("Ayrton Sénnà"));

    [Fact]
    public void Two_surnames_prefer_senna_then_barrichello()
    {
        Assert.Equal(VictoryTheme.Senna, VictoryThemeSelector.Select("Senna Massa"));
        Assert.Equal(VictoryTheme.Barrichello, VictoryThemeSelector.Select("Massa Barrichello"));
    }

    [Fact]
    public void Resolve_falls_back_to_default_then_silence()
    {
        var cfg = new VictoryConfig { DefaultPath = "d.mp3", SennaPath = "s.mp3" };
        Assert.Equal("s.mp3", cfg.Resolve(VictoryTheme.Senna, _ => true));
        Assert.Equal("d.mp3", cfg.Resolve(VictoryTheme.Massa, _ => true));          // sem arquivo no tema: Padrao
        Assert.Equal("d.mp3", cfg.Resolve(VictoryTheme.Senna, p => p == "d.mp3"));  // arquivo do tema sumiu: Padrao
        Assert.Null(cfg.Resolve(VictoryTheme.Senna, _ => false));
        Assert.Null(new VictoryConfig().Resolve(VictoryTheme.Default, _ => true));  // nada configurado: silencio
    }

    [Fact]
    public void Store_roundtrip_import_and_clear()
    {
        string root = Path.Combine(Path.GetTempPath(), "ams2-victory-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            string src = Path.Combine(root, "meu som.mp3");
            File.WriteAllBytes(src, [1, 2, 3]);
            var store = new VictoryStore(Path.Combine(root, "cfg"));
            Assert.False(store.Current.Enabled);

            string dest = store.ImportFile(VictoryTheme.Senna, src);
            Assert.Equal(Path.Combine(root, "cfg", "victory", "senna.mp3"), dest);
            Assert.True(File.Exists(dest));
            store.Save(store.Current.WithPath(VictoryTheme.Senna, dest) with { Enabled = true, VolumePct = 150 });

            var again = new VictoryStore(Path.Combine(root, "cfg"));
            Assert.True(again.Current.Enabled);
            Assert.Equal(100, again.Current.VolumePct);   // normalizado
            Assert.Equal(dest, again.Current.SennaPath);
            Assert.True(File.Exists(Path.Combine(root, "cfg", VictoryStore.FileName)));

            store.DeleteCopies(VictoryTheme.Senna);
            Assert.False(File.Exists(dest));
            Assert.Throws<InvalidOperationException>(() => store.ImportFile(VictoryTheme.Massa, Path.Combine(root, "x.txt")));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Ipc_message_carries_victory_fields()
    {
        var msg = new IpcMessage { Cmd = IpcCommands.SetVictory, Victory = new VictoryConfig { Enabled = true, MassaPath = "m.mp3" }, VictoryTheme = "Massa" };
        var back = IpcProtocol.TryParse(IpcProtocol.Serialize(msg));
        Assert.NotNull(back);
        Assert.Equal("setVictory", back!.Cmd);
        Assert.Equal("m.mp3", back.Victory!.MassaPath);
        Assert.Equal("Massa", back.VictoryTheme);
    }
}
