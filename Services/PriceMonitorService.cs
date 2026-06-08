using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RSIMasterpro.Models;

namespace RSIMasterpro.Services;

// Polls Binance every few seconds, caches latest prices, and closes any OPEN trade
// whose price reached its TP or SL.
public class PriceMonitorService : BackgroundService
{
    private readonly ILogger<PriceMonitorService> _logger;
    private readonly BinanceService _binance;
    private readonly DatabaseService _db;
    private readonly MarketDataService _market;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(3);

    public PriceMonitorService(
        ILogger<PriceMonitorService> logger,
        BinanceService binance,
        DatabaseService db,
        MarketDataService market)
    {
        _logger = logger;
        _binance = binance;
        _db = db;
        _market = market;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Price monitor started (interval={Interval}).", _interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var openTrades = await _db.GetOpenTradesAsync(stoppingToken);
                if (openTrades.Count == 0)
                {
                    await Task.Delay(_interval, stoppingToken);
                    continue;
                }

                var symbols = openTrades.Select(t => t.Symbol).Distinct().ToList();
                var prices = await _binance.GetCurrentPricesAsync(symbols, stoppingToken);

                foreach (var (sym, price) in prices)
                    _market.SetPrice(sym, price);

                foreach (var trade in openTrades)
                {
                    if (!prices.TryGetValue(trade.Symbol, out var price)) continue;

                    var (hit, reason) = CheckTpSl(trade, price);
                    if (!hit) continue;

                    var pnl = CalculatePnl(trade, price);
                    await _db.CloseTradeAsync(trade.Id, price, reason, pnl, stoppingToken);
                    _logger.LogInformation(
                        "Trade #{Id} closed: {Symbol} {Side} @ {Exit} ({Reason}) pnl={Pnl:0.##}",
                        trade.Id, trade.Symbol, trade.Side, price, reason, pnl);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Price monitor cycle error.");
            }

            try { await Task.Delay(_interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private static (bool hit, string reason) CheckTpSl(Trade t, double price)
    {
        var noSl = t.TrackingMode == "NO_SL";
        if (t.Side == "LONG")
        {
            if (price >= t.TakeProfit) return (true, "TP");
            if (!noSl && price <= t.StopLoss) return (true, "SL");
        }
        else // SHORT
        {
            if (price <= t.TakeProfit) return (true, "TP");
            if (!noSl && price >= t.StopLoss) return (true, "SL");
        }
        return (false, "");
    }

    private static double CalculatePnl(Trade t, double exitPrice)
    {
        var diff = t.Side == "LONG"
            ? exitPrice - t.EntryPrice
            : t.EntryPrice - exitPrice;
        return diff * t.Quantity;
    }
}
