namespace MaC.Core.Models;

public class Trade
{
    public Guid Id { get; set; }

    public DateTime EntryTime { get; set; }

    public DateTime ExitTime { get; set; }

    public string Instrument { get; set; } = string.Empty;

    public int Contracts { get; set; }

    public decimal EntryPrice { get; set; }

    public decimal ExitPrice { get; set; }

    public decimal GrossPnL { get; set; }

    public decimal NetPnL { get; set; }

    public decimal Commission { get; set; }
}