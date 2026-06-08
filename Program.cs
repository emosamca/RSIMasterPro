using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using RSIMasterpro;
using RSIMasterpro.Configuration;
using RSIMasterpro.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = "RSI Master Pro Service");
builder.Services.Configure<AppSettings>(builder.Configuration.GetSection("AppSettings"));

builder.Services.AddSingleton<BinanceService>();
builder.Services.AddSingleton<DatabaseService>();
builder.Services.AddSingleton<TelegramService>();
builder.Services.AddSingleton<StrategyStore>();
builder.Services.AddSingleton<StrategyService>();
builder.Services.AddSingleton<MarketDataService>();

string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "service";
bool isCli = mode is "seed" or "candles";

if (!isCli)
{
    builder.Services.AddHostedService<Worker>();
    builder.Services.AddHostedService<PriceMonitorService>();
}

var app = builder.Build();

switch (mode)
{
    case "seed":
    {
        var symbols = args.Skip(1).ToArray();
        if (symbols.Length == 0)
        {
            Console.Error.WriteLine("Usage: dotnet run -- seed SYMBOL1 [SYMBOL2 ...]");
            return;
        }
        var db = app.Services.GetRequiredService<DatabaseService>();
        await db.EnsureSchemaAsync(CancellationToken.None);
        await db.UpsertSymbolsAsync(symbols, CancellationToken.None);
        return;
    }

    case "candles":
    {
        int count = 5;
        var explicitSymbols = new List<string>();
        for (int i = 1; i < args.Length; i++)
        {
            if (int.TryParse(args[i], out var n)) count = n;
            else explicitSymbols.Add(args[i].ToUpperInvariant());
        }

        var db = app.Services.GetRequiredService<DatabaseService>();
        var binance = app.Services.GetRequiredService<BinanceService>();

        List<string> symbols;
        if (explicitSymbols.Count > 0)
            symbols = explicitSymbols;
        else
        {
            await db.EnsureSchemaAsync(CancellationToken.None);
            symbols = (await db.GetActiveCryptosAsync(CancellationToken.None))
                .Select(c => c.Symbol).ToList();
        }

        var inv = CultureInfo.InvariantCulture;
        foreach (var s in symbols)
        {
            var candles = await binance.GetClosed15MinCandlesAsync(s, CancellationToken.None);
            var slice = candles.Skip(Math.Max(0, candles.Count - count)).ToList();
            Console.WriteLine();
            Console.WriteLine($"=== {s}  (showing last {slice.Count} of {candles.Count} closed 15m candles) ===");
            Console.WriteLine($"{"OpenTime (UTC)",-20} {"CloseTime (UTC)",-20} {"Open",14} {"High",14} {"Low",14} {"Close",14} {"Volume",18}");
            foreach (var c in slice)
            {
                Console.WriteLine(string.Format(inv,
                    "{0,-20} {1,-20} {2,14:0.########} {3,14:0.########} {4,14:0.########} {5,14:0.########} {6,18:0.########}",
                    c.OpenTime.ToString("yyyy-MM-dd HH:mm:ss", inv),
                    c.CloseTime.ToString("yyyy-MM-dd HH:mm:ss", inv),
                    c.Open, c.High, c.Low, c.Close, c.Volume));
            }
        }
        return;
    }
}

// Web UI + API
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/symbols", async (DatabaseService db, CancellationToken ct) =>
    Results.Ok(await db.GetAllCryptosAsync(ct)));

app.MapPost("/api/symbols", async (DatabaseService db, AddSymbolRequest req, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Symbol))
        return Results.BadRequest("Sembol gerekli.");
    var sym = req.Symbol.Trim().ToUpperInvariant();
    await db.UpsertSymbolsAsync(new[] { sym }, ct);
    return Results.Created($"/api/symbols/{sym}", new { symbol = sym });
});

app.MapDelete("/api/symbols/{symbol}", async (DatabaseService db, string symbol, CancellationToken ct) =>
{
    var deleted = await db.DeleteSymbolAsync(symbol, ct);
    return deleted > 0 ? Results.NoContent() : Results.NotFound();
});

app.MapDelete("/api/trades/{mode}", async (string mode, DatabaseService db, CancellationToken ct) =>
{
    var m = mode.ToUpperInvariant();
    if (m != "STANDARD" && m != "NO_SL")
        return Results.BadRequest(new { error = "mode must be STANDARD or NO_SL" });

    var deleted = await db.DeleteTradesByModeAsync(m, ct);
    return Results.Ok(new { mode = m, deleted });
});

app.MapGet("/api/strategy", (StrategyStore store) => Results.Ok(store.Current));

app.MapPost("/api/strategy", (StrategyStore store, StrategySettings req) =>
{
    var errors = new List<string>();
    if (req.FastRsiLength < 1) errors.Add("FastRsiLength >= 1 olmalı.");
    if (req.SlowRsiLength < 1) errors.Add("SlowRsiLength >= 1 olmalı.");
    if (req.EmaLength < 1) errors.Add("EmaLength >= 1 olmalı.");
    if (req.MinAdx < 0 || req.MinAdx > 100) errors.Add("MinAdx 0-100 aralığında olmalı.");
    if (req.TakeProfitAtrMult <= 0) errors.Add("TakeProfitAtrMult > 0 olmalı.");
    if (req.StopLossAtrMult <= 0) errors.Add("StopLossAtrMult > 0 olmalı.");
    if (req.RiskPerTrade <= 0) errors.Add("RiskPerTrade > 0 olmalı.");
    if (req.AccountEquity <= 0) errors.Add("AccountEquity > 0 olmalı.");

    if (errors.Count > 0)
        return Results.BadRequest(new { errors });

    store.Update(req);
    return Results.Ok(store.Current);
});

app.MapGet("/api/dashboard", async (DatabaseService db, MarketDataService market, CancellationToken ct) =>
{
    var statsMap = await db.GetStatsAsync(ct);
    var open = await db.GetOpenTradesAsync(ct);
    var closed = await db.GetLastClosedTradesAsync(20, ct);

    var openWithPrice = open.Select(t =>
    {
        var cp = market.GetPrice(t.Symbol);
        double? upnl = null;
        if (cp.HasValue)
        {
            var diff = t.Side == "LONG" ? cp.Value - t.EntryPrice : t.EntryPrice - cp.Value;
            upnl = diff * t.Quantity;
        }
        return new
        {
            id = t.Id,
            symbol = t.Symbol,
            side = t.Side,
            trackingMode = t.TrackingMode,
            entryPrice = t.EntryPrice,
            quantity = t.Quantity,
            stopLoss = t.StopLoss,
            takeProfit = t.TakeProfit,
            entryTime = t.EntryTime,
            currentPrice = cp,
            unrealizedPnl = upnl
        };
    });

    return Results.Ok(new
    {
        stats = new
        {
            standard = statsMap.GetValueOrDefault("STANDARD") ?? new(),
            noSl = statsMap.GetValueOrDefault("NO_SL") ?? new()
        },
        openTrades = openWithPrice,
        closedTrades = closed
    });
});

await app.RunAsync();

public record AddSymbolRequest(string Symbol);
