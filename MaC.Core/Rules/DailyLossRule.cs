using MaC.Core.Enums;
using MaC.Core.Interfaces;
using MaC.Core.Models;
using MaC.Core.Results;

namespace MaC.Core.Rules;

public class DailyLossRule : IRule
{
    public EngineResult Evaluate(Account account, TradingDay tradingDay, RuleSet ruleSet, Trade trade)
    {
        if (tradingDay.DailyPnL <= -ruleSet.DailyLossLimit)
        {
            return new EngineResult
            {
                Action = EngineAction.FailAccount,
                Reason = "Daily Loss Exceeded",
                RuleTriggered = RuleType.DailyLoss
            };
        }

        return EngineResult.None("Daily Loss OK");
    }
}