using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace IracingLiveCoach.Core;

/// <summary>Fetches this driver's own historical per-corner baselines from iracing-analytics's
/// GET /api/telemetry/local-coach/baselines, caching each car+track response to a local JSON
/// file. Per this app's own Global Constraint, a sync failure (network error, non-2xx status,
/// malformed JSON) NEVER blocks driving -- it falls back to whatever was last cached for that
/// exact car+track, and if nothing was ever cached either, returns an empty (no-corners)
/// baseline rather than throwing.</summary>
public class BaselineSync
{
    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;
    private readonly string _endpointBaseUrl;
    private readonly string _importKey;

    public BaselineSync(HttpClient httpClient, string cacheDirectory, string endpointBaseUrl, string importKey)
    {
        _httpClient = httpClient;
        _cacheDirectory = cacheDirectory;
        _endpointBaseUrl = endpointBaseUrl.TrimEnd('/');
        _importKey = importKey;
    }

    public async Task<BaselineResponse> GetBaselineAsync(int carId, int trackId)
    {
        var cacheFile = Path.Combine(_cacheDirectory, $"{carId}_{trackId}.json");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_endpointBaseUrl}/api/telemetry/local-coach/baselines?car={carId}&track={trackId}");
            request.Headers.Add("x-import-key", _importKey);

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return await ReadFromCacheOrEmpty(cacheFile);

            var json = await response.Content.ReadAsStringAsync();
            var parsed = JsonSerializer.Deserialize<BaselineResponse>(json, JsonOptions.Baseline);
            if (parsed is null) return await ReadFromCacheOrEmpty(cacheFile);

            Directory.CreateDirectory(_cacheDirectory);
            await File.WriteAllTextAsync(cacheFile, json);
            return parsed;
        }
        catch
        {
            return await ReadFromCacheOrEmpty(cacheFile);
        }
    }

    private static async Task<BaselineResponse> ReadFromCacheOrEmpty(string cacheFile)
    {
        try
        {
            if (File.Exists(cacheFile))
            {
                var cachedJson = await File.ReadAllTextAsync(cacheFile);
                var cached = JsonSerializer.Deserialize<BaselineResponse>(cachedJson, JsonOptions.Baseline);
                if (cached is not null) return cached;
            }
        }
        catch
        {
            // A corrupted cache file is no better than a missing one -- fall through to empty.
        }

        return new BaselineResponse("ok", null, null, new());
    }
}
