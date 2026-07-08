using MaC.Core.Models;
using MaC.Core.Evaluations;

namespace MaC.Core.Statistics;

public class AccountStatisticsService : IAccountStatisticsService
{
    public AccountStatistics Calculate(
        Account account,
        IEnumerable<TradeHistory> tradeHistory)
    {
        var trades = tradeHistory.ToList();

        var totalTrades = trades.Count;
        var winningTrades = trades.Count(t => t.NetPnL > 0);
        var losingTrades = trades.Count(t => t.NetPnL < 0);

        var grossProfit = trades
            .Where(t => t.NetPnL > 0)
            .Sum(t => t.NetPnL);

        var grossLoss = Math.Abs(trades
            .Where(t => t.NetPnL < 0)
            .Sum(t => t.NetPnL));

        var winRate = totalTrades == 0
            ? 0m
            : (decimal)winningTrades / totalTrades * 100m;

        var profitFactor = grossLoss == 0
            ? 0m
            : grossProfit / grossLoss;

        var averageWin = winningTrades == 0
            ? 0m
            : grossProfit / winningTrades;

        var averageLoss = losingTrades == 0
            ? 0m
            : grossLoss / losingTrades;

        var expectancy = totalTrades == 0
            ? 0m
            : ((averageWin * winningTrades) - (averageLoss * losingTrades)) / totalTrades;

        var bestTrade = trades.Any()
            ? trades.Max(t => t.NetPnL)
            : 0m;

        var consistency = grossProfit == 0
            ? 0m
            : bestTrade / grossProfit * 100m;
        var evaluation = EvaluationFactory.Create(account.Type);

        var profitTarget = evaluation.RuleSet.ProfitTarget;
        var remainingToTarget = Math.Max(0m, profitTarget - (account.ClosedBalance - account.StartingBalance));

        return new AccountStatistics
        {
            Balance = account.ClosedBalance,
            Profit = account.ClosedBalance - account.StartingBalance,
            HighWaterMark = account.HighWaterMark,
            Floor = account.Floor,

            TotalTrades = totalTrades,
            WinningTrades = winningTrades,
            LosingTrades = losingTrades,
            WinRate = winRate,

            GrossProfit = grossProfit,
            GrossLoss = grossLoss,
            ProfitFactor = profitFactor,

            AverageWin = averageWin,
            AverageLoss = averageLoss,
            Expectancy = expectancy,

            Consistency = consistency,
            ProfitTarget = profitTarget,
            RemainingToTarget = remainingToTarget,
        };
    }
}