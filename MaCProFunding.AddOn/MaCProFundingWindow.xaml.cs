using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MaC.Core.Enums;
using MaC.Core.Models;
using MaC.Core.Summaries;
using MaC.NinjaTrader.Adapters;
using MaC.NinjaTrader.Models;
using MaC.NinjaTrader.Services;

namespace MaCProFunding.AddOn
{
    /// <summary>
    /// Ventana local utilizada para visualizar
    /// el estado producido por MaC Engine.
    ///
    /// La ventana puede conectarse a múltiples
    /// sesiones administradas por NinjaAccountListener.
    /// </summary>
    public partial class MaCProFundingWindow : Window
    {
        private NinjaAccountListener? _listener;

        private NinjaAccountSession? _session;

        private readonly DispatcherTimer
            _refreshTimer;

        private bool _isLoadingAccounts;

        private List<string> _knownAccountNames =
    new List<string>();

        private int _accountRefreshTickCounter;

        /// <summary>
        /// Constructor utilizado por el diseñador
        /// y para inicializar la interfaz.
        /// </summary>
        public MaCProFundingWindow()
        {
            InitializeComponent();

            PremiumDashboard.AccountSelectionRequested +=
                OnPremiumAccountSelectionRequested;

            PremiumDashboard.RefreshAccountsRequested +=
                OnPremiumRefreshAccountsRequested;

            _refreshTimer =
                new DispatcherTimer
                {
                    Interval =
                        TimeSpan.FromMilliseconds(500)
                };

            _refreshTimer.Tick +=
                OnRefreshTimerTick;

            Closed +=
                OnWindowClosed;

            ShowDisconnectedState();
        }

        /// <summary>
        /// Constructor principal del Dashboard
        /// multicuenta.
        /// </summary>
        public MaCProFundingWindow(
            NinjaAccountListener listener)
            : this()
        {
            ConnectListener(
                listener);
        }

        /// <summary>
        /// Constructor conservado para mantener
        /// compatibilidad con código antiguo.
        ///
        /// Conecta directamente una sola sesión.
        /// </summary>
        public MaCProFundingWindow(
            NinjaAccountSession session)
            : this()
        {
            ConnectSession(
                session);
        }

        /// <summary>
        /// Conecta el Dashboard con el listener
        /// oficial que administra todas las cuentas MaC_.
        /// </summary>
        public void ConnectListener(
            NinjaAccountListener listener)
        {
            _listener =
                listener
                ?? throw new ArgumentNullException(
                    nameof(listener));

            LoadAccounts();

            if (!_refreshTimer.IsEnabled)
            {
                _refreshTimer.Start();
            }
        }

        /// <summary>
        /// Conecta directamente una sesión.
        ///
        /// Este método se mantiene por compatibilidad,
        /// pero el flujo principal utiliza ConnectListener.
        /// </summary>
        public void ConnectSession(
     NinjaAccountSession session)
        {
            _session =
                session
                ?? throw new ArgumentNullException(
                    nameof(session));

            PremiumDashboard.AttachSession(
                _session);

            RefreshDashboard();

            if (!_refreshTimer.IsEnabled)
            {
                _refreshTimer.Start();
            }
        }

        /// <summary>
        /// Carga en el selector todas las cuentas
        /// monitoreadas por NinjaAccountListener.
        /// </summary>
        private void LoadAccounts()
        {
            if (_listener == null)
            {
                _session =
                    null;

                ShowDisconnectedState();

                return;
            }

            string? previousSelection =
                AccountSelector.SelectedItem
                    as string;

            var accountNames =
                _listener.AccountNames
                    .ToList();
            _knownAccountNames =
    accountNames.ToList();

            _isLoadingAccounts =
                true;

            try
            {
                AccountSelector.ItemsSource =
                    null;

                AccountSelector.ItemsSource =
                    accountNames;

                if (accountNames.Count == 0)
                {
                    _session = null;

                    AccountSelector.SelectedItem = null;

                    PremiumDashboard.SetAccounts(
                        accountNames,
                        null);

                    ShowDisconnectedState();

                    return;
                }

                string selectedAccountName;

                if (!string.IsNullOrWhiteSpace(
                        previousSelection) &&
                    accountNames.Contains(
                        previousSelection,
                        StringComparer.OrdinalIgnoreCase))
                {
                    selectedAccountName =
                        previousSelection;
                }
                else
                {
                    selectedAccountName =
                        accountNames[0];
                }

                AccountSelector.SelectedItem =
                    selectedAccountName;
                PremiumDashboard.SetAccounts(
    accountNames,
    selectedAccountName);
                SelectSession(
                    selectedAccountName);
            }
            finally
            {
                _isLoadingAccounts =
                    false;
            }
        }

        /// <summary>
        /// Selecciona la sesión correspondiente
        /// al nombre elegido en el ComboBox.
        /// </summary>
        private void SelectSession(
            string? accountName)
        {
            if (_listener == null ||
                string.IsNullOrWhiteSpace(
                    accountName))
            {
                _session =
                    null;

                ShowDisconnectedState();

                return;
            }

            _session =
    _listener.GetSession(
        accountName);

            if (_session == null)
            {
                ShowDisconnectedState();

                return;
            }

            PremiumDashboard.SetSelectedAccount(
    accountName);

            PremiumDashboard.AttachSession(
                _session);

            RefreshDashboard();
        }

        /// <summary>
        /// Se ejecuta cuando el usuario selecciona
        /// otra cuenta en el Dashboard.
        /// </summary>
        private void OnAccountSelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (_isLoadingAccounts)
            {
                return;
            }

            string? selectedAccountName =
                AccountSelector.SelectedItem
                    as string;

            SelectSession(
                selectedAccountName);
        }

        /// <summary>
        /// Recarga manualmente la lista de cuentas.
        /// </summary>
        private void OnRefreshAccountsClick(
            object sender,
            RoutedEventArgs e)
        {
            if (_listener != null)
            {
                _listener.RefreshAccounts();
            }

            LoadAccounts();
        }

        private void OnPremiumAccountSelectionRequested(
            object? sender,
            string? accountName)
        {
            SelectSession(
                accountName);
        }

        private void OnPremiumRefreshAccountsRequested(
            object? sender,
            EventArgs e)
        {
            if (_listener != null)
            {
                _listener.RefreshAccounts();
            }

            LoadAccounts();
        }

        /// <summary>
        /// Obtiene una fotografía consistente de la sesión
        /// y actualiza el Dashboard.
        /// </summary>
        public void RefreshDashboard()
        {
            if (_session == null)
            {
                ShowDisconnectedState();

                return;
            }

            EvaluationState state =
                _session.GetEvaluationState();

            RenderState(
                state);
        }

        /// <summary>
        /// Actualiza todos los controles visuales
        /// utilizando un único EvaluationState.
        /// </summary>
        private void RenderState(
            EvaluationState state)
        {
            AccountNameText.Text =
                state.AccountName;

            NinjaAccountNameText.Text =
                _session?.NinjaAccount.Name
                ?? "Sin cuenta NinjaTrader";

            BalanceText.Text =
                FormatMoney(
                    state.CurrentBalance);

            FloorText.Text =
                FormatMoney(
                    state.CurrentFloor);

            RenderDailyPnL(
                state);

            RenderDailyLoss(
                state);

            RenderTradingDays(
                state);

            RenderProfitTarget(
                state);

            RenderConsistency(
                state);

            RenderProgressionLadder(
                state);

            RenderTodayActivity();

            RenderAccountStatus(
                state.Status.ToString());
        }

        /// <summary>
        /// Muestra la ganancia o pérdida acumulada
        /// durante el día actual.
        /// </summary>
        private void RenderDailyPnL(
            EvaluationState state)
        {
            decimal dailyPnL =
                state.DailyPnL;

            DailyPnLText.Text =
                FormatSignedMoney(
                    dailyPnL);

            if (dailyPnL > 0m)
            {
                DailyPnLText.Foreground =
                    CreateBrush(
                        34,
                        197,
                        94);

                return;
            }

            if (dailyPnL < 0m)
            {
                DailyPnLText.Foreground =
                    CreateBrush(
                        248,
                        113,
                        113);

                return;
            }

            DailyPnLText.Foreground =
                CreateBrush(
                    209,
                    213,
                    219);
        }

        /// <summary>
        /// Muestra el estado definitivo de la regla
        /// de pérdida diaria.
        ///
        /// Cuando la cuenta ya fue declarada Failed,
        /// conserva visualmente la infracción aunque
        /// el PnL cambie después del cierre.
        /// </summary>
        private void RenderDailyLoss(
            EvaluationState state)
        {
            bool accountFailed =
                state.Status ==
                AccountStatus.Failed;

            bool dailyLossLimitBreached =
                state.DailyLossLimit > 0m &&
                state.DailyPnL <=
                    -state.DailyLossLimit;

            if (accountFailed ||
                dailyLossLimitBreached)
            {
                DailyLossText.Text =
                    "✕ LIMIT BREACHED";

                DailyLossText.Foreground =
                    CreateBrush(
                        248,
                        113,
                        113);

                return;
            }

            DailyLossText.Text =
                "✓ OK";

            DailyLossText.Foreground =
                CreateBrush(
                    34,
                    197,
                    94);
        }

        /// <summary>
        /// Muestra los días completados y actualiza
        /// su barra de progreso.
        /// </summary>
        private void RenderTradingDays(
            EvaluationState state)
        {
            int requiredDays =
                Math.Max(
                    1,
                    state.MinimumTradingDays);

            int completedDays =
                Math.Max(
                    0,
                    state.TradingDaysCompleted);

            TradingDaysText.Text =
                $"{completedDays} / " +
                $"{state.MinimumTradingDays}";

            TradingDaysProgressBar.Maximum =
                requiredDays;

            TradingDaysProgressBar.Value =
                Math.Min(
                    completedDays,
                    requiredDays);
        }

        /// <summary>
        /// Muestra el beneficio acumulado frente
        /// al objetivo de la evaluación.
        /// </summary>
        private void RenderProfitTarget(
            EvaluationState state)
        {
            decimal visibleProfit =
                Math.Max(
                    0m,
                    state.TotalProfit);

            decimal target =
                Math.Max(
                    0m,
                    state.ProfitTarget);

            ProfitTargetText.Text =
                $"{FormatMoneyWithoutDecimals(visibleProfit)} / " +
                $"{FormatMoneyWithoutDecimals(target)}";

            decimal percentage =
                target <= 0m
                    ? 0m
                    : visibleProfit /
                      target *
                      100m;

            percentage =
                Math.Min(
                    100m,
                    Math.Max(
                        0m,
                        percentage));

            ProfitTargetProgressBar.Maximum =
                100;

            ProfitTargetProgressBar.Value =
                Convert.ToDouble(
                    percentage);
        }

        /// <summary>
        /// Muestra el estado informativo de consistencia.
        ///
        /// La consistencia no falla la cuenta. Cuando todavía
        /// no se cumple, indica cuánto beneficio adicional
        /// necesita alcanzar el trader.
        /// </summary>
        private void RenderConsistency(
            EvaluationState state)
        {
            decimal consistencyPercentage =
                Math.Max(
                    0m,
                    state.ConsistencyPercentage);

            decimal bestDayProfit =
                Math.Max(
                    0m,
                    state.BestDayProfit);

            decimal totalProfit =
                Math.Max(
                    0m,
                    state.TotalProfit);

            decimal requiredTotalProfit =
                Math.Max(
                    0m,
                    state.RequiredTotalProfitForConsistency);

            decimal remainingProfit =
                Math.Max(
                    0m,
                    state.RemainingProfitForConsistency);

            ConsistencyText.Text =
                $"{consistencyPercentage:0.0}%";

            ConsistencyBestDayText.Text =
                $"Best Day: " +
                $"{FormatMoney(bestDayProfit)}";

            ConsistencyProgressBar.Minimum =
                0;

            ConsistencyProgressBar.Maximum =
                100;

            decimal progressPercentage;

            if (requiredTotalProfit <= 0m)
            {
                progressPercentage =
                    0m;
            }
            else
            {
                progressPercentage =
                    totalProfit /
                    requiredTotalProfit *
                    100m;
            }

            progressPercentage =
                Math.Min(
                    100m,
                    Math.Max(
                        0m,
                        progressPercentage));

            ConsistencyProgressBar.Value =
                Convert.ToDouble(
                    progressPercentage);

            if (state.ConsistencyMet)
            {
                ConsistencyText.Foreground =
                    CreateBrush(
                        110,
                        231,
                        183);

                ConsistencyRequirementText.Text =
                    "✓ MET";

                ConsistencyRequirementText.Foreground =
                    CreateBrush(
                        110,
                        231,
                        183);

                ConsistencyProgressBar.Foreground =
                    CreateBrush(
                        16,
                        185,
                        129);

                ConsistencyProgressBar.Value =
                    100;

                return;
            }

            ConsistencyText.Foreground =
                CreateBrush(
                    245,
                    158,
                    11);

            if (bestDayProfit <= 0m)
            {
                ConsistencyRequirementText.Text =
                    "Waiting for profitable day";
            }
            else
            {
                ConsistencyRequirementText.Text =
                    $"Need: " +
                    $"{FormatMoney(remainingProfit)}";
            }

            ConsistencyRequirementText.Foreground =
                CreateBrush(
                    251,
                    191,
                    36);

            ConsistencyProgressBar.Foreground =
                CreateBrush(
                    245,
                    158,
                    11);
        }

        /// <summary>
        /// Presenta la Progression Ladder utilizando exclusivamente
        /// la información incluida en el EvaluationState recibido.
        ///
        /// Esta lógica es únicamente visual y no modifica contratos,
        /// reglas ni enforcement de NinjaTrader.
        /// </summary>
        private void RenderProgressionLadder(
            EvaluationState state)
        {
            var steps =
                state.PositionSizeProgression == null
                    ? new List<PositionSizeStep>()
                    : state.PositionSizeProgression
                        .OrderBy(
                            step => step.MinimumProfit)
                        .ToList();

            if (steps.Count == 0)
            {
                ClearProgressionLadder(
                    "Progression data unavailable");

                return;
            }

            decimal totalProfit =
                Math.Max(
                    0m,
                    state.TotalProfit);

            int currentIndex =
                -1;

            for (int index = 0;
                 index < steps.Count;
                 index++)
            {
                if (totalProfit >=
                    steps[index].MinimumProfit)
                {
                    currentIndex =
                        index;
                }
            }

            if (currentIndex < 0)
            {
                currentIndex =
                    0;
            }

            var currentStep =
                steps[currentIndex];

            ProgressionCurrentLevelText.Text =
                "LEVEL " +
                ToRomanNumeral(
                    currentIndex + 1);

            ProgressionCurrentContractsText.Text =
                FormatContracts(
                    currentStep.MaximumContracts);

            List<ProgressionStepRow> rows =
                new List<ProgressionStepRow>();

            for (int index = 0;
                 index < steps.Count;
                 index++)
            {
                bool isReached =
                    index < currentIndex;

                bool isCurrent =
                    index == currentIndex;

                rows.Add(
                    new ProgressionStepRow
                    {
                        ProfitText =
                            FormatMoneyWithoutDecimals(
                                steps[index].MinimumProfit),

                        ContractsText =
                            FormatContracts(
                                steps[index].MaximumContracts),

                        IndicatorText =
                            isReached
                                ? "✓"
                                : isCurrent
                                    ? "●"
                                    : "○",

                        IndicatorBackground =
                            isCurrent
                                ? CreateBrush(200, 150, 56)
                                : isReached
                                    ? CreateBrush(34, 197, 94)
                                    : CreateBrush(58, 63, 72),

                        RowBackground =
                            isCurrent
                                ? CreateBrush(32, 27, 17)
                                : Brushes.Transparent,

                        PrimaryForeground =
                            isCurrent
                                ? CreateBrush(248, 223, 120)
                                : isReached
                                    ? CreateBrush(245, 245, 245)
                                    : CreateBrush(115, 122, 134),

                        SecondaryForeground =
                            isCurrent
                                ? CreateBrush(248, 223, 120)
                                : isReached
                                    ? CreateBrush(167, 172, 181)
                                    : CreateBrush(115, 122, 134),

                        FontWeight =
                            isCurrent
                                ? FontWeights.SemiBold
                                : FontWeights.Normal
                    });
            }

            ProgressionStepsItems.ItemsSource =
                rows;

            int nextIndex =
                currentIndex + 1;

            if (nextIndex < steps.Count)
            {
                decimal remainingProfit =
                    Math.Max(
                        0m,
                        steps[nextIndex].MinimumProfit -
                        totalProfit);

                ProgressionNextLevelPanel.Visibility =
                    Visibility.Visible;

                ProgressionRemainingProfitText.Text =
                    "You need " +
                    FormatMoney(remainingProfit) +
                    " more";

                ProgressionNextContractsText.Text =
                    "to unlock " +
                    FormatContracts(
                        steps[nextIndex].MaximumContracts);

                ProgressionMaxLevelText.Text =
                    "Account maximum: " +
                    FormatContracts(
                        state.MaximumContracts);

                return;
            }

            ProgressionNextLevelPanel.Visibility =
                Visibility.Collapsed;

            ProgressionRemainingProfitText.Text =
                string.Empty;

            ProgressionNextContractsText.Text =
                string.Empty;

            ProgressionMaxLevelText.Text =
                "Maximum progression level reached · " +
                "Account maximum: " +
                FormatContracts(
                    state.MaximumContracts);
        }

        private void ClearProgressionLadder(
            string message)
        {
            ProgressionCurrentLevelText.Text =
                "—";

            ProgressionCurrentContractsText.Text =
                "—";

            ProgressionStepsItems.ItemsSource =
                null;

            ProgressionNextLevelPanel.Visibility =
                Visibility.Collapsed;

            ProgressionRemainingProfitText.Text =
                string.Empty;

            ProgressionNextContractsText.Text =
                string.Empty;

            ProgressionMaxLevelText.Text =
                message;
        }

        private static string FormatContracts(
            int contracts)
        {
            return contracts == 1
                ? "1 Contract"
                : contracts.ToString(
                    CultureInfo.InvariantCulture) +
                  " Contracts";
        }

        private static string ToRomanNumeral(
            int value)
        {
            if (value <= 0)
            {
                return "—";
            }

            int[] values =
            {
                1000, 900, 500, 400,
                100, 90, 50, 40,
                10, 9, 5, 4, 1
            };

            string[] numerals =
            {
                "M", "CM", "D", "CD",
                "C", "XC", "L", "XL",
                "X", "IX", "V", "IV", "I"
            };

            System.Text.StringBuilder result =
                new System.Text.StringBuilder();

            for (int index = 0;
                 index < values.Length;
                 index++)
            {
                while (value >= values[index])
                {
                    result.Append(
                        numerals[index]);

                    value -=
                        values[index];
                }
            }

            return result.ToString();
        }


        /// <summary>
        /// Presenta las operaciones cerradas durante la jornada
        /// actual de la cuenta seleccionada.
        /// </summary>
        private void RenderTodayActivity()
        {
            if (_session == null)
            {
                ClearTodayActivity();

                return;
            }

            IReadOnlyList<NinjaTradeSnapshot> trades =
                _session.GetTodayClosedTrades();

            if (trades.Count == 0)
            {
                ClearTodayActivity();

                return;
            }

            List<TradeLogRow> rows =
                trades
                    .Select(
                        CreateTradeLogRow)
                    .ToList();

            TradeLogItems.ItemsSource =
                rows;

            EmptyTradeLogPanel.Visibility =
                Visibility.Collapsed;

            TradeLogPanel.Visibility =
                Visibility.Visible;

            int winningTrades =
                trades.Count(
                    trade => trade.RealizedPnL > 0m);

            decimal totalPnL =
                trades.Sum(
                    trade => trade.RealizedPnL);

            decimal winRate =
                trades.Count == 0
                    ? 0m
                    : (decimal)winningTrades /
                      trades.Count *
                      100m;

            TradeCountSummaryText.Text =
                $"Total Trades: {trades.Count}";

            WinRateSummaryText.Text =
                $"Win Rate: {winRate:0.0}%";

            TotalPnLSummaryText.Text =
                $"Total P&L: {FormatSignedMoney(totalPnL)}";

            TotalPnLSummaryText.Foreground =
                totalPnL > 0m
                    ? CreateBrush(16, 185, 129)
                    : totalPnL < 0m
                        ? CreateBrush(239, 68, 68)
                        : CreateBrush(115, 122, 134);
        }

        private void ClearTodayActivity()
        {
            TradeLogItems.ItemsSource =
                null;

            EmptyTradeLogPanel.Visibility =
                Visibility.Visible;

            TradeLogPanel.Visibility =
                Visibility.Collapsed;

            TradeCountSummaryText.Text =
                "Total Trades: 0";

            WinRateSummaryText.Text =
                "Win Rate: 0.0%";

            TotalPnLSummaryText.Text =
                "Total P&L: $0.00";

            TotalPnLSummaryText.Foreground =
                CreateBrush(115, 122, 134);
        }

        private static TradeLogRow CreateTradeLogRow(
            NinjaTradeSnapshot trade)
        {
            bool isLong =
                trade.Direction ==
                NinjaTradeDirection.Long;

            bool isWinning =
                trade.RealizedPnL > 0m;

            TimeSpan duration =
                trade.ExitTime -
                trade.EntryTime;

            return new TradeLogRow
            {
                Time =
                    trade.ExitTime.ToString(
                        "hh:mm:ss tt",
                        CultureInfo.GetCultureInfo(
                            "en-US")),

                Instrument =
                    trade.InstrumentName,

                Side =
                    isLong
                        ? "LONG"
                        : trade.Direction ==
                          NinjaTradeDirection.Short
                            ? "SHORT"
                            : "—",

                SideBackground =
                    isLong
                        ? CreateBrush(20, 68, 43)
                        : CreateBrush(73, 29, 31),

                SideForeground =
                    isLong
                        ? CreateBrush(74, 222, 128)
                        : CreateBrush(248, 113, 113),

                Contracts =
                    trade.Quantity.ToString(
                        CultureInfo.InvariantCulture),

                Entry =
                    trade.EntryPrice.ToString(
                        "N2",
                        CultureInfo.GetCultureInfo(
                            "en-US")),

                Exit =
                    trade.ExitPrice.ToString(
                        "N2",
                        CultureInfo.GetCultureInfo(
                            "en-US")),

                PnL =
                    FormatSignedMoney(
                        trade.RealizedPnL),

                PnLForeground =
                    trade.RealizedPnL > 0m
                        ? CreateBrush(74, 222, 128)
                        : trade.RealizedPnL < 0m
                            ? CreateBrush(248, 113, 113)
                            : CreateBrush(115, 122, 134),

                Duration =
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:00}:{1:00}:{2:00}",
                        (int)duration.TotalHours,
                        duration.Minutes,
                        duration.Seconds),

                Result =
                    isWinning
                        ? "WIN"
                        : trade.RealizedPnL < 0m
                            ? "LOSS"
                            : "FLAT",

                ResultBackground =
                    isWinning
                        ? CreateBrush(20, 68, 43)
                        : trade.RealizedPnL < 0m
                            ? CreateBrush(73, 29, 31)
                            : CreateBrush(42, 46, 53),

                ResultForeground =
                    isWinning
                        ? CreateBrush(74, 222, 128)
                        : trade.RealizedPnL < 0m
                            ? CreateBrush(248, 113, 113)
                            : CreateBrush(115, 122, 134)
            };
        }

        /// <summary>
        /// Actualiza el estado general de la cuenta
        /// y los colores de su indicador.
        /// </summary>
        private void RenderAccountStatus(
            string status)
        {
            string normalizedStatus =
                string.IsNullOrWhiteSpace(
                    status)
                    ? "UNKNOWN"
                    : status
                        .Trim()
                        .ToUpperInvariant();

            AccountStatusText.Text =
                normalizedStatus;

            switch (normalizedStatus)
            {
                case "ACTIVE":

                    AccountStatusBorder.Background =
                        CreateBrush(
                            6,
                            78,
                            59);

                    AccountStatusText.Foreground =
                        CreateBrush(
                            110,
                            231,
                            183);

                    break;

                case "PASSED":

                    AccountStatusBorder.Background =
                        CreateBrush(
                            30,
                            64,
                            175);

                    AccountStatusText.Foreground =
                        CreateBrush(
                            191,
                            219,
                            254);

                    break;

                case "FAILED":

                    AccountStatusBorder.Background =
                        CreateBrush(
                            127,
                            29,
                            29);

                    AccountStatusText.Foreground =
                        CreateBrush(
                            254,
                            202,
                            202);

                    break;

                case "LOCKED":

                    AccountStatusBorder.Background =
                        CreateBrush(
                            120,
                            53,
                            15);

                    AccountStatusText.Foreground =
                        CreateBrush(
                            253,
                            230,
                            138);

                    break;

                default:

                    AccountStatusBorder.Background =
                        CreateBrush(
                            55,
                            65,
                            81);

                    AccountStatusText.Foreground =
                        CreateBrush(
                            209,
                            213,
                            219);

                    break;
            }
        }

        /// <summary>
        /// Limpia la interfaz cuando todavía
        /// no existe una sesión conectada.
        /// </summary>
        private void ShowDisconnectedState()
        {
            AccountNameText.Text =
                "Sin cuenta conectada";

            NinjaAccountNameText.Text =
                "Sin cuenta NinjaTrader";

            BalanceText.Text =
                FormatMoney(
                    0m);

            FloorText.Text =
                FormatMoney(
                    0m);

            DailyPnLText.Text =
                FormatMoney(
                    0m);

            DailyPnLText.Foreground =
                CreateBrush(
                    209,
                    213,
                    219);

            DailyLossText.Text =
                "—";

            DailyLossText.Foreground =
                CreateBrush(
                    156,
                    163,
                    175);

            TradingDaysText.Text =
                "0 / 0";

            TradingDaysProgressBar.Maximum =
                1;

            TradingDaysProgressBar.Value =
                0;

            ProfitTargetText.Text =
                "$0 / $0";

            ProfitTargetProgressBar.Maximum =
                100;

            ProfitTargetProgressBar.Value =
                0;

            ConsistencyText.Text =
                "0.0%";

            ConsistencyText.Foreground =
                CreateBrush(
                    209,
                    213,
                    219);

            ConsistencyBestDayText.Text =
                "Best Day: $0.00";

            ConsistencyBestDayText.Foreground =
                CreateBrush(
                    209,
                    213,
                    219);

            ConsistencyRequirementText.Text =
                "Need: $0.00";

            ConsistencyRequirementText.Foreground =
                CreateBrush(
                    156,
                    163,
                    175);

            ConsistencyProgressBar.Minimum =
                0;

            ConsistencyProgressBar.Maximum =
                100;

            ConsistencyProgressBar.Value =
                0;

            ConsistencyProgressBar.Foreground =
                CreateBrush(
                    245,
                    158,
                    11);

            ClearProgressionLadder(
                "Disconnected · progression data unavailable");

            ClearTodayActivity();

            AccountStatusText.Text =
                "DISCONNECTED";

            AccountStatusBorder.Background =
                CreateBrush(
                    55,
                    65,
                    81);

            AccountStatusText.Foreground =
                CreateBrush(
                    209,
                    213,
                    219);
        }

        /// <summary>
        /// Actualiza periódicamente únicamente
        /// la cuenta que está seleccionada.
        /// </summary>
        private void OnRefreshTimerTick(
     object? sender,
     EventArgs e)
        {
            // Mantiene actualizado el Dashboard
            // de la cuenta actualmente seleccionada.
            RefreshDashboard();

            // Si todavía no existe un listener conectado,
            // no hay cuentas que buscar.
            if (_listener == null)
            {
                return;
            }

            // El timer principal corre cada 500 ms.
            // No queremos escanear todas las cuentas
            // de NinjaTrader cada medio segundo.
            _accountRefreshTickCounter++;

            // Esperamos 4 ciclos:
            // 500 ms x 4 = aproximadamente 2 segundos.
            if (_accountRefreshTickCounter < 4)
            {
                return;
            }

            // Reiniciamos el contador para comenzar
            // nuevamente otros 4 ciclos.
            _accountRefreshTickCounter = 0;

            try
            {
                // Aquí sí le pedimos al listener que revise
                // las cuentas actualmente visibles en NinjaTrader.
                //
                // Si encuentra una nueva SimMaC,
                // la registra y crea su NinjaAccountSession.
                int accountsAdded =
                    _listener.RefreshAccounts();

                // Si no apareció ninguna cuenta nueva,
                // no necesitamos reconstruir los selectores.
                if (accountsAdded <= 0)
                {
                    return;
                }

                // Obtenemos la lista ACTUALIZADA de cuentas
                // que MaC está supervisando.
                List<string> currentAccountNames =
                    _listener.AccountNames
                        .ToList();

                // Comparamos la lista actual contra la lista
                // que el Dashboard conocía anteriormente.
                //
                // Así identificamos específicamente
                // cuál cuenta acaba de aparecer.
                string? newAccountName =
                    currentAccountNames
                        .FirstOrDefault(
                            accountName =>
                                !_knownAccountNames.Contains(
                                    accountName,
                                    StringComparer.OrdinalIgnoreCase));

                // Actualiza la lista de cuentas del host.
                LoadAccounts();

                // Si logramos identificar la cuenta nueva,
                // la seleccionamos automáticamente.
                if (!string.IsNullOrWhiteSpace(
                        newAccountName))
                {
                    // Selector antiguo del host.
                    // Lo mantenemos sincronizado porque todavía
                    // participa en la arquitectura actual.
                    AccountSelector.SelectedItem =
                        newAccountName;

                    // Actualizamos el selector REAL
                    // que estás viendo en DashboardV2.
                    PremiumDashboard.SetAccounts(
                        currentAccountNames,
                        newAccountName);

                    // Conectamos el Dashboard con la sesión
                    // correspondiente a esa nueva cuenta.
                    SelectSession(
                        newAccountName);
                }
            }
            catch (Exception exception)
            {
                // Si NinjaTrader está actualizando sus cuentas
                // justo en ese instante, evitamos que un error
                // del auto-refresh rompa el Dashboard.
                System.Diagnostics.Debug.WriteLine(
                    "[MaCProFundingWindow] " +
                    "Error en auto-refresh de cuentas: " +
                    exception);
            }
        }

        /// <summary>
        /// Libera referencias y detiene el temporizador
        /// cuando la ventana se cierra.
        /// </summary>
        private void OnWindowClosed(
            object? sender,
            EventArgs e)
        {
            _refreshTimer.Stop();

            _refreshTimer.Tick -=
                OnRefreshTimerTick;

            Closed -=
                OnWindowClosed;

            _session =
                null;

            _listener =
                null;
        }

        private static string FormatMoney(
            decimal value)
        {
            return value.ToString(
                "C2",
                CultureInfo.GetCultureInfo(
                    "en-US"));
        }

        private static string FormatMoneyWithoutDecimals(
            decimal value)
        {
            return value.ToString(
                "C0",
                CultureInfo.GetCultureInfo(
                    "en-US"));
        }

        private static string FormatSignedMoney(
            decimal value)
        {
            if (value > 0m)
            {
                return
                    "+" +
                    FormatMoney(
                        value);
            }

            return FormatMoney(
                value);
        }


        private sealed class ProgressionStepRow
        {
            public string ProfitText { get; set; } =
                string.Empty;

            public string ContractsText { get; set; } =
                string.Empty;

            public string IndicatorText { get; set; } =
                string.Empty;

            public Brush IndicatorBackground { get; set; } =
                Brushes.Transparent;

            public Brush RowBackground { get; set; } =
                Brushes.Transparent;

            public Brush PrimaryForeground { get; set; } =
                Brushes.White;

            public Brush SecondaryForeground { get; set; } =
                Brushes.White;

            public FontWeight FontWeight { get; set; } =
                FontWeights.Normal;
        }

        private sealed class TradeLogRow
        {
            public string Time { get; set; } =
                string.Empty;

            public string Instrument { get; set; } =
                string.Empty;

            public string Side { get; set; } =
                string.Empty;

            public Brush SideBackground { get; set; } =
                Brushes.Transparent;

            public Brush SideForeground { get; set; } =
                Brushes.Black;

            public string Contracts { get; set; } =
                string.Empty;

            public string Entry { get; set; } =
                string.Empty;

            public string Exit { get; set; } =
                string.Empty;

            public string PnL { get; set; } =
                string.Empty;

            public Brush PnLForeground { get; set; } =
                Brushes.Black;

            public string Duration { get; set; } =
                string.Empty;

            public string Result { get; set; } =
                string.Empty;

            public Brush ResultBackground { get; set; } =
                Brushes.Transparent;

            public Brush ResultForeground { get; set; } =
                Brushes.Black;
        }

        private static SolidColorBrush CreateBrush(
            byte red,
            byte green,
            byte blue)
        {
            return new SolidColorBrush(
                Color.FromRgb(
                    red,
                    green,
                    blue));
        }
        // Ajusta la ventana al area disponible sin maximizar ni guardar
        // dimensiones del equipo de desarrollo.
        private void OnCompactWindowLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnCompactWindowLoaded;
            var workArea = SystemParameters.WorkArea;
            Width = Math.Min(1060d, Math.Max(600d, workArea.Width - 48d));
            Height = Math.Min(740d, Math.Max(450d, workArea.Height - 48d));
            MinWidth = Math.Min(760d, Width);
            MinHeight = Math.Min(520d, Height);
            Left = workArea.Left + (workArea.Width - Width) / 2d;
            Top = workArea.Top + (workArea.Height - Height) / 2d;
        }

    }
}