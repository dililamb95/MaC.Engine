using MaC.Core.Models;

namespace MaC.Core.Evaluations;

public static class EvaluationFactory
{
    public static List<Evaluation> GetAll()
    {
        return new List<Evaluation>
        {
            Create(AccountType.Breeze25K),
            Create(AccountType.Solstice50K),
            Create(AccountType.Summit75K),
            Create(AccountType.Ethereal100K),
            Create(AccountType.Zenith200K)
        };
    }

    public static Evaluation Create(AccountType accountType)
    {
        return accountType switch
        {
            AccountType.Breeze25K => CreateEvaluation(accountType, "BREEZE 25K", "BREEZE (25K)", 25000m, 1750m, 1550m, 550m, 3),
            AccountType.Solstice50K => CreateEvaluation(accountType, "SOLSTICE 50K", "SOLSTICE (50K)", 50000m, 3000m, 2000m, 1100m, 6),
            AccountType.Summit75K => CreateEvaluation(accountType, "SUMMIT 75K", "SUMMIT (75K)", 75000m, 4500m, 2750m, 1600m, 9),
            AccountType.Ethereal100K => CreateEvaluation(accountType, "ETHEREAL 100K", "ETHEREAL (100K)", 100000m, 6000m, 3500m, 2200m, 12),
            AccountType.Zenith200K => CreateEvaluation(accountType, "ZENITH 200K", "ZENITH (200K)", 200000m, 11000m, 6000m, 4400m, 16),
            _ => throw new ArgumentOutOfRangeException(nameof(accountType))
        };
    }

    private static Evaluation CreateEvaluation(
        AccountType type,
        string name,
        string displayName,
        decimal accountSize,
        decimal profitTarget,
        decimal eodDrawdown,
        decimal dailyLossLimit,
        int maxContracts)
    {
        return new Evaluation
        {
            Type = type,
            Name = name,
            DisplayName = displayName,
            AccountSize = accountSize,
            RuleSet = new RuleSet
            {
                ProfitTarget = profitTarget,
                EodDrawdown = eodDrawdown,
                DailyLossLimit = dailyLossLimit,
                MaxContracts = maxContracts,
                MinimumTradingDays = 5,
                MaxConsistencyPercentage = 30m
            }
        };
    }
}