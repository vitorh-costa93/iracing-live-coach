using Ams2.OverlayHost.Data;
using Ams2.Shared.PlayerNames;
using Xunit;

namespace Ams2.Integration.Tests;

public sealed class PlayerIdentityProviderTests
{
    [Fact]
    public void Provider_applies_chosen_livery_identity_to_player_car()
    {
        double now = 0;
        var names = PlayerNameStore.InMemory();
        names.SetIdentity(FakeRawSource.PlayerCarName, "1999 Stewart #16 - R. Barrichello", "Rubens Barrichello", "BRA", "Stewart");
        using var provider = new OverlayDataProvider(new FakeRawSource(() => now), () => now, names: names);
        var model = provider.Tick();
        var me = model.Session!.PlayerCar!;
        Assert.Equal("Rubens Barrichello", me.Name);
        Assert.Equal("Stewart", me.TeamName);
        Assert.Equal("BRA", me.Country);
    }
}

public sealed class PlayerIdentityProviderDetectorTests
{
    [Fact]
    public void Manual_choice_survives_when_detector_has_nothing_for_the_car()
    {
        double now = 0;
        var names = PlayerNameStore.InMemory();
        names.SetIdentity(FakeRawSource.PlayerCarName, "1999 Stewart #16 - R. Barrichello", "Rubens Barrichello", "BRA", "Stewart");
        using var det = new Ams2.Shared.Liveries.LiveryDetector([new Ams2.Shared.Liveries.LiveryEntry("X", "Other", "", "", "formula_classic_g2") { Class = "F-Classic_Gen2" }]);
        using var provider = new OverlayDataProvider(new FakeRawSource(() => now), () => now, names: names, liveries: det);
        var me = provider.Tick().Session!.PlayerCar!;
        Assert.Equal("Rubens Barrichello", me.Name);
        Assert.Equal("Stewart", me.TeamName);
    }
}

public sealed class IdentityPriorityTests
{
    static PlayerNameEntry E(string name) => new() { Model = "m", Name = name };

    [Fact] public void Manual_choice_wins_over_detection() => Assert.Equal("Rubens Barrichello", OverlayDataProvider.ChooseIdentity(E("Rubens Barrichello"), E("Felipe Massa"))!.Name);
    [Fact] public void Detection_fills_when_no_manual_choice() => Assert.Equal("Felipe Massa", OverlayDataProvider.ChooseIdentity(E(""), E("Felipe Massa"))!.Name);
    [Fact] public void Null_when_nothing() => Assert.Null(OverlayDataProvider.ChooseIdentity(null, null));
}
