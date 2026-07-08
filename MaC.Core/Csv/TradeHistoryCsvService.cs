using System.Globalization;
using System.Text;
using MaC.Core.Enums;
using MaC.Core.Models;

namespace MaC.Core.Csv;

public class TradeHistoryCsvService
{
    public void Export(IEnumerable<TradeHistory> trades, string filePath)
    {
        var builder = new StringBuilder();

        builder.AppendLine("Date,AccountName,Contracts,NetPnL,BalanceAfterTrade,AccountStatus,RuleTriggered");

        foreach (var trade in trades)
        {
            builder.AppendLine(
                $"{trade.Date:yyyy-MM-dd}," +
                $"{trade.AccountName}," +
                $"{trade.Contracts}," +
                $"{trade.NetPnL.ToString(CultureInfo.InvariantCulture)}," +
                $"{trade.BalanceAfterTrade.ToString(CultureInfo.InvariantCulture)}," +
                $"{trade.AccountStatus}," +
                $"{trade.RuleTriggered}");
        }

        File.WriteAllText(filePath, builder.ToString());
    }

    public List<TradeHistory> Import(string filePath)
    {
        var lines = File.ReadAllLines(filePath).Skip(1);
        var trades = new List<TradeHistory>();

        foreach (var line in lines)
        {
            var parts = line.Split(',');

            trades.Add(new TradeHistory
            {
                Date = DateTime.Parse(parts[0]),
                AccountName = parts[1],
                Contracts = int.Parse(parts[2]),
                NetPnL = decimal.Parse(parts[3], CultureInfo.InvariantCulture),
                BalanceAfterTrade = decimal.Parse(parts[4], CultureInfo.InvariantCulture),
                AccountStatus = Enum.Parse<AccountStatus>(parts[5]),
                RuleTriggered = parts[6]
            });
        }

        return trades;
    }
}