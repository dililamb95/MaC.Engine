namespace MaC.Core.Models;

public class TradingContext
{
    public Account Account { get; set; } = new();

    public TradingDay TradingDay { get; set; } = new();

    public RuleSet RuleSet { get; set; } = new();

    public Trade Trade { get; set; } = new();
}