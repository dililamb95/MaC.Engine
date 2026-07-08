using MaC.Core.Models;

namespace MaC.Core.Evaluations;

public class Evaluation
{
    public AccountType Type { get; set; }

    public string Name { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public decimal AccountSize { get; set; }

    public RuleSet RuleSet { get; set; } = new();
}