using System.Text.Json;
using IracingLiveCoach.Core;
using Xunit;

namespace IracingLiveCoach.Core.Tests;

public class ModelsTests
{
    private const string SampleJson = """
    {
      "status": "ok",
      "trackLengthMeters": 5891,
      "gearModel": { "3": { "a": 42.1, "b": 850 }, "4": { "a": 38.7, "b": 920 } },
      "corners": [
        {
          "number": 1,
          "name": "Copse",
          "startPct": 2.1,
          "endPct": 5.4,
          "brakingPointPct": 3.0,
          "brakingPointStdDev": 0.3,
          "correctionBaselineDeg": 6.2,
          "wheelspinRatePct": 12.5,
          "lapTimeContributionSeconds": 4.8,
          "lapTimeStdDev": 0.15
        },
        {
          "number": 2,
          "name": null,
          "startPct": 10.0,
          "endPct": 15.0,
          "brakingPointPct": null,
          "brakingPointStdDev": null,
          "correctionBaselineDeg": null,
          "wheelspinRatePct": null,
          "lapTimeContributionSeconds": null,
          "lapTimeStdDev": null
        }
      ]
    }
    """;

    [Fact]
    public void Deserializes_the_real_endpoint_shape_including_nulls()
    {
        var result = JsonSerializer.Deserialize<BaselineResponse>(SampleJson, JsonOptions.Baseline);

        Assert.NotNull(result);
        Assert.Equal("ok", result!.Status);
        Assert.Equal(5891, result.TrackLengthMeters);
        Assert.Equal(2, result.Corners.Count);
        Assert.Equal(42.1, result.GearModel!["3"].A);
        Assert.Equal(850, result.GearModel["3"].B);

        var namedCorner = result.Corners[0];
        Assert.Equal("Copse", namedCorner.Name);
        Assert.Equal(3.0, namedCorner.BrakingPointPct);

        var unnamedCorner = result.Corners[1];
        Assert.Null(unnamedCorner.Name);
        Assert.Null(unnamedCorner.BrakingPointPct);
        Assert.Null(unnamedCorner.WheelspinRatePct);
    }

    [Fact]
    public void Deserializes_an_empty_corners_response_without_error()
    {
        const string json = """{"status":"ok","trackLengthMeters":null,"corners":[]}""";

        var result = JsonSerializer.Deserialize<BaselineResponse>(json, JsonOptions.Baseline);

        Assert.NotNull(result);
        Assert.Empty(result!.Corners);
        Assert.Null(result.TrackLengthMeters);
        Assert.Null(result.GearModel);
    }
}
