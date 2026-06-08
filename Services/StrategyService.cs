using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RSIMasterpro.Configuration;
using RSIMasterpro.Indicators;
using RSIMasterpro.Models;

namespace RSIMasterpro.Services;

// Implements the RSI Master Pro pine strategy:
//   longCondition  = crossover(div, 0)  and close > ema200 and adx > min and volForce and strongLong
//   shortCondition = crossunder(div, 0) and close < ema200 and adx > min and volForce and strongShort
public class StrategyService
{
    private readonly ILogger<StrategyService> _logger;
    private readonly StrategyStore _store;
    private readonly DatabaseService _db;
    private readonly BinanceService _binance;
    private readonly TelegramService _telegram;

    private readonly Dictionary<string, DateTime> _lastSignaledCandle = new();

    // Read live so parameters edited from the dashboard apply on the next run.
    private StrategySettings _strategy => _store.Current;

    public StrategyService(
        ILogger<StrategyService> logger,
        StrategyStore store,
        DatabaseService db,
        BinanceService binance,
        TelegramService telegram)
    {
        _logger = logger;
        _store = store;
        _db = db;
        _binance = binance;
        _telegram = telegram;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _logger.LogInformation("Strategy run started at {Time:O} UTC.", DateTime.UtcNow);

        List<CryptoSymbol> symbols;
        try
        {
            symbols = await _db.GetActiveCryptosAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load symbols from database.");
            return;
        }

        int signalCount = 0;
        foreach (var s in symbols)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var result = await AnalyzeAsync(s.Symbol, ct);
                if (result.HasSignal) signalCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing {Symbol}", s.Symbol);
            }
        }

        _logger.LogInformation("Strategy run finished. {Count} signals.", signalCount);
    }

    private sealed record AnalysisResult(bool HasSignal, Candle? LastCandle);

    private async Task<AnalysisResult> AnalyzeAsync(string symbol, CancellationToken ct)
    {
        var candles = await _binance.GetClosed15MinCandlesAsync(symbol, ct);
        var lastCandle = candles.Count > 0 ? candles[^1] : null;

        int minNeeded = _strategy.EmaLength + 10;
        if (candles.Count < minNeeded)
        {
            _logger.LogWarning("Not enough candles for {Symbol} (got {Count}, need {Need}).",
                symbol, candles.Count, minNeeded);
            return new AnalysisResult(false, lastCandle);
        }

        var close = candles.Select(c => c.Close).ToArray();
        var high = candles.Select(c => c.High).ToArray();
        var low = candles.Select(c => c.Low).ToArray();
        var vol = candles.Select(c => c.Volume).ToArray();

        var emaCalc = new EmaIndicator(_strategy.EmaLength);
        var atrCalc = new AtrIndicator(14);
        var rsiMainCalc = new RsiIndicator(14);
        var divCalc = new DivergenceIndicator(_strategy.FastRsiLength, _strategy.SlowRsiLength);
        var dmiCalc = new DmiIndicator(14, 14);
        var volSmaCalc = new SmaIndicator(20);

        var ema = emaCalc.Calculate(close);
        var atr = atrCalc.Calculate(high, low, close);
        var rsiMain = rsiMainCalc.Calculate(close);
        var (div, divAccel) = divCalc.Calculate(close);
        var (_, _, adx) = dmiCalc.Calculate(high, low, close);
        var volSma = volSmaCalc.Calculate(vol);

        int i = candles.Count - 1;
        int p = i - 1;

        if (p < 0 ||
            double.IsNaN(ema[i]) || double.IsNaN(atr[i]) || double.IsNaN(rsiMain[i]) ||
            double.IsNaN(div[i]) || double.IsNaN(div[p]) || double.IsNaN(divAccel[i]) ||
            double.IsNaN(adx[i]) || double.IsNaN(volSma[i]))
        {
            _logger.LogDebug("Indicators not warm yet for {Symbol}.", symbol);
            return new AnalysisResult(false, lastCandle);
        }

        var candleClose = candles[i].CloseTime;
        if (_lastSignaledCandle.TryGetValue(symbol, out var last) && last >= candleClose)
        {
            _logger.LogDebug("Already evaluated {Symbol} candle {Time:O}.", symbol, candleClose);
            return new AnalysisResult(false, lastCandle);
        }

        bool crossover = div[i] > 0 && div[p] <= 0;
        bool crossunder = div[i] < 0 && div[p] >= 0;
        bool isTrending = adx[i] > _strategy.MinAdx;
        bool volForce = vol[i] > volSma[i] * 1.2;
        bool strongLong = rsiMain[i] > 55 && divAccel[i] > 0.1;
        bool strongShort = rsiMain[i] < 45 && divAccel[i] < -0.1;

        double src = close[i];
        bool longCond = crossover && src > ema[i] && isTrending && volForce && strongLong;
        bool shortCond = crossunder && src < ema[i] && isTrending && volForce && strongShort;

        _lastSignaledCandle[symbol] = candleClose;

        if (!longCond && !shortCond)
        {
            _logger.LogDebug("{Symbol} {Time:O}: no signal (close={Close} ema={Ema} adx={Adx} div={Div}).",
                symbol, candleClose, src, ema[i], adx[i], div[i]);
            return new AnalysisResult(false, lastCandle);
        }

        double riskAmount = _strategy.AccountEquity * (_strategy.RiskPerTrade / 100.0);
        double stopDist = atr[i] * _strategy.StopLossAtrMult;
        double qty = stopDist > 0 ? riskAmount / stopDist : 0;

        var signal = new TradeSignal
        {
            Symbol = symbol,
            EntryPrice = src,
            Quantity = qty,
            CandleCloseTime = candleClose,
            Type = longCond ? SignalType.Long : SignalType.Short,
            StopLoss = longCond ? src - stopDist : src + stopDist,
            TakeProfit = longCond
                ? src + atr[i] * _strategy.TakeProfitAtrMult
                : src - atr[i] * _strategy.TakeProfitAtrMult
        };

        await _telegram.SendSignalAsync(signal, ct);

        try
        {
            var sideStr = signal.Type == SignalType.Long ? "LONG" : "SHORT";

            await _db.InsertTradeAsync(new Trade
            {
                Symbol = signal.Symbol,
                Side = sideStr,
                EntryPrice = signal.EntryPrice,
                Quantity = signal.Quantity,
                StopLoss = signal.StopLoss,
                TakeProfit = signal.TakeProfit,
                TrackingMode = "STANDARD"
            }, ct);

            await _db.InsertTradeAsync(new Trade
            {
                Symbol = signal.Symbol,
                Side = sideStr,
                EntryPrice = signal.EntryPrice,
                Quantity = signal.Quantity,
                StopLoss = signal.StopLoss,
                TakeProfit = signal.TakeProfit,
                TrackingMode = "NO_SL"
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist virtual trade for {Symbol}.", signal.Symbol);
        }

        return new AnalysisResult(true, lastCandle);
    }
}
