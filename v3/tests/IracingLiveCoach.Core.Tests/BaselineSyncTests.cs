using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IracingLiveCoach.Core;
using Xunit;

namespace IracingLiveCoach.Core.Tests;

/// <summary>Routes every HttpClient request to a caller-supplied function instead of the network
/// -- the standard way to unit-test HttpClient-based code without a real server.</summary>
internal class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
    public HttpRequestMessage? LastRequest { get; private set; }

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        return Task.FromResult(_responder(request));
    }
}

public class BaselineSyncTests : IDisposable
{
    private readonly string _tempCacheDir = Path.Combine(Path.GetTempPath(), "iracing-live-coach-tests-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_tempCacheDir)) Directory.Delete(_tempCacheDir, recursive: true);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private const string SampleJson = """{"status":"ok","trackLengthMeters":5891,"corners":[]}""";

    [Fact]
    public async Task Fetches_from_the_network_and_caches_the_result_on_success()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, SampleJson));
        var httpClient = new HttpClient(handler);
        var sync = new BaselineSync(httpClient, _tempCacheDir, "https://example.test", importKey: "secret");

        var result = await sync.GetBaselineAsync(carId: 152, trackId: 80);

        Assert.Equal(5891, result.TrackLengthMeters);
        Assert.Equal("https://example.test/api/telemetry/local-coach/baselines?car=152&track=80", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("secret", handler.LastRequest.Headers.GetValues("x-import-key").First());

        var cachedFile = Path.Combine(_tempCacheDir, "152_80.json");
        Assert.True(File.Exists(cachedFile));
    }

    [Fact]
    public async Task Falls_back_to_the_cached_file_when_the_network_call_fails()
    {
        Directory.CreateDirectory(_tempCacheDir);
        await File.WriteAllTextAsync(Path.Combine(_tempCacheDir, "152_80.json"), SampleJson);

        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("network down"));
        var httpClient = new HttpClient(handler);
        var sync = new BaselineSync(httpClient, _tempCacheDir, "https://example.test", importKey: "secret");

        var result = await sync.GetBaselineAsync(carId: 152, trackId: 80);

        Assert.Equal(5891, result.TrackLengthMeters);
    }

    [Fact]
    public async Task Falls_back_to_the_cached_file_when_the_server_returns_an_error_status()
    {
        Directory.CreateDirectory(_tempCacheDir);
        await File.WriteAllTextAsync(Path.Combine(_tempCacheDir, "152_80.json"), SampleJson);

        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.InternalServerError, "{}"));
        var httpClient = new HttpClient(handler);
        var sync = new BaselineSync(httpClient, _tempCacheDir, "https://example.test", importKey: "secret");

        var result = await sync.GetBaselineAsync(carId: 152, trackId: 80);

        Assert.Equal(5891, result.TrackLengthMeters);
    }

    [Fact]
    public async Task Returns_an_empty_baseline_when_both_the_network_and_the_cache_are_unavailable()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("network down"));
        var httpClient = new HttpClient(handler);
        var sync = new BaselineSync(httpClient, _tempCacheDir, "https://example.test", importKey: "secret");

        var result = await sync.GetBaselineAsync(carId: 999, trackId: 999);

        Assert.Equal("ok", result.Status);
        Assert.Empty(result.Corners);
    }
}
