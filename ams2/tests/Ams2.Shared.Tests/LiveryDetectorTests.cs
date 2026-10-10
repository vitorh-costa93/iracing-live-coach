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
}
