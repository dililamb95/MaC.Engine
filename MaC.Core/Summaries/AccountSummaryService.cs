using MaC.Core.Models;
using MaC.Core.Statistics;

namespace MaC.Core.Summaries;

public class AccountSummaryService
{
    public string Generate(Account account, AccountStatistics stats)
    {
        return $"""
        ===========================
        {account.Name}
        ===========================

        Estado............. {account.Status}

        Balance............ {stats.Balance:C}
        Ganancia........... {stats.Profit:C}
        Objetivo........... {stats.ProfitTarget:C}
        Faltan............. {stats.RemainingToTarget:C}

        High Water Mark.... {stats.HighWaterMark:C}
        Floor.............. {stats.Floor:C}

        Trades............. {stats.TotalTrades}
        Win Rate........... {stats.WinRate:N2}%
        Profit Factor...... {stats.ProfitFactor:N2}
        Average Win........ {stats.AverageWin:C}
        Average Loss....... {stats.AverageLoss:C}
        Expectancy......... {stats.Expectancy:C}
        Consistency........ {stats.Consistency:N2}%
        """;
    }
}