namespace RSIMasterpro.Configuration;

public class AppSettings
{
    public DatabaseSettings Database { get; set; } = new();
    public TelegramSettings Telegram { get; set; } = new();
    public BinanceSettings Binance { get; set; } = new();
    public StrategySettings Strategy { get; set; } = new();
}

public class DatabaseSettings
{
    public string ConnectionString { get; set; } = "";
    public string CryptosTable { get; set; } = "cryptos";
    public string SymbolColumn { get; set; } = "symbol";
    public string ActiveColumn { get; set; } = "active";
}

public class TelegramSettings
{
    public string BotToken { get; set; } = "";
    public List<string> ChatIds { get; set; } = new();
}

public class BinanceSettings
{
    public int CandleLimit { get; set; } = 300;
}

public class StrategySettings
{
    public int FastRsiLength { get; set; } = 9;
    public int SlowRsiLength { get; set; } = 18;
    public int EmaLength { get; set; } = 80;
    public int MinAdx { get; set; } = 20;
    public double TakeProfitAtrMult { get; set; } = 6.0;
    public double StopLossAtrMult { get; set; } = 0.8;
    public double RiskPerTrade { get; set; } = 3.0;
    public double AccountEquity { get; set; } = 1000.0;
}
