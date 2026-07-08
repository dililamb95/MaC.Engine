using MaC.Core.Enums;
using MaC.Core.Models;
using MaC.Core.Results;
using MaC.Core.Services.Drawdown;

namespace MaC.Core.Services;

public class AccountEngine
{
    private readonly RuleEngine _ruleEngine;
    private readonly IDrawdownManager _drawdownManager;

    public AccountEngine(RuleEngine ruleEngine, IDrawdownManager drawdownManager)
    {
        _ruleEngine = ruleEngine;
        _drawdownManager = drawdownManager;
    }

    public EngineResult ProcessTrade(TradingContext context)
    {
        if (context.Account.Status == AccountStatus.Failed ||
            context.Account.Status == AccountStatus.Passed)
        {
            return EngineResult.None("Account is closed");
        }

        if (context.Trade.Contracts > context.RuleSet.MaxContracts)
        {
            var result = new EngineResult
            {
                Action = EngineAction.FailAccount,
                Reason = "Max contracts exceeded",
                RuleTriggered = RuleType.MaxContracts
            };

            UpdateAccountStatus(context, result);
            return result;
        }

        UpdateTradingDays(context);
        UpdateBalances(context);
        UpdateHighWaterMark(context);
        _drawdownManager.UpdateFloor(context);
        UpdateStatistics(context);

        var evaluationResult = EvaluateRules(context);

        UpdateAccountStatus(context, evaluationResult);

        return evaluationResult;
    }

    private void UpdateTradingDays(TradingContext context)
    {
        if (context.TradingDay.TradeCount == 0)
        {
            context.Account.TradingDays++;
        }
    }

    private void UpdateBalances(TradingContext context)
    {
        context.Account.ClosedBalance += context.Trade.NetPnL;
        context.TradingDay.CurrentBalance += context.Trade.NetPnL;
    }

    private void UpdateHighWaterMark(TradingContext context)
    {
        if (context.Account.ClosedBalance > context.Account.HighWaterMark)
        {
            context.Account.HighWaterMark = context.Account.ClosedBalance;
        }
    }

    private void UpdateStatistics(TradingContext context)
    {
        context.TradingDay.TradeCount++;
        context.TradingDay.TotalNetPnL += context.Trade.NetPnL;

        if (context.Trade.NetPnL > 0)
        {
            context.TradingDay.WinningTrades++;
        }
        else if (context.Trade.NetPnL < 0)
        {
            context.TradingDay.LosingTrades++;
        }
    }

    private EngineResult EvaluateRules(TradingContext context)
    {
        return _ruleEngine.Evaluate(
            context.Account,
            context.TradingDay,
            context.RuleSet,
            context.Trade);
    }

    private void UpdateAccountStatus(TradingContext context, EngineResult result)
    {
        if (result.Action == EngineAction.FailAccount)
        {
            context.Account.Status = AccountStatus.Failed;
        }

        if (result.Action == EngineAction.PassAccount)
        {
            context.Account.Status = AccountStatus.Passed;
        }
    }
}