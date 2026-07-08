using MaC.Core.Models;

namespace MaC.NinjaTrader.Mappers;

public class NinjaTradeMapper
{
    public Trade Map(decimal netPnL, int contracts)
    {
        return new Trade
        {
            NetPnL = netPnL,
            Contracts = contracts
        };
    }
}