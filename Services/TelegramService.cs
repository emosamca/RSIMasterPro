using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RSIMasterpro.Configuration;
using RSIMasterpro.Models;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace RSIMasterpro.Services;

public class TelegramService
{
    private readonly ILogger<TelegramService> _logger;
    private readonly TelegramBotClient _bot;
    private readonly ChatId[] _chatIds;
    private readonly BinanceService _binance;

    public TelegramService(ILogger<TelegramService> logger, IOptions<AppSettings> opts, BinanceService binance)
    {
        _logger = logger;
        _binance = binance;
        var settings = opts.Value.Telegram;
        _bot = new TelegramBotClient(settings.BotToken);
        _chatIds = settings.ChatIds
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => long.TryParse(s, out var num) ? new ChatId(num) : new ChatId(s))
            .ToArray();

        if (_chatIds.Length == 0)
            _logger.LogWarning("No Telegram ChatIds configured — messages will be skipped.");
    }

    public async Task SendTextAsync(string text, CancellationToken ct)
    {
        foreach (var chatId in _chatIds)
        {
            try
            {
                await _bot.SendTextMessageAsync(chatId, text, cancellationToken: ct);
                _logger.LogInformation("Telegram message sent to {ChatId}: {Msg}", chatId, text);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send Telegram message to {ChatId}.", chatId);
            }
        }
    }

    public async Task SendSignalAsync(TradeSignal signal, CancellationToken ct)
    {
        var isLong = signal.Type == SignalType.Long;
        var icon = isLong ? "🟢" : "🔴";
        var side = isLong ? "Long" : "Short";

        var livePrice = await _binance.GetCurrentPriceAsync(signal.Symbol, ct);
        var priceText = livePrice.HasValue ? FormatPrice(livePrice.Value) : FormatPrice(signal.EntryPrice);

        var msg = $"{icon} {side}-{signal.Symbol} " +
                  $"güncel fiyat: {priceText} " +
                  $"alış adedi: {FormatQty(signal.Quantity)} " +
                  $"stop fiyatı: {FormatPrice(signal.StopLoss)} " +
                  $"kar al: {FormatPrice(signal.TakeProfit)}";

        await SendTextAsync(msg, ct);
    }

    private static string FormatPrice(double v) =>
        v.ToString("0.########", CultureInfo.InvariantCulture);

    private static string FormatQty(double v) =>
        v.ToString("0.####", CultureInfo.InvariantCulture);
}
