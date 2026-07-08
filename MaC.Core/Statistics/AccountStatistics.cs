namespace MaC.Core.Statistics;

/// <summary>
/// Representa las estadísticas calculadas de una cuenta.
/// </summary>
public class AccountStatistics
{
    public decimal Balance { get; set; }

    public decimal Profit { get; set; }

    public decimal ProfitTarget { get; set; }

    public decimal RemainingToTarget { get; set; }

    public decimal HighWaterMark { get; set; }

    public decimal Floor { get; set; }

    public int TotalTrades { get; set; }

    public int WinningTrades { get; set; }

    public int LosingTrades { get; set; }

    public decimal WinRate { get; set; }

    public decimal GrossProfit { get; set; }

    public decimal GrossLoss { get; set; }

    public decimal ProfitFactor { get; set; }

    public decimal AverageWin { get; set; }

    public decimal AverageLoss { get; set; }

    public decimal Expectancy { get; set; }

    public decimal Consistency { get; set; }
}