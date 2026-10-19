using IracingLiveCoach.Core.Coach;
using Xunit;

namespace IracingLiveCoach.Core.Tests.Coach;

public class CoachContextMapperTests
{
    [Fact]
    public void Maps_track_car_length_and_session()
    {
        var c = CoachContextMapper.FromSession(123, "spa up", "7.00 km", "mclaren720s", "McLaren 720S", "Race");
        Assert.Equal(new CoachContext("spa up", "mclaren720s", 7000, "Race"), c);
    }

    [Fact]
    public void Falls_back_to_trackid_and_screen_name_and_miles()
    {
        var c = CoachContextMapper.FromSession(55, null, "1.00 mi", "", "Car X", null);
        Assert.NotNull(c);
        Assert.Equal("track55", c!.TrackId);
        Assert.Equal("Car X", c.CarId);
        Assert.Equal(1609.344, c.TrackLengthM, 3);
        Assert.Equal("", c.SessionType);
    }

    [Fact]
    public void Null_when_track_or_car_unknown()
    {
        Assert.Null(CoachContextMapper.FromSession(0, "", "3 km", "car", null, "Race"));
        Assert.Null(CoachContextMapper.FromSession(1, "t", "3 km", null, null, "Race"));
    }

    [Fact]
    public void Unparseable_length_is_zero()
    {
        Assert.Equal(0, CoachContextMapper.FromSession(1, "t", "abc", "c", null, null)!.TrackLengthM);
    }
}
