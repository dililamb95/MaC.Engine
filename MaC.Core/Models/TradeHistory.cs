using MaC.Core.Enums;

namespace MaC.Core.Models;

public class TradeHistory
{
    public DateTime Date { get; set; }

    public string AccountName { get; set; } = string.Empty;

    public int Contracts { get; set; }

    public decimal NetPnL { get; set; }

    public decimal BalanceAfterTrade { get; set; }

    public AccountStatus AccountStatus { get; set; }

    public string RuleTriggered { get; set; } = string.Empty;
}