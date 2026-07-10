using MaC.Core.Models;
using NinjaTrader.Cbi;

using CoreAccount = MaC.Core.Models.Account;
using CoreAccountStatus = MaC.Core.Enums.AccountStatus;
using CoreTrade = MaC.Core.Models.Trade;
using NinjaAccount = NinjaTrader.Cbi.Account;

namespace MaC.NinjaTrader.Services;

public static class NinjaTradingContextFactory
{
    public static TradingContext CreateDefault()
    {
        return Create(50000m);
    }

    public static TradingContext Create(NinjaAccount ninjaAccount)
    {
        double cashValue = ninjaAccount.Get(
            AccountItem.CashValue,
            Currency.UsDollar);

        return Create(Convert.ToDecimal(cashValue));
    }

    private static TradingContext Create(decimal currentBalance)
    {
        const decimal eodDrawdown = 2000m;

        return new TradingContext
        {
            Account = new CoreAccount
            {
                StartingBalance = currentBalance,
                ClosedBalance = currentBalance,
                HighWaterMark = currentBalance,
                Floor = currentBalance - eodDrawdown,
                Status = CoreAccountStatus.Active
            },

            TradingDay = new TradingDay
            {
                StartingBalance = currentBalance,
                CurrentBalance = currentBalance
            },

            RuleSet = new RuleSet
            {
                DailyLossLimit = 1100m,
                EodDrawdown = eodDrawdown,
                MaxContracts = 6,
                ProfitTarget = 3000m,
                MinimumTradingDays = 5,
                MaxConsistencyPercentage = 30m
            },

            Trade = new CoreTrade()
        };
    }
}