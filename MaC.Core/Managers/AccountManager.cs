using MaC.Core.Enums;
using MaC.Core.Evaluations;
using MaC.Core.Factories;
using MaC.Core.Interfaces;
using MaC.Core.Models;
using MaC.Core.Persistence;
using MaC.Core.Results;
using MaC.Core.Services;
using MaC.Core.Statistics;
using MaC.Core.Summaries;

namespace MaC.Core.Managers;

public class AccountManager
{
    private readonly List<Account> _accounts = new();
    private readonly List<TradeHistory> _tradeHistory = new();
    private readonly Dictionary<(Guid AccountId, DateTime Date), TradingDay> _tradingDays = new();

    private readonly AccountEngine _accountEngine;
    private readonly IAccountManagerStorage _storage;
    private readonly IAccountStatisticsService _statisticsService;
    private readonly AccountSummaryService _summaryService;

    public AccountManager()
        : this(new AccountManagerStorage())
    {
    }

    public AccountManager(IAccountManagerStorage storage)
    {
        _accountEngine = AccountEngineFactory.Create();
        _storage = storage;
        _statisticsService = new AccountStatisticsService();
        _summaryService = new AccountSummaryService();
    }

    public IReadOnlyList<Account> Accounts => _accounts;
    public IReadOnlyList<TradeHistory> TradeHistory => _tradeHistory;

    public Account CreateAccount(AccountType accountType)
    {
        var evaluation = EvaluationFactory.Create(accountType);

        var account = new Account
        {
            Id = Guid.NewGuid(),
            Type = accountType,
            Name = evaluation.Name,
            StartingBalance = evaluation.AccountSize,
            ClosedBalance = evaluation.AccountSize,
            HighWaterMark = evaluation.AccountSize,
            InitialFloor = evaluation.AccountSize - evaluation.RuleSet.EodDrawdown,
            Floor = evaluation.AccountSize - evaluation.RuleSet.EodDrawdown,
            Status = AccountStatus.Active
        };

        _accounts.Add(account);

        return account;
    }

    public EngineResult ProcessTrade(Account account, Trade trade)
    {
        var evaluation = EvaluationFactory.Create(account.Type);
        var tradeDate = GetTradeDate(trade);
        var tradingDay = GetOrCreateTradingDay(account, tradeDate);

        var context = new TradingContext
        {
            Account = account,
            TradingDay = tradingDay,
            RuleSet = evaluation.RuleSet,
            Trade = trade
        };

        var result = _accountEngine.ProcessTrade(context);

        _tradeHistory.Add(new TradeHistory
        {
            Date = tradeDate,
            AccountName = account.Name,
            Contracts = trade.Contracts,
            NetPnL = trade.NetPnL,
            BalanceAfterTrade = account.ClosedBalance,
            AccountStatus = account.Status,
            RuleTriggered = result.RuleTriggered?.ToString() ?? string.Empty
        });

        return result;
    }

    private TradingDay GetOrCreateTradingDay(Account account, DateTime date)
    {
        var key = (account.Id, date.Date);

        if (_tradingDays.TryGetValue(key, out var tradingDay))
        {
            return tradingDay;
        }

        tradingDay = new TradingDay
        {
            Date = date.Date,
            StartingBalance = account.ClosedBalance,
            CurrentBalance = account.ClosedBalance
        };

        _tradingDays[key] = tradingDay;

        return tradingDay;
    }

    private static DateTime GetTradeDate(Trade trade)
    {
        return trade.EntryTime == default
            ? DateTime.Today
            : trade.EntryTime.Date;
    }

    public Account? GetAccount(string name)
    {
        return _accounts.FirstOrDefault(a => a.Name == name);
    }

    public IReadOnlyList<Account> GetAllAccounts()
    {
        return _accounts;
    }

    public IReadOnlyList<TradeHistory> GetTradeHistory()
    {
        return _tradeHistory;
    }

    public AccountStatistics GetStatistics(Account account)
    {
        return _statisticsService.Calculate(account, _tradeHistory);
    }

    public string GetSummary(Account account)
    {
        var statistics = GetStatistics(account);
        return _summaryService.Generate(account, statistics);
    }

    public void Save(string filePath)
    {
        var state = new AccountManagerState
        {
            Accounts = _accounts,
            TradeHistory = _tradeHistory
        };

        _storage.Save(state, filePath);
    }

    public void Load(string filePath)
    {
        var state = _storage.Load(filePath);

        _accounts.Clear();
        _accounts.AddRange(state.Accounts);

        _tradeHistory.Clear();
        _tradeHistory.AddRange(state.TradeHistory);
    }
}