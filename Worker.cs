using RSIMasterpro.Services;

namespace RSIMasterpro;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly StrategyService _strategy;
    private readonly DatabaseService _database;
    private readonly TelegramService _telegram;

    public Worker(
        ILogger<Worker> logger,
        StrategyService strategy,
        DatabaseService database,
        TelegramService telegram)
    {
        _logger = logger;
        _strategy = strategy;
        _database = database;
        _telegram = telegram;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RSI Master Pro service started.");

        // Block service start until schema is in place; retry until success.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _database.EnsureSchemaAsync(stoppingToken);
                break;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Schema setup failed, retrying in 30s.");
                try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var next = GetNextRunTime();
                var delay = next - DateTime.UtcNow;
                _logger.LogInformation("Next run scheduled at {Next:O} UTC (in {Delay}).", next, delay);

                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, stoppingToken);

                await _strategy.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during scheduled strategy run.");
                try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        _logger.LogInformation("RSI Master Pro service stopping.");
    }

    // Next run = boundary of next 15-min candle close + 30s buffer for Binance to finalize.
    private static DateTime GetNextRunTime()
    {
        var now = DateTime.UtcNow;
        int minute = now.Minute;
        int minutesToNext = 15 - (minute % 15);
        var boundary = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc)
            .AddMinutes(minutesToNext);
        return boundary.AddSeconds(30);
    }
}
