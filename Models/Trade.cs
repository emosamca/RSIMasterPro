namespace RSIMasterpro.Models;

public class Trade
{
    public int Id { get; set; }
    public string Symbol { get; set; } = "";
    public string Side { get; set; } = "";              // LONG | SHORT
    public double EntryPrice { get; set; }
    public double Quantity { get; set; }
    public double StopLoss { get; set; }
    public double TakeProfit { get; set; }
    public string TrackingMode { get; set; } = "STANDARD"; // STANDARD | NO_SL
    public string Status { get; set; } = "OPEN";        // OPEN | CLOSED
    public DateTime EntryTime { get; set; }
    public double? ExitPrice { get; set; }
    public DateTime? ExitTime { get; set; }
    public string? ExitReason { get; set; }             // TP | SL
    public double? Pnl { get; set; }
}

public class TradeStats
{
    public int OpenCount { get; set; }
    public int ClosedCount { get; set; }
    public int PositiveCount { get; set; }
    public int NegativeCount { get; set; }
    public double TotalPnl { get; set; }
}
