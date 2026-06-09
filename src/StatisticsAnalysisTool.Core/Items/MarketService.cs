using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace StatisticsAnalysisTool.Core.Items;

/// <summary>
/// Fetches current market prices from the community Albion Online Data Project API
/// (https://www.albion-online-data.com/). Regional servers: "west" (Americas), "east" (Asia),
/// "europe". Done server-side so the browser isn't subject to cross-origin restrictions.
/// </summary>
public sealed class MarketService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private const int MaxItemsPerRequest = 50;

    public async Task<IReadOnlyList<MarketPrice>> GetPricesAsync(
        IEnumerable<string> uniqueNames,
        string server = "west",
        CancellationToken cancellationToken = default)
    {
        var names = uniqueNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxItemsPerRequest)
            .ToList();

        if (names.Count == 0)
        {
            return Array.Empty<MarketPrice>();
        }

        var host = server.ToLowerInvariant() switch
        {
            "east" => "east.albion-online-data.com",
            "europe" => "europe.albion-online-data.com",
            _ => "west.albion-online-data.com",
        };

        var url = $"https://{host}/api/v2/stats/prices/{string.Join(",", names)}.json";

        await using var stream = await Http.GetStreamAsync(url, cancellationToken).ConfigureAwait(false);
        var prices = await JsonSerializer
            .DeserializeAsync<List<MarketPrice>>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        return prices ?? (IReadOnlyList<MarketPrice>) Array.Empty<MarketPrice>();
    }
}

// Property names map to the AO Data API's snake_case via the SnakeCaseLower policy on read; the
// web layer re-serializes these to camelCase for the browser.
public sealed record MarketPrice
{
    public string ItemId { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public int Quality { get; init; }
    public long SellPriceMin { get; init; }
    public long SellPriceMax { get; init; }
    public long BuyPriceMin { get; init; }
    public long BuyPriceMax { get; init; }
}
