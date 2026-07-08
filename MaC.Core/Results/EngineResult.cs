using MaC.Core.Enums;

namespace MaC.Core.Results;

public class EngineResult
{
    public EngineAction Action { get; set; }
    public string Reason { get; set; } = string.Empty;
    public RuleType? RuleTriggered { get; set; }

    public bool IsAccountFailed => Action == EngineAction.FailAccount;
    public bool IsAccountPassed => Action == EngineAction.PassAccount;

    public static EngineResult None(string reason)
    {
        return new EngineResult
        {
            Action = EngineAction.None,
            Reason = reason,
            RuleTriggered = null
        };
    }
}