using Ams2.Core.Calc;
namespace Ams2.Core.Tests;

public class Broadcast04PulseTests
{
    [Fact]
    public void Visible_refresh_extends_lifetime_without_replaying_entrance()
    {
        var motion = new Broadcast04Pulse();
        Assert.Equal(0, motion.Evaluate(10, 10, 16));
        Assert.InRange(motion.Evaluate(10.08, 10, 16), .49f, .51f);
        Assert.Equal(1, motion.Evaluate(11, 10, 16));
        Assert.Equal(1, motion.Evaluate(12, 12, 18));
        Assert.InRange(motion.Evaluate(17.92, 12, 18), .49f, .51f);
        Assert.Equal(0, motion.Evaluate(18, 12, 18));
        Assert.Equal(0, motion.Evaluate(20, 20, 26));
    }

    [Fact]
    public void Rewind_disconnect_and_invalid_windows_clear_previous_transition()
    {
        var motion = new Broadcast04Pulse();
        Assert.Equal(1, motion.Evaluate(12, 10, 16));
        Assert.Equal(0, motion.Evaluate(1, 1, 7));
        Assert.Equal(0, motion.Evaluate(double.NaN, 0, 6));
        Assert.Equal(0, motion.Evaluate(2, double.NegativeInfinity, 6));
        Assert.Equal(0, motion.Evaluate(2, 2, 2));
        Assert.Equal(1, motion.Evaluate(3, 2, double.PositiveInfinity));
        motion.Reset();
        Assert.Equal(0, motion.Evaluate(4, 4, 10));
    }
}
