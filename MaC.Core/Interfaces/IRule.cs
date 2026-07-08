using MaC.Core.Models;
using MaC.Core.Results;

namespace MaC.Core.Interfaces;

public interface IRule
{
    EngineResult Evaluate(Account account, TradingDay tradingDay, RuleSet ruleSet, Trade trade);
}