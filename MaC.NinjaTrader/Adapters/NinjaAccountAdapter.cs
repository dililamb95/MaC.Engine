using MaC.Core.Factories;
using MaC.Core.Models;
using MaC.Core.Results;
using MaC.Core.Services;
using MaC.NinjaTrader.Mappers;
using NinjaTrader.Cbi;

namespace MaC.NinjaTrader.Adapters;

public class NinjaAccountAdapter
{
    private readonly NinjaTradeMapper _tradeMapper = new();
    private readonly AccountEngine _engine = AccountEngineFactory.Create();

    public EngineResult Process(
        TradingContext context,
        decimal netPnL,
        int contracts)
    {
        context.Trade = _tradeMapper.Map(netPnL, contracts);

        return _engine.ProcessTrade(context);
    }

    public EngineResult Process(
        TradingContext context,
        Execution execution,
        decimal netPnL)
    {
        return Process(
            context,
            netPnL,
            execution.Quantity);
    }
}