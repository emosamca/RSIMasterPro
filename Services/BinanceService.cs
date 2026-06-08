using Binance.Net.Clients;
using Binance.Net.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RSIMasterpro.Configuration;
using RSIMasterpro.Models;

namespace RSIMasterpro.Services;

public class BinanceService : IDisposable
{
    private readonly ILogger<BinanceService> _logger;
    private readonly BinanceSettings _settings;
    private readonly BinanceRestClient _client;

    public BinanceService(ILogger<BinanceService> logger, IOptions<AppSettings> opts)
    {
        _logger = logger;
        _settings = opts.Value.Binance;
        _client = new BinanceRestClient();
    }

    public async Task<List<Candle>> GetClosed15MinCandlesAsync(string symbol, CancellationToken ct)
    {
        var result = await _client.UsdFuturesApi.ExchangeData.GetKlinesAsync(
            symbol,
            KlineInterval.FifteenMinutes,
            limit: _settings.CandleLimit,
            ct: ct);

        if (!result.Success)
        {
            _logger.LogError("Binance kline fetch failed for {Symbol}: {Error}", symbol, result.Error);
            return new List<Candle>();
        }

        var nowUtc = DateTime.UtcNow;

        return result.Data
            .Where(k => k.CloseTime < nowUtc)
            .OrderBy(k => k.OpenTime)
            .Select(k => new Candle
            {
                OpenTime = k.OpenTime,
                CloseTime = k.CloseTime,
                Open = (double)k.OpenPrice,
                High = (double)k.HighPrice,
                Low = (double)k.LowPrice,
                Close = (double)k.ClosePrice,
                Volume = (double)k.Volume
            })
            .ToList();
    }

    public async Task<double?> GetCurrentPriceAsync(string symbol, CancellationToken ct)
    {
        var result = await _client.UsdFuturesApi.ExchangeData.GetPriceAsync(symbol, ct: ct);

        if (!result.Success)
        {
            _logger.LogError("Binance price fetch failed for {Symbol}: {Error}", symbol, result.Error);
            return null;
        }

        return (double)result.Data.Price;
    }

    public async Task<Dictionary<string, double>> GetCurrentPricesAsync(IEnumerable<string> symbols, CancellationToken ct)
    {
        var result = await _client.UsdFuturesApi.ExchangeData.GetPricesAsync(ct: ct);

        if (!result.Success)
        {
            _logger.LogError("Binance bulk price fetch failed: {Error}", result.Error);
            return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        }

        var wanted = new HashSet<string>(symbols, StringComparer.OrdinalIgnoreCase);
        var dict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in result.Data)
        {
            if (wanted.Contains(p.Symbol))
                dict[p.Symbol] = (double)p.Price;
        }
        return dict;
    }

    public void Dispose() => _client.Dispose();
}
