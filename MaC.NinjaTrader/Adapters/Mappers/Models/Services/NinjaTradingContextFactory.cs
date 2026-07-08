using MaC.Core.Enums;
using MaC.Core.Models;

namespace MaC.NinjaTrader.Services;

public static class NinjaTradingContextFactory
{
    public static TradingContext CreateDefault()
    {
        return new TradingContext
        {
            Account = new Account
            {
                StartingBalance = 50000m,
                ClosedBalance = 50000m,
                HighWaterMark = 50000m,
                Floor = 48000m,
                Status = AccountStatus.Active
            },
            TradingDay = new TradingDay
            {
                StartingBalance = 50000m,
                CurrentBalance = 50000m
            },
            RuleSet = new RuleSet
            {
                DailyLossLimit = 1100m,
                EodDrawdown = 2000m,
                MaxContracts = 6,
                ProfitTarget = 3000m,
                MinimumTradingDays = 5,
                MaxConsistencyPercentage = 30m
            },
            Trade = new Trade()
        };
    }
}