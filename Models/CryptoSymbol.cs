namespace RSIMasterpro.Models;

public class CryptoSymbol
{
    public string Symbol { get; set; } = "";
    public bool Active { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}
