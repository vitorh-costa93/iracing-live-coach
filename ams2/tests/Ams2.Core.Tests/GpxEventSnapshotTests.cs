using Ams2.Core.Calc.GpxSat;

namespace Ams2.Core.Tests;

public class GpxEventSnapshotTests
{
    [Fact]
    public void Value_is_built_once_per_event_and_frozen_until_the_next_one()
    {
        var snap = new GpxEventSnapshot();
        int live = 1, builds = 0;
        int Build() { builds++; return live; }

        Assert.Equal(1, snap.Get("str", null, Build));   // sem evento: ao vivo
        live = 2;
        Assert.Equal(2, snap.Get("str", null, Build));
        Assert.False(snap.Has("str", null));

        Assert.Equal(2, snap.Get("str", 100, Build));    // lider cruzou: retrato
        live = 3;
        Assert.Equal(2, snap.Get("str", 100, Build));    // congelado na janela
        Assert.True(snap.Has("str", 100));
        Assert.Equal(3, snap.Get("stp", 100, Build));    // outra chave, outro retrato
        Assert.Equal(3, snap.Get("str", 190, Build));    // proximo cruzamento: novo retrato
        Assert.Equal(5, builds);

        snap.Reset();
        Assert.False(snap.Has("str", 190));
    }
}
