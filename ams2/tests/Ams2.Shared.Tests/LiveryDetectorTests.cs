using System.Text;
using Ams2.Shared.Liveries;
using Xunit;

namespace Ams2.Shared.Tests;

public class LiveryDetectorTests
{
    [Fact]
    public void ParseRecord_le_o_nome_depois_de_hash_e_u32()
    {
        var folder = Encoding.ASCII.GetBytes("formula_v8_g1_b");
        var name = Encoding.ASCII.GetBytes("Michael Schumacher #5");
        var buf = folder.Concat(new byte[] { 0x12, 0x46, 0x32, 0x65 }).Concat(BitConverter.GetBytes(25)).Concat(BitConverter.GetBytes(name.Length)).Concat(name).Concat(new byte[8]).ToArray();
        Assert.Equal("Michael Schumacher #5", GameLiveryProbe.ParseRecord(buf, folder.Length));
    }

    [Fact]
    public void ParseRecord_rejeita_lixo()
    {
        var buf = new byte[64];
        Assert.Null(GameLiveryProbe.ParseRecord(buf, 4));                 // tamanho 0
        BitConverter.GetBytes(10).CopyTo(buf, 12); buf[16] = 1;           // byte nao imprimivel
        Assert.Null(GameLiveryProbe.ParseRecord(buf, 4));
        Assert.Null(GameLiveryProbe.ParseRecord(buf, 60));                // fora do buffer
    }

    [Theory]
    [InlineData("formula_v8_g1_b", "Formula V8 Gen1 Model1 (B) - Low Downforce", true)]
    [InlineData("formula_v8_g1_m", "Formula V8 Gen1 Model1 (B) - Low Downforce", false)]
    [InlineData("renault_r26", "Renault R26 - Low Downforce", true)]
    [InlineData("renault_r26", "Formula V8 Gen1 Model1 (B) - Low Downforce", false)]
    [InlineData("mclaren_mp48", "McLaren Cosworth MP4/8", true)]
    [InlineData("mclaren_mp48", "McLaren Cosworth MP4/8 - High Downforce", true)]
    [InlineData("mclaren_mp48", "McLaren Cosworth MP4/8 (M)", true)]
    [InlineData("mclaren_mp48", "McLaren MP4/6", false)]
    [InlineData("mclaren_mp48", "Formula HiTech Gen2 Model1", false)]
    [InlineData("formula_hitech_g2m1", "Formula HiTech Gen2 Model1", true)]
    [InlineData("formula_hitech_g2m1", "Formula HiTech Gen2 Model2", false)]
    public void FolderMatchesCar_casa_pasta_com_nome_do_carro(string folder, string car, bool expected)
        => Assert.Equal(expected, LiveryDetector.FolderMatchesCar(folder, car));

    static readonly List<LiveryEntry> Cat =
    [
        new("Felipe Massa #6", "Felipe Massa", "BRA", "Ferrari", "formula_v8_g1_b") { Class = "F-V8_Gen1" },
        new("Kimi Raikkonen #7", "Kimi Raikkonen", "FIN", "Ferrari", "formula_v8_g1_b") { Class = "F-V8_Gen1" },
        new("Fernando Alonso #1", "Fernando Alonso", "ESP", "Renault NT", "renault_r26") { Class = "F-V8_Gen1" },
    ];

    static Dictionary<string, int> Hits(params string[] names) => names.ToDictionary(n => n, _ => 1);

    static readonly List<LiveryEntry> Seasons =
    [
        new("Benetton-Ford #5 M. Schumacher", "Michael Schumacher", "DEU", "Benetton-Ford", "formula_hitech_g2m1") { Class = "F-Hitech_Gen2" },
        new("Ferrari #3 M. Schumacher", "Michael Schumacher", "DEU", "Ferrari", "formula_v10_g1") { Class = "F-V10_Gen1" },
    ];

    [Fact]
    public void FindDriver_uses_car_and_class_instead_of_first_season_of_same_driver()
    {
        using var detector = new LiveryDetector(Seasons);
        Assert.Equal("Ferrari", detector.FindDriver("Michael Schumacher", "Formula V10 Gen1 Model1", "F-V10_Gen1")?.Team);
        Assert.Equal("Benetton-Ford", detector.FindDriver("Michael Schumacher", "Formula HiTech Gen2 Model1", "F-Hitech_Gen2")?.Team);
        Assert.Null(detector.FindDriver("Michael Schumacher")); // nenhuma temporada informada: nao chuta
        Assert.Null(detector.FindDriver("Michael Schumacher", "Formula V10 Gen1 Model1", "F-Hitech_Gen2"));
    }

    [Fact]
    public void Cached_livery_cannot_supply_identity_to_another_car_or_class()
    {
        var empty = new HashSet<string>();
        Assert.Equal(Seasons[1], LiveryDetector.ChooseCached(Seasons, Seasons[1].Livery, "F-V10_Gen1", "Formula V10 Gen1 Model1", empty));
        Assert.Null(LiveryDetector.ChooseCached(Seasons, Seasons[0].Livery, "F-V10_Gen1", "Formula V10 Gen1 Model1", empty));
        Assert.Null(LiveryDetector.ChooseCached(Cat, Cat[2].Livery, "F-V8_Gen1", "Formula V8 Gen1 Model1 (B)", empty));
        Assert.Null(LiveryDetector.ChooseCached(Cat, Cat[0].Livery, "F-V8_Gen1", "Formula V8 Gen1 Model1 (M)", empty));
        Assert.Null(LiveryDetector.ChooseCached(Seasons, Seasons[1].Livery, "F-Hitech_Gen2", "Formula V10 Gen1 Model1", empty));
    }

    [Fact]
    public void Choose_rejects_single_hit_from_another_car_and_opponent()
    {
        Assert.Null(LiveryDetector.Choose(Cat, Hits(Cat[2].Livery), "F-V8_Gen1", "Formula V8 Gen1 Model1 (B)", new HashSet<string>(), out _));
        Assert.Null(LiveryDetector.Choose(Cat, Hits(Cat[0].Livery), "F-V8_Gen1", "Formula V8 Gen1 Model1 (B)", new HashSet<string> { Cat[0].Driver }, out _));
    }

    [Fact]
    public void Choose_resolves_duplicate_livery_names_by_car_before_selecting()
    {
        var catalog = new List<LiveryEntry>
        {
            Cat[2] with { Livery = "Shared #1" },
            Cat[0] with { Livery = "Shared #1" },
        };
        Assert.Equal(catalog[1], LiveryDetector.Choose(catalog, Hits("Shared #1"), "F-V8_Gen1", "Formula V8 Gen1 Model1 (B)", new HashSet<string>(), out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Choose_MP48_identifica_Senna_e_preserva_ambiguidade(bool andrettiInField)
    {
        List<LiveryEntry> catalog =
        [
            new("McLaren-Ford #8 A. Senna", "Ayrton Senna", "BRA", "McLaren-Ford", "mclaren_mp48") { Class = "F-Hitech_Gen2" },
            new("McLaren-Ford #7 M. Andretti", "Michael Andretti", "USA", "McLaren-Ford", "mclaren_mp48") { Class = "F-Hitech_Gen2" },
            new("Scuderia Ferrari #27 J. Alesi", "Jean Alesi", "FRA", "Ferrari", "formula_hitech_g2m1") { Class = "F-Hitech_Gen2" },
        ];
        var hits = Hits(catalog.Select(e => e.Livery).ToArray());
        var opponents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (andrettiInField) opponents.Add("Michael Andretti");
        var selected = LiveryDetector.Choose(catalog, hits, "F-Hitech_Gen2", "McLaren Cosworth MP4/8", opponents, out _);
        if (andrettiInField)
            Assert.Equal(catalog[0], selected);
        else
            Assert.Null(selected);
    }

    [Fact]
    public void Choose_unica_candidata_da_pasta_vence_e_traz_pais_BRA()
    {
        var e = LiveryDetector.Choose(Cat, Hits("Felipe Massa #6", "Fernando Alonso #1"), "F-V8_Gen1", "Formula V8 Gen1 Model1 (B)", new HashSet<string>(), out var why);
        Assert.Equal("ok", why);
        Assert.Equal("Felipe Massa #6", e?.Livery);
        Assert.Equal("BRA", e?.Country);
        Assert.Equal("br", CountryCodes.ToAlpha2(e?.Country));
    }

    [Fact]
    public void Choose_varias_na_pasta_sem_IA_nao_chuta()
    {
        var e = LiveryDetector.Choose(Cat, Hits("Felipe Massa #6", "Kimi Raikkonen #7"), "F-V8_Gen1", "Formula V8 Gen1 Model1 (B)", new HashSet<string>(), out var why);
        Assert.Null(e);
        Assert.Equal("varias candidatas", why);
    }

    [Fact]
    public void Choose_descarta_pinturas_de_pilotos_que_sao_carros_da_IA()
    {
        var ai = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "kimi raikkonen" };
        var e = LiveryDetector.Choose(Cat, Hits("Felipe Massa #6", "Kimi Raikkonen #7"), "F-V8_Gen1", "Formula V8 Gen1 Model1 (B)", ai, out _);
        Assert.Equal("Felipe Massa #6", e?.Livery);
    }

    [Fact]
    public void Choose_se_todas_sao_da_IA_continua_sem_chutar()
    {
        var ai = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Felipe Massa", "Kimi Raikkonen" };
        Assert.Null(LiveryDetector.Choose(Cat, Hits("Felipe Massa #6", "Kimi Raikkonen #7"), "F-V8_Gen1", "Formula V8 Gen1 Model1 (B)", ai, out _));
    }
}
