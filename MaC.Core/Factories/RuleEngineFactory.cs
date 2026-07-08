using MaC.Core.Interfaces;
using MaC.Core.Rules;
using MaC.Core.Services;

namespace MaC.Core.Factories;

public static class RuleEngineFactory
{
    public static RuleEngine Create()
    {
        return new RuleEngine(new List<IRule>
        {
            new DailyLossRule(),
            new EodFloorRule(),
            new MaxContractsRule(),
            new ProfitTargetRule()
        });
    }
}