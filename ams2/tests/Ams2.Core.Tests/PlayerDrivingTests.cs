using Ams2.Core.Calc;
using Ams2.Core.Reading;

namespace Ams2.Core.Tests;

public class PlayerDrivingTests
{
    static FakeMemory Mem(uint state = 2, int viewed = 0, uint pit = 0)
    {
        var m = new FakeMemory();
        m.Raw.GameState = state; m.Raw.ViewedParticipantIndex = viewed;
        m.SetCar(0, "Player", "Car A", "GT3", 1, 100);
        m.SetCar(1, "Rival", "Car B", "GT3", 2, 50);
        m.Raw.PitModes[0] = pit;
        return m;
    }

    static SessionSnapshot Snap(FakeMemory m) => SnapshotMapper.Map(in m.Raw);

    [Theory]
    [InlineData(1u, false)]
    [InlineData(2u, true)]
    [InlineData(3u, false)]
    [InlineData(4u, false)]
    [InlineData(5u, false)]
    [InlineData(6u, false)]
    public void Only_game_state_2_counts(uint state, bool expected) =>
        Assert.Equal(expected, new PlayerDrivingTracker().Evaluate(Snap(Mem(state))));

    [Fact]
    public void Car_parked_in_garage_hides_but_driving_out_of_garage_shows()
    {
        Assert.False(new PlayerDrivingTracker().Evaluate(Snap(Mem(pit: 4))));
        Assert.True(new PlayerDrivingTracker().Evaluate(Snap(Mem(pit: 5))));
        Assert.True(new PlayerDrivingTracker().Evaluate(Snap(Mem(pit: 2)))); // parado no box durante a corrida continua visivel
    }

    [Fact]
    public void Camera_on_another_car_hides_until_back_on_own_car()
    {
        var t = new PlayerDrivingTracker();
        var m = Mem();
        Assert.True(t.Evaluate(Snap(m)));          // indice proprio = 0
        m.Raw.ViewedParticipantIndex = 1;
        Assert.False(t.Evaluate(Snap(m)));
        m.Raw.ViewedParticipantIndex = 0;
        Assert.True(t.Evaluate(Snap(m)));
    }

    [Fact]
    public void Own_index_is_forgotten_after_menu_or_loading()
    {
        var t = new PlayerDrivingTracker();
        var m = Mem();
        Assert.True(t.Evaluate(Snap(m)));
        m.Raw.GameState = 3;
        Assert.False(t.Evaluate(Snap(m)));
        m.Raw.GameState = 2; m.Raw.ViewedParticipantIndex = 1; // nova sessao: outro slot
        Assert.True(t.Evaluate(Snap(m)));
    }

    [Fact]
    public void Shows_immediately_and_hides_after_debounce()
    {
        var t = new PlayerDrivingTracker();
        var m = Mem();
        Assert.True(t.Update(0, Snap(m)));
        m.Raw.GameState = 4;
        Assert.True(t.Update(1.00, Snap(m)));   // comeca a contar
        Assert.True(t.Update(1.10, Snap(m)));
        Assert.False(t.Update(1.16, Snap(m)));
        m.Raw.GameState = 2;
        Assert.True(t.Update(1.17, Snap(m)));   // volta na hora
    }

    [Fact]
    public void Short_glitch_below_debounce_does_not_hide()
    {
        var t = new PlayerDrivingTracker();
        var m = Mem();
        t.Update(0, Snap(m));
        m.Raw.GameState = 4; Assert.True(t.Update(1.0, Snap(m)));
        m.Raw.GameState = 2; Assert.True(t.Update(1.05, Snap(m)));
        m.Raw.GameState = 4; Assert.True(t.Update(1.10, Snap(m)));  // contagem reinicia
        Assert.True(t.Update(1.20, Snap(m)));
    }

    [Fact]
    public void Pipeline_reader_to_tracker_keeps_player_state_but_hides_on_pause()
    {
        var m = Mem();
        using var reader = new SharedMemoryReader(m);
        var t = new PlayerDrivingTracker();
        Assert.True(t.Update(0, reader.Poll().Snapshot!));
        m.Raw.GameState = 4;
        var s = reader.Poll().Snapshot!;
        Assert.Equal(2, s.Cars.Count);                  // dados continuam chegando aos trackers (historico nao zera)
        t.Update(0.5, s);
        Assert.False(t.Update(0.7, s));
        m.Raw.GameState = 2;
        Assert.True(t.Update(0.8, reader.Poll().Snapshot!));
    }
}
