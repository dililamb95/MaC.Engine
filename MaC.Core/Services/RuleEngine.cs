using MaC.Core.Enums;
using MaC.Core.Interfaces;
using MaC.Core.Models;
using MaC.Core.Results;

namespace MaC.Core.Services;

public class RuleEngine
{
    private readonly List<IRule> _rules;

    public RuleEngine(List<IRule> rules)
    {
        _rules = rules;
    }

    public EngineResult Evaluate(Account account, TradingDay tradingDay, RuleSet ruleSet, Trade trade)
    {
        foreach (var rule in _rules)
        {
            var result = rule.Evaluate(account, tradingDay, ruleSet, trade);

            if (result.Action != EngineAction.None)
            {
                return result;
            }
        }

        return EngineResult.None("All rules passed");
    }
}