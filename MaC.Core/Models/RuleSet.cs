namespace MaC.Core.Models;

public class RuleSet
{
    public decimal ProfitTarget { get; set; }

    public decimal DailyLossLimit { get; set; }

    public decimal EodDrawdown { get; set; }

    public int MinimumTradingDays { get; set; }

    public decimal MaxConsistencyPercentage { get; set; }

    public int MaxContracts { get; set; }
}