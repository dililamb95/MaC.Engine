using MaC.Core.Enums;
using MaC.Core.Interfaces;
using MaC.Core.Models;
using MaC.Core.Results;

namespace MaC.Core.Rules;

public class ProfitTargetRule : IRule
{
    public EngineResult Evaluate(Account account, TradingDay tradingDay, RuleSet ruleSet, Trade trade)
    {
        var profit = account.ClosedBalance - account.StartingBalance;

        if (profit <= 0)
        {
            return EngineResult.None("Profit target, minimum days, or consistency not met");
        }

        var consistency = CalculateConsistency(tradingDay, profit);

        if (profit >= ruleSet.ProfitTarget &&
            account.TradingDays >= ruleSet.MinimumTradingDays &&
            consistency <= ruleSet.MaxConsistencyPercentage)
        {
            return new EngineResult
            {
                Action = EngineAction.PassAccount,
                Reason = "Profit target reached",
                RuleTriggered = RuleType.ProfitTarget
            };
        }

        return EngineResult.None("Profit target, minimum days, or consistency not met");
    }

    private static decimal CalculateConsistency(TradingDay tradingDay, decimal profit)
    {
        if (tradingDay.TotalNetPnL <= 0)
        {
            return 0m;
        }

        return tradingDay.TotalNetPnL / profit * 100m;
    }
}