using Ams2.Shared.PlayerNames;

namespace Ams2.Shared.Tests;

public sealed class PlayerNameStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "ams2-pn-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Theory]
    [InlineData("McLaren Cosworth MP4/8", "McLaren Cosworth MP4/8")]
    [InlineData("McLaren Cosworth MP4/8 (M)", "McLaren Cosworth MP4/8")]
    [InlineData("  Formula Classic Gen2 (B) ", "Formula Classic Gen2")]
    [InlineData("Car (X)", "Car (X)")]
    public void Model_key_drops_the_tyre_suffix(string car, string key) => Assert.Equal(key, PlayerNameStore.ModelKey(car));

    [Fact]
    public void Set_get_is_case_insensitive_and_ignores_suffix()
    {
        var s = new PlayerNameStore(_dir);
        s.Set("McLaren Cosworth MP4/8", "  Miko Hanninen ");
        Assert.Equal("Miko Hanninen", s.Get("mclaren cosworth mp4/8 (M)")!.Name);
        Assert.Null(s.Get("Outro Carro"));
        s.Set("MCLAREN COSWORTH MP4/8 (B)", "");
        Assert.Null(s.Get("McLaren Cosworth MP4/8"));
    }

    [Fact]
    public void Persists_versioned_json_and_reloads()
    {
        var a = new PlayerNameStore(_dir);
        a.Observe("Formula V10 Gen3 Model2", "Vitor COSTA");
        a.Set("Formula V10 Gen3 Model2", "Ayrton Senna");
        string json = File.ReadAllText(Path.Combine(_dir, "player-names.json"));
        Assert.Contains("\"version\": 1", json);
        var b = new PlayerNameStore(_dir);
        Assert.Equal("Ayrton Senna", b.Get("Formula V10 Gen3 Model2")!.Name);
        var e = Assert.Single(b.State().Entries);
        Assert.Equal(("Vitor COSTA", "Ayrton Senna"), (e.OriginalName, e.Name));
    }

    [Fact]
    public void Observe_tracks_current_and_only_notifies_on_change()
    {
        var s = new PlayerNameStore(_dir);
        int n = 0; s.Changed += () => n++;
        s.Observe("Car A (M)", "Vitor COSTA");
        s.Observe("Car A", "Vitor COSTA");
        Assert.Equal(1, n);
        Assert.Equal("Car A", s.State().CurrentModel);
        s.Observe("", "");                               // sem carro detectado
        Assert.Equal("", s.State().CurrentModel);
    }

    [Fact]
    public void SetIdentity_stores_livery_driver_country_and_team_and_clear_removes_them()
    {
        var s = new PlayerNameStore(_dir);
        s.SetIdentity("Formula V8 Gen1 Model1 (M)", "Kimi Raikkonen #3", " Kimi Raikkonen ", "fin", " McLaren ");
        var g = s.Get("formula v8 gen1 model1")!;
        Assert.Equal(("Kimi Raikkonen #3", "Kimi Raikkonen", "fin", "McLaren"), (g.Livery, g.Name, g.Country, g.Team));
        var b = new PlayerNameStore(_dir);                           // persistido
        Assert.Equal("McLaren", b.Get("Formula V8 Gen1 Model1")!.Team);
        s.Set("Formula V8 Gen1 Model1", "Outro");                    // so o nome muda
        Assert.Equal(("Outro", "McLaren"), (s.Get("Formula V8 Gen1 Model1")!.Name, s.Get("Formula V8 Gen1 Model1")!.Team));
        s.Clear("Formula V8 Gen1 Model1");
        Assert.Null(s.Get("Formula V8 Gen1 Model1"));
    }

    [Fact]
    public void Team_only_override_counts_and_a_legacy_file_without_the_new_fields_loads()
    {
        var s = new PlayerNameStore(_dir);
        s.SetIdentity("Car T", "", "", "", "Ferrari");
        Assert.Equal("Ferrari", s.Get("Car T")!.Team);
        File.WriteAllText(s.FilePath!, "{\"version\":1,\"entries\":[{\"model\":\"Old\",\"originalName\":\"V\",\"suggested\":\"X\",\"name\":\"Antigo\"}]}");
        var b = new PlayerNameStore(_dir);
        Assert.Equal("Antigo", b.Get("Old")!.Name);
    }

    [Fact]
    public void Refresh_picks_up_external_edits_and_ignores_corrupt_or_newer_files()
    {
        var s = new PlayerNameStore(_dir);
        s.Set("A", "Nome A");
        int n = 0; s.Changed += () => n++;
        Thread.Sleep(30);
        File.WriteAllText(s.FilePath!, "{\"version\":1,\"entries\":[{\"model\":\"A (M)\",\"name\":\"Editado\"}]}");
        s.Refresh();
        Assert.Equal("Editado", s.Get("A")!.Name);
        Assert.Equal(1, n);
        s.Refresh();
        Assert.Equal(1, n);
        File.WriteAllText(s.FilePath!, "{ lixo");
        s.Refresh();
        Assert.Equal("Editado", s.Get("A")!.Name);      // mantem o estado em memoria
        File.WriteAllText(s.FilePath!, "{\"version\":99,\"entries\":[{\"model\":\"A\",\"name\":\"Futuro\"}]}");
        Assert.Null(new PlayerNameStore(_dir).Get("A")); // esquema mais novo: ignorado
    }

    [Fact]
    public void In_memory_store_writes_nothing()
    {
        var s = PlayerNameStore.InMemory();
        s.Set("A", "X");
        Assert.Equal("X", s.Get("A")!.Name);
        Assert.Null(s.FilePath);
    }
}
