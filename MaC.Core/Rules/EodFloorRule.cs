using MaC.Core.Enums;
using MaC.Core.Interfaces;
using MaC.Core.Models;
using MaC.Core.Results;

namespace MaC.Core.Rules;

public class EodFloorRule : IRule
{
    public EngineResult Evaluate(Account account, TradingDay tradingDay, RuleSet ruleSet, Trade trade)
    {
        if (account.ClosedBalance <= account.Floor)
        {
            return new EngineResult
            {
                Action = EngineAction.FailAccount,
                Reason = "EOD Floor Breached",
                RuleTriggered = RuleType.EodFloor
            };
        }

        return EngineResult.None("EOD Floor OK");
    }
}