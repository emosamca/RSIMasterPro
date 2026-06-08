namespace RSIMasterpro.Models;

public enum SignalType
{
    Long,
    Short
}

public class TradeSignal
{
    public SignalType Type { get; set; }
    public string Symbol { get; set; } = "";
    public double EntryPrice { get; set; }
    public double StopLoss { get; set; }
    public double TakeProfit { get; set; }
    public double Quantity { get; set; }
    public DateTime CandleCloseTime { get; set; }
}
