using System.Collections.Concurrent;

namespace RSIMasterpro.Services;

// In-memory cache of latest Binance prices, populated by PriceMonitorService.
public class MarketDataService
{
    private readonly ConcurrentDictionary<string, (double Price, DateTime UpdatedAtUtc)> _prices = new();

    public void SetPrice(string symbol, double price)
        => _prices[symbol] = (price, DateTime.UtcNow);

    public double? GetPrice(string symbol)
        => _prices.TryGetValue(symbol, out var v) ? v.Price : null;
}
