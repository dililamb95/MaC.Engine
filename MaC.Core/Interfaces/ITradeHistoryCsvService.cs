using MaC.Core.Models;

namespace MaC.Core.Interfaces;

public interface ITradeHistoryCsvService
{
    void Export(IEnumerable<TradeHistory> trades, string filePath);

    List<TradeHistory> Import(string filePath);
}