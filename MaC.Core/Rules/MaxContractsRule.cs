using MaC.Core.Enums;
using MaC.Core.Interfaces;
using MaC.Core.Models;
using MaC.Core.Results;

namespace MaC.Core.Rules;

public class MaxContractsRule : IRule
{
    public EngineResult Evaluate(Account account, TradingDay tradingDay, RuleSet ruleSet, Trade trade)
    {
        if (trade.Contracts > ruleSet.MaxContracts)
        {
            return new EngineResult
            {
                Action = EngineAction.FailAccount,
                Reason = "Maximum Contracts Exceeded",
                RuleTriggered = RuleType.MaxContracts
            };
        }

        return EngineResult.None("Max Contracts OK");
    }
}