namespace MaC.Core.Models;

public class TradingDay
{
    public DateTime Date { get; set; }

    public decimal StartingBalance { get; set; }

    public decimal CurrentBalance { get; set; }

    public int TradeCount { get; set; }

    public int WinningTrades { get; set; }

    public int LosingTrades { get; set; }

    public decimal TotalNetPnL { get; set; }

    public decimal DailyPnL => CurrentBalance - StartingBalance;
}