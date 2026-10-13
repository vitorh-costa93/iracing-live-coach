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
    public void FolderMatchesCar_casa_pasta_com_nome_do_carro(string folder, string car, bool expected)
        => Assert.Equal(expected, LiveryDetector.FolderMatchesCar(folder, car));

    static readonly List<LiveryEntry> Cat =
    [
        new("Felipe Massa #6", "Felipe Massa", "BRA", "Ferrari", "formula_v8_g1_b") { Class = "F-V8_Gen1" },
        new("Kimi Raikkonen #7", "Kimi Raikkonen", "FIN", "Ferrari", "formula_v8_g1_b") { Class = "F-V8_Gen1" },
        new("Fernando Alonso #1", "Fernando Alonso", "ESP", "Renault NT", "renault_r26") { Class = "F-V8_Gen1" },
    ];

    static Dictionary<string, int> Hits(params string[] names) => names.ToDictionary(n => n, _ => 1);

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
