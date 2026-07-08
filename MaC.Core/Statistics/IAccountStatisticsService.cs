using MaC.Core.Models;

namespace MaC.Core.Statistics;

public interface IAccountStatisticsService
{
    AccountStatistics Calculate(Account account, IEnumerable<TradeHistory> tradeHistory);
}