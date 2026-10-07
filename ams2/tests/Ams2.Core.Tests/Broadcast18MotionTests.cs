using Ams2.Core.Calc;

namespace Ams2.Core.Tests;

public class Broadcast18MotionTests
{
    [Fact]
    public void Overlapping_event_refresh_does_not_restart_a_visible_plate()
    {
        var motion = new Broadcast18Motion();
        Assert.Equal(0, motion.Evaluate(10, 10, 16));
        Assert.InRange(motion.Evaluate(10.12, 10, 16), .87f, .88f);
        Assert.Equal(1, motion.Evaluate(11, 10, 16));
        Assert.Equal(1, motion.Evaluate(12, 12, 18));
        Assert.Equal(0, motion.Evaluate(20, 20, 26));
    }

    [Fact]
    public void Presence_enters_once_and_exit_holds_without_replaying_entrance()
    {
        var motion = new Broadcast18Motion();
        Assert.Equal(0, motion.Presence(0, false));
        Assert.Equal(0, motion.Presence(1, true));
        Assert.Equal(1, motion.Presence(2, true));
        Assert.Equal(1, motion.Presence(3, false, 4));
        Assert.Equal(1, motion.Presence(6, false, 4));
        Assert.InRange(motion.Presence(7.1, false, 4), .87f, .88f);
        Assert.Equal(0, motion.Presence(7.21, false, 4));
        Assert.Equal(0, motion.Presence(8, true));
    }

    [Fact]
    public void Running_lap_to_result_keeps_container_visible()
    {
        var motion = new Broadcast18Motion();
        Assert.Equal(1, motion.Evaluate(60, 0, double.PositiveInfinity));
        Assert.Equal(1, motion.Evaluate(61, 61, 70));
        Assert.Equal(1, motion.Evaluate(62, 61, 70));
        Assert.InRange(motion.Evaluate(69.9, 61, 70), .87f, .88f);
        Assert.Equal(0, motion.Evaluate(70, 61, 70));
    }

    [Fact]
    public void Session_or_clock_reset_discards_previous_event_window()
    {
        var motion = new Broadcast18Motion();
        motion.Evaluate(12, 10, 16, 1);
        Assert.Equal(0, motion.Evaluate(12, 12, 18, 2));
        Assert.Equal(0, motion.Evaluate(5, 5, 10, 2));
        Assert.Equal(0, motion.Evaluate(double.NaN, 0, 10));
        Assert.Equal(0, motion.Evaluate(11, double.NaN, 10));
    }
}
