namespace RSIMasterpro.Models;

public class Candle
{
    public DateTime OpenTime { get; set; }
    public DateTime CloseTime { get; set; }
    public double Open { get; set; }
    public double High { get; set; }
    public double Low { get; set; }
    public double Close { get; set; }
    public double Volume { get; set; }
}
