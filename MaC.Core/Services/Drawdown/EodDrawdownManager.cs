using MaC.Core.Models;

namespace MaC.Core.Services.Drawdown;

public class EodDrawdownManager : IDrawdownManager
{
    public void UpdateFloor(TradingContext context)
    {
        var newFloor = context.Account.HighWaterMark - context.RuleSet.EodDrawdown;

        if (newFloor > context.Account.Floor)
        {
            context.Account.Floor = newFloor;
        }
    }
}