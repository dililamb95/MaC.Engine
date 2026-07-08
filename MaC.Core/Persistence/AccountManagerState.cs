using MaC.Core.Models;

namespace MaC.Core.Persistence;

public class AccountManagerState
{
    public List<Account> Accounts { get; set; } = new();

    public List<TradeHistory> TradeHistory { get; set; } = new();
}