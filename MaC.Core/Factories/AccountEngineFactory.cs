using MaC.Core.Services;
using MaC.Core.Services.Drawdown;

namespace MaC.Core.Factories;

public static class AccountEngineFactory
{
    public static AccountEngine Create()
    {
        return new AccountEngine(
            RuleEngineFactory.Create(),
            new EodDrawdownManager());
    }
}