using MaC.Core.Models;

namespace MaC.Core.Services.Drawdown;

public interface IDrawdownManager
{
    void UpdateFloor(TradingContext context);
}