using Ams2.Core.Calc;

namespace Ams2.Core.Tests;

public class Broadcast18RowMotionTests
{
    [Fact]
    public void First_frame_snaps_and_swap_follows_car_identity_without_restarting()
    {
        var motion = new Broadcast18RowMotion();
        var initial = new Dictionary<int, float> { [1] = 0, [2] = 40 };
        var swapped = new Dictionary<int, float> { [2] = 0, [1] = 40 };
        Assert.Equal(0, motion.Evaluate(0, "session", initial)[1]);
        Assert.Equal(0, motion.Evaluate(1, "session", swapped)[1]);
        var halfway = motion.Evaluate(1 + Broadcast18RowMotion.Duration / 2, "session", swapped);
        Assert.InRange(halfway[1], 19.99f, 20.01f);
        Assert.InRange(halfway[2], 19.99f, 20.01f);
        Assert.Equal(40, motion.Evaluate(2, "session", swapped)[1]);
    }

    [Fact]
    public void New_target_starts_from_current_visual_position()
    {
        var motion = new Broadcast18RowMotion();
        motion.Evaluate(0, "session", new Dictionary<int, float> { [1] = 0 });
        motion.Evaluate(1, "session", new Dictionary<int, float> { [1] = 40 });
        var retargeted = motion.Evaluate(1.14, "session", new Dictionary<int, float> { [1] = 80 });
        Assert.InRange(retargeted[1], 19.99f, 20.01f);
        Assert.InRange(motion.Evaluate(1.28, "session", new Dictionary<int, float> { [1] = 80 })[1], 49.99f, 50.01f);
    }

    [Fact]
    public void Rewind_session_disconnect_and_removed_rows_snap_to_new_target()
    {
        var motion = new Broadcast18RowMotion();
        var targets = new Dictionary<int, float> { [1] = 0 };
        motion.Evaluate(10, "session", targets);
        targets[1] = 40;
        Assert.Equal(40, motion.Evaluate(1, "session", targets)[1]);
        targets[1] = 80;
        Assert.Equal(80, motion.Evaluate(1.1, "other-session", targets)[1]);
        motion.Reset(); targets[1] = 120;
        Assert.Equal(120, motion.Evaluate(1.2, "other-session", targets)[1]);
        Assert.Empty(motion.Evaluate(1.3, "other-session", new Dictionary<int, float>()));
        targets[1] = 160;
        Assert.Equal(160, motion.Evaluate(1.4, "other-session", targets)[1]);
        Assert.Equal(160, motion.Evaluate(double.NaN, "other-session", targets)[1]);
        targets[1] = 200;
        Assert.Equal(200, motion.Evaluate(1.5, "other-session", targets)[1]);
    }
}
