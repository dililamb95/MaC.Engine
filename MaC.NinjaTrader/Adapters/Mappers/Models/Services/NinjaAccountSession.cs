using MaC.Core.Enums;
using MaC.Core.Evaluations;
using MaC.Core.Models;
using MaC.Core.Summaries;
using MaC.NinjaTrader.Guardian;
using MaC.NinjaTrader.Models;
using MaC.NinjaTrader.Risk;
using MaC.NinjaTrader.Security;
using NinjaTrader.Cbi;
using MaC.Core.Services.Trading;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CoreAccountStatus =
    MaC.Core.Enums.AccountStatus;
using CoreTradingContext =
    MaC.Core.Models.TradingContext;
using NinjaAccount =
    NinjaTrader.Cbi.Account;

namespace MaC.NinjaTrader.Services;

public class NinjaAccountSession : IDisposable
{
    private const int RiskMonitorIntervalMilliseconds =
        250;

    private readonly object _syncRoot =
        new object();

    private readonly SessionLifecycleManager
        _lifecycleManager;

    private readonly NinjaTodayActivityStorage
        _todayActivityStorage =
            new NinjaTodayActivityStorage();

    private readonly string
        _todayActivityFilePath;

    private readonly NinjaAccountResetDetector
        _resetDetector;

    private readonly string
        _securityAuditFilePath;

    private readonly NinjaGuardianObserver
        _guardianObserver;

    private readonly NinjaTradeProcessor
        _tradeProcessor =
            new NinjaTradeProcessor();

    private MaCCloudTradeClient?
        _cloudTradeClient;

    private readonly MaCCloudOutbox?
    _cloudOutbox;

    private readonly HashSet<string>
        _processedExecutionIds =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

    private readonly Queue<NinjaTradeSnapshot>
        _pendingClosedTrades =
            new Queue<NinjaTradeSnapshot>();

    private readonly List<NinjaTradeSnapshot>
        _closedTradesToday =
            new List<NinjaTradeSnapshot>();

    private DateTime _closedTradesDate =
        TradingSessionClock.GetCurrentTradingDate();
    private readonly Timer _riskMonitor;

    private DateTime _lastKnownTradingDate;

    private decimal _lastCashValue;

    private bool _isDisposed;

    private bool _isRiskCheckRunning;

    private static readonly TimeSpan CloudHeartbeatInterval =
    TimeSpan.FromMinutes(2);

    private DateTime _lastCloudHeartbeatUtc =
        DateTime.MinValue;

    private static readonly TimeSpan CloudOutboxRetryInterval =
    TimeSpan.FromSeconds(15);

    private DateTime _lastCloudOutboxRetryUtc =
        DateTime.MinValue;

    private int _isCloudOutboxRetryRunning;

    private static readonly TimeSpan CloudBindingRetryInterval =
    TimeSpan.FromSeconds(30);

    private DateTime _lastCloudBindingRetryUtc =
        DateTime.MinValue;

    private int _isCloudBindingRetryRunning;

    private static readonly TimeSpan CloudBindingNotFoundRetryInterval =
    TimeSpan.FromMinutes(10);

    private DateTime _cloudBindingNotFoundRetryAfterUtc =
        DateTime.MinValue;

    /*
 * Indica que la evaluación ya quedó FAILED
 * mientras todavía existía una posición abierta.
 *
 * MaC continuará observando exclusivamente
 * el cierre de esa posición para registrar
 * su liquidación financiera final.
 */
    private bool _isAwaitingTerminalSettlement;

    private readonly string _terminalSettlementFilePath;

    private readonly Guid _terminalSettlementAccountId;

    private decimal _terminalSettlementCashValueBaseline;

    private readonly NinjaTerminalSettlementStorage _terminalSettlementStorage =
        new NinjaTerminalSettlementStorage();

    private NinjaTerminalSettlementState? _terminalSettlementState;
    private DateTime _terminalFlatObservedUtc = DateTime.MinValue;
    private DateTime _terminalLastCashChangeUtc = DateTime.MinValue;
    private bool _terminalExitObserved;

    // Give NinjaTrader time to post CashValue after the last ATM fill.
    private static readonly TimeSpan TerminalPostingGrace = TimeSpan.FromSeconds(3);


    /*
     * Infracción contractual detectada mientras
     * existe una posición abierta.
     *
     * No se aplica oficialmente hasta que la
     * operación haya cerrado y su resultado
     * haya sido procesado.
     */
    private bool _hasPendingContractFailure;

    private RuleType _pendingContractFailureRule =
        RuleType.MaxContracts;

    private string _pendingContractFailureReason =
        string.Empty;

    public NinjaAccount NinjaAccount { get; }

    public CoreTradingContext Context =>
        _lifecycleManager.Context;

    public NinjaAccountSession(
      NinjaAccount ninjaAccount)
      : this(
          ninjaAccount,
          null)
    {
    }

    public NinjaAccountSession(
        NinjaAccount ninjaAccount,
        MaCCloudOptions? cloudOptions)
    {
        NinjaAccount =
            ninjaAccount
            ?? throw new ArgumentNullException(
                nameof(ninjaAccount));

        /*
         * MaC Cloud es opcional.
         *
         * La ausencia de configuración Cloud
         * NUNCA debe impedir que MaC Engine,
         * Guardian, Risk Monitor o Dashboard
         * funcionen localmente.
         */
        _cloudOutbox =
        new MaCCloudOutbox(
            NinjaAccount.Name);

        if (cloudOptions != null &&
            !string.IsNullOrWhiteSpace(
                cloudOptions.AccountBindingToken))
        {
            _cloudTradeClient =
                new MaCCloudTradeClient(
        cloudOptions.BaseUrl,
        cloudOptions.ApiKey,
        cloudOptions.AccountBindingToken,
        cloudOptions.InstallationCredential,
        cloudOptions.InstallationId);
        }
        else if (cloudOptions != null)
        {
            WriteOutput(
                $"MaC Cloud | Binding no configurado para la cuenta | " +
                $"Cuenta: {NinjaAccount.Name} | " +
                $"La sesión local continuará normalmente.");
        }
        /*
         * Desde aquí en adelante toda la sesión
         * local se inicializa independientemente
         * de MaC Cloud.
         */
        AccountType accountType =
            ResolveAccountType(
                NinjaAccount);

        _lifecycleManager =
            new SessionLifecycleManager(
                NinjaAccount,
                accountType);

        _terminalSettlementAccountId =
    Context.Account.Id;

        _todayActivityFilePath =
            CreateTodayActivityFilePath(
                _lifecycleManager.StateFilePath);

        _terminalSettlementFilePath =
    Path.Combine(
        Path.GetDirectoryName(
            _lifecycleManager.StateFilePath)
            ?? string.Empty,
        Path.GetFileNameWithoutExtension(
            _lifecycleManager.StateFilePath)
        + ".terminal-settlement.json");

        _securityAuditFilePath =
            CreateSecurityAuditFilePath(
                _lifecycleManager.StateFilePath);

        _resetDetector =
            new NinjaAccountResetDetector(
                NinjaAccount.Name,
                _securityAuditFilePath);

        _guardianObserver =
            new NinjaGuardianObserver(
                NinjaAccount.Name,
                GuardianFilePaths.CreateBaselineFilePath(
                    _lifecycleManager.StateFilePath));

        RestoreTodayActivity();

        _lastCashValue =
      Convert.ToDecimal(
          NinjaAccount.Get(
              AccountItem.CashValue,
              Currency.UsDollar));

        _terminalSettlementCashValueBaseline =
    _lastCashValue;

        RestoreTerminalSettlement();

        _lastKnownTradingDate =
            Context.TradingDay.Date;

        _riskMonitor =
            new Timer(
                MonitorRisk,
                null,
                RiskMonitorIntervalMilliseconds,
                RiskMonitorIntervalMilliseconds);

        if (Context.Account.Status ==
            CoreAccountStatus.Failed)
        {
            WriteOutput(
                $"MaC | Evaluación fallida restaurada | " +
                $"Cuenta: {NinjaAccount.Name} | " +
                $"Regla: " +
                $"{Context.Account.FailedRule?.ToString() ?? "No informada"} | " +
                $"Motivo: {Context.Account.FailureReason}");
        }

        WriteOutput(
            $"MaC | Sesión creada | " +
            $"Cuenta: {NinjaAccount.Name} | " +
            $"Tipo: {Context.Account.Type} | " +
            $"Evaluación: {Context.Account.Name} | " +
            $"Balance cerrado: {_lastCashValue:F2} | " +
            $"PnL flotante: {Context.UnrealizedPnL:F2} | " +
            $"Estado restaurado: " +
            $"{_lifecycleManager.WasStateRestored}");

        IReadOnlyList<TradeHistory> persistedHistory =
            GetTradeHistory();

        WriteOutput(
            $"MaC | Historial persistido cargado | " +
            $"Cuenta: {NinjaAccount.Name} | " +
            $"Registros: {persistedHistory.Count}");

        try
        {
            if (!_guardianObserver.HasBaseline(
                    Context.Account.Id))
            {
                _guardianObserver.AcceptCurrentState(
                    Context.Account,
                    persistedHistory.Count,
                    "Initial session state accepted " +
                    "as Guardian baseline.");
            }
            else
            {
                var assessment =
                    _guardianObserver.Observe(
                        Context.Account,
                        persistedHistory.Count,
                        "Session startup");

                WriteOutput(
                    "[GUARDIAN] Startup assessment | " +
                    $"Account: {NinjaAccount.Name} | " +
                    $"Tamper: {assessment.IsTamperDetected} | " +
                    $"Review: {assessment.RequiresReview}");

                if (assessment.IsTamperDetected)
                {
                    string reason =
                        "MaC Guardian detected an unauthorized " +
                        "modification of the evaluation state.";

                    _lifecycleManager.FailAccount(
                        RuleType.GuardianIntegrityViolation,
                        reason);

                    WriteOutput(
                        "[GUARDIAN] ACCOUNT FAILED | " +
                        $"Account: {NinjaAccount.Name} | " +
                        $"Rule: " +
                        $"{RuleType.GuardianIntegrityViolation} | " +
                        $"Reason: {reason}");
                }
            }
        }
        catch (Exception exception)
        {
            WriteOutput(
                "[GUARDIAN] Startup inspection error | " +
                $"Account: {NinjaAccount.Name} | " +
                exception);
        }

        /*
         * 8C — STARTUP CLOUD SNAPSHOT
         *
         * La sesión local ya fue restaurada y Guardian
         * terminó su inspección inicial.
         *
         * Enviamos ahora una fotografía completa del
         * estado oficial de la evaluación a MaCPro Cloud.
         *
         * Este envío no modifica reglas ni decisiones
         * del Engine y nunca debe bloquear el arranque
         * de NinjaTrader.
         */
        EvaluationState startupCloudState =
            EvaluationStateFactory.Create(
                Context);

        QueueCloudSnapshotSend(
            startupCloudState);
        TryProcessCloudOutbox();
        _lastCloudHeartbeatUtc =
    DateTime.UtcNow;

        WriteOutput(
            $"MaC Cloud | Snapshot inicial programado | " +
            $"Cuenta: {NinjaAccount.Name} | " +
            $"Estado: {startupCloudState.Status} | " +
            $"Balance: {startupCloudState.CurrentBalance:F2}");
    }


    /// <summary>
    /// Retorna una fotografía consistente
    /// para el Dashboard.
    /// </summary>
    public EvaluationState GetEvaluationState()
    {
        lock (_syncRoot)
        {
            return EvaluationStateFactory.Create(
                Context);
        }
    }

    /// <summary>
    /// Retorna una copia segura de las operaciones
    /// cerradas durante la jornada actual.
    /// </summary>
    public IReadOnlyList<NinjaTradeSnapshot>
        GetTodayClosedTrades()
    {
        lock (_syncRoot)
        {
            EnsureClosedTradesDate();

            return _closedTradesToday
                .OrderByDescending(
                    trade => trade.ExitTime)
                .ToList()
                .AsReadOnly();
        }
    }

    /// <summary>
    /// Retorna una copia segura del historial
    /// persistido de operaciones.
    /// </summary>
    public IReadOnlyList<TradeHistory>
        GetTradeHistory()
    {
        lock (_syncRoot)
        {
            return _lifecycleManager
                .GetTradeHistory();
        }
    }

    public void ProcessExecution(
        ExecutionEventArgs e)
    {
        if (e == null)
        {
            throw new ArgumentNullException(
                nameof(e));
        }

        lock (_syncRoot)
        {
            if (_isDisposed ||
                e.Operation != Operation.Add)
            {
                return;
            }

            /*
             * Una evaluación FAILED ya terminó oficialmente.
             *
             * Las ejecuciones posteriores pertenecen
             * exclusivamente a NinjaTrader.
             *
             * MaC no las registra, no las valida,
             * no las cancela y nunca solicita Flatten.
             */

            if (Context.Account.Status ==
       CoreAccountStatus.Failed)
            {
                /*
                 * Una cuenta FAILED no acepta nuevas entradas.
                 *
                 * Solamente dejamos pasar la salida de la
                 * posición que ya estaba abierta cuando
                 * ocurrió el breach.
                 */
                if (!_isAwaitingTerminalSettlement ||
                    !e.Execution.IsExit)
                {
                    return;
                }
            }
            string executionId =
                CreateExecutionId(
                    e);

            if (!_processedExecutionIds.Add(
                    executionId))
            {
                return;
            }

            string instrumentName =
                e.Execution.Instrument?.FullName
                ?? string.Empty;

            if (string.IsNullOrWhiteSpace(
                    instrumentName) ||
                e.Quantity <= 0)
            {
                return;
            }

            if (e.Execution.IsEntry)
            {
                ProcessEntry(
                    e,
                    executionId,
                    instrumentName);
            }

            if (e.Execution.IsExit)
            {
                if (_isAwaitingTerminalSettlement)
                {
                    _terminalExitObserved = true;
                    _terminalFlatObservedUtc = DateTime.MinValue;
                }
                ProcessExit(
                    e,
                    executionId,
                    instrumentName);
            }

            WriteOutput(
                $"MaC | Ejecución registrada | " +
                $"Cuenta: {NinjaAccount.Name} | " +
                $"ExecutionId: {executionId} | " +
                $"Instrumento: {instrumentName} | " +
                $"Cantidad: {e.Quantity} | " +
                $"Precio: {e.Price}");
        }
    }

    public void ProcessPosition(
        PositionEventArgs e)
    {
        if (e == null)
        {
            throw new ArgumentNullException(
                nameof(e));
        }

        lock (_syncRoot)
        {
            if (_isDisposed)
            {
                return;
            }

            /*
             * Este método es únicamente informativo.
             *
             * MaC no cancela órdenes,
             * no cierra posiciones
             * y no envía Flatten.
             */
            WriteOutput(
                $"MaC | Posición actualizada | " +
                $"Cuenta: {NinjaAccount.Name} | " +
                $"Instrumento: " +
                $"{e.Position.Instrument?.FullName} | " +
                $"Posición: {e.MarketPosition} | " +
                $"Cantidad: {e.Quantity}");
        }
    }

    public void ProcessAccountItem(
        AccountItemEventArgs e)
    {
        if (e == null)
        {
            throw new ArgumentNullException(
                nameof(e));
        }

        if (e.AccountItem ==
      AccountItem.UnrealizedProfitLoss)
        {
            bool hasOpenPosition =
                NinjaAccount.Positions.Any(
                    position =>
                        position.MarketPosition !=
                            MarketPosition.Flat &&
                        position.Quantity > 0);

            decimal effectiveUnrealizedPnL =
                NinjaRiskPnLResolver.ResolveUnrealizedPnL(
                    hasOpenPosition,
                    Convert.ToDecimal(
                        e.Value));

            ProcessUnrealizedProfitLoss(
                effectiveUnrealizedPnL);

            return;
        }

        if (e.AccountItem ==
            AccountItem.CashValue)
        {
            ProcessCashValue(
                Convert.ToDecimal(
                    e.Value));
        }
    }

    /// <summary>
    /// Detecta el cambio oficial de Trading Day.
    ///
    /// Cuando cambia la jornada:
    ///
    /// 1. Cierra oficialmente la jornada anterior.
    /// 2. Consolida High Water Mark, Best Day y EOD Floor.
    /// 3. Crea/sincroniza la nueva jornada.
    /// 4. Reinicia Today's Activity.
    ///
    /// No cancela órdenes.
    /// No envía Flatten.
    /// No administra posiciones.
    /// </summary>
    private void EnsureTradingDayTransition()
    {
        DateTime currentTradingDate =
            TradingSessionClock
                .GetCurrentTradingDate();

        bool tradingDayChanged =
            TradingDayTransitionDetector
                .HasTradingDayChanged(
                    _lastKnownTradingDate,
                    currentTradingDate);

        if (!tradingDayChanged)
        {
            return;
        }

        DateTime previousTradingDate =
            _lastKnownTradingDate;

        /*
         * Una evaluación que ya terminó no debe
         * reabrirse debido a un cambio de jornada.
         */
        if (Context.Account.Status ==
                CoreAccountStatus.Failed ||
            Context.Account.Status ==
                CoreAccountStatus.Passed ||
            Context.Account.Status ==
                CoreAccountStatus.Suspended)
        {
            _lastKnownTradingDate =
                currentTradingDate;

            WriteOutput(
                $"MaC | Cambio de Trading Day detectado " +
                $"en evaluación terminada | " +
                $"Cuenta: {NinjaAccount.Name} | " +
                $"Anterior: " +
                $"{previousTradingDate:yyyy-MM-dd} | " +
                $"Actual: " +
                $"{currentTradingDate:yyyy-MM-dd} | " +
                $"Estado: {Context.Account.Status}");

            return;
        }

        /*
         * Cierre oficial de la jornada anterior.
         *
         * AccountEngine consolidará:
         * - High Water Mark
         * - Best Day
         * - EOD Floor
         * - reglas financieras
         * - requisitos de calificación
         */
        _lifecycleManager.CloseTradingDay(
    previousTradingDate);

        _lastKnownTradingDate =
            currentTradingDate;

        /*
         * El propio cierre EOD puede haber marcado
         * la evaluación como FAILED.
         *
         * Si eso ocurrió, no se crea una nueva
         * jornada de evaluación.
         */
        if (Context.Account.Status ==
                CoreAccountStatus.Failed ||
            Context.Account.Status ==
                CoreAccountStatus.Passed ||
            Context.Account.Status ==
                CoreAccountStatus.Suspended)
        {
            WriteOutput(
                $"MaC | Trading Day cerrado sin nueva jornada | " +
                $"Cuenta: {NinjaAccount.Name} | " +
                $"Cerrado: {previousTradingDate:yyyy-MM-dd} | " +
                $"Estado: {Context.Account.Status} | " +
                $"Regla: " +
                $"{Context.Account.FailedRule?.ToString() ?? "Ninguna"}");

            return;
        }

        _lifecycleManager.SynchronizeBalance();

        EnsureClosedTradesDate();

    }

    private void MonitorRisk(
        object? state)
    {
        lock (_syncRoot)
        {
            if (_isDisposed ||
                _isRiskCheckRunning)
            {
                return;
            }

            _isRiskCheckRunning =
                true;
        }

        try
        {
            EnsureTradingDayTransition();

            double cashValue =
                NinjaAccount.Get(
                            AccountItem.CashValue,
                    Currency.UsDollar);

            ProcessCashValue(
                Convert.ToDecimal(
                    cashValue));

            double reportedUnrealizedProfitLoss =
        NinjaAccount.Get(
            AccountItem.UnrealizedProfitLoss,
            Currency.UsDollar);

            bool hasOpenPosition =
                NinjaAccount.Positions.Any(
                    position =>
                        position.MarketPosition !=
                            MarketPosition.Flat &&
                        position.Quantity > 0);
            decimal effectiveUnrealizedPnL =
                NinjaRiskPnLResolver.ResolveUnrealizedPnL(
                    hasOpenPosition,
                    Convert.ToDecimal(
                        reportedUnrealizedProfitLoss));
            ProcessUnrealizedProfitLoss(
                effectiveUnrealizedPnL);
            TryEnsureCloudBinding();
            TryQueueCloudHeartbeat();
            TryProcessCloudOutbox();
        }
        catch (ObjectDisposedException)
        {
            // La sesión está terminando.
        }
        catch (Exception exception)
        {
            WriteOutput(
                $"MaC | Error en monitor de riesgo | " +
                $"Cuenta: {NinjaAccount.Name} | " +
                $"{exception.Message}");
        }
        finally
        {
            lock (_syncRoot)
            {
                _isRiskCheckRunning =
                    false;
            }
        }
    }

    private void ProcessUnrealizedProfitLoss(
        decimal unrealizedPnL)
    {
        lock (_syncRoot)
        {
            if (_isDisposed)
            {
                return;
            }

            /*
             * Una evaluación FAILED queda congelada.
             *
             * El PnL de operaciones posteriores no modifica
             * el contexto, el Dashboard ni el estado persistido.
             */
            if (Context.Account.Status ==
                CoreAccountStatus.Failed)
            {
                return;
            }

            var result =
                _lifecycleManager
                    .ProcessUnrealizedPnL(
                        unrealizedPnL);

            if (!result.IsAccountFailed &&
                Context.Account.Status !=
                    CoreAccountStatus.Failed)
            {
                return;
            }

            string reason =
                string.IsNullOrWhiteSpace(
                    result.Reason)
                    ? "La evaluación se encuentra fallida."
                    : result.Reason;

            /*
             * El Engine ya marcó y persistió el FAILED.
             *
             * No se cancela ninguna orden.
             * No se envía Flatten.
             * NinjaTrader conserva el control normal.
             */

            /*
 * Si el fallo ocurrió mientras todavía existe
 * una posición abierta, esperamos únicamente
 * su liquidación final.
 */
            bool terminalOpen = HasOpenPosition();
            if (terminalOpen)
            {
                BeginTerminalSettlement();
            }
            AcceptGuardianBaseline("Official risk failure recorded by MaC Engine.");
            WriteOutput(
                $"MaC | FALLO DETECTADO EN TIEMPO REAL | " +
                $"Cuenta: {NinjaAccount.Name} | " +
                $"Regla: " +
                $"{result.RuleTriggered?.ToString() ?? "No informada"} | " +
                $"PnL realizado: " +
                $"{Context.RealizedDailyPnL:F2} | " +
                $"PnL flotante: " +
                $"{Context.UnrealizedPnL:F2} | " +
                $"PnL efectivo: " +
                $"{Context.EffectiveDailyPnL:F2} | " +
                $"Motivo: {reason}");

            /*
 * 8C — CRITICAL STATUS SNAPSHOT
 *
 * La evaluación acaba de cambiar oficialmente
 * a FAILED.
 *
 * Cloud debe conocer este estado inmediatamente,
 * incluso si todavía existe una posición abierta.
 */
            EvaluationState failedCloudState =
                EvaluationStateFactory.Create(
                    Context);

            QueueCloudSnapshotSend(
                failedCloudState);

            WriteOutput(
                $"MaC Cloud | Snapshot crítico programado | " +
                $"Cuenta: {NinjaAccount.Name} | " +
                $"Estado: {failedCloudState.Status} | " +
                $"Regla: " +
                $"{failedCloudState.FailedRule?.ToString() ?? "No informada"}");
        }
    }
    private void ProcessCashValue(
       decimal updatedBalance)
    {
        lock (_syncRoot)
        {
            if (_isDisposed)
            {
                return;
            }

            /*
             * Una evaluación FAILED queda congelada.
             *
             * Los cambios posteriores de CashValue no deben
             * modificar balance, historial, High Water Mark,
             * Floor ni estadísticas de la evaluación terminada.
             *
             * NinjaTrader puede continuar funcionando normalmente,
             * pero esa actividad ya no pertenece a la evaluación.
             */
            if (Context.Account.Status ==
      CoreAccountStatus.Failed)
            {
                /*
                 * Una evaluación FAILED es irreversible.
                 *
                 * No volvemos a ejecutar AccountEngine,
                 * RuleEngine ni ProcessTrade.
                 *
                 * Únicamente terminamos de observar la posición
                 * que estaba abierta cuando ocurrió el breach.
                 */
                if (!_isAwaitingTerminalSettlement ||
                    _terminalSettlementState == null ||
                    _terminalSettlementState.IsSettlementApplied)
                {
                    return;
                }

                // Observe cash movement, but do not mistake a flat position
                // for proof that NinjaTrader has posted the final ATM fill.
                if (updatedBalance != _lastCashValue)
                {
                    _lastCashValue = updatedBalance;
                    _terminalLastCashChangeUtc = DateTime.UtcNow;
                    _terminalFlatObservedUtc = DateTime.MinValue;
                }

                if (HasOpenPosition())
                {
                    _terminalFlatObservedUtc = DateTime.MinValue;
                    return;
                }

                DateTime nowUtc = DateTime.UtcNow;
                if (_terminalFlatObservedUtc == DateTime.MinValue)
                {
                    _terminalFlatObservedUtc = nowUtc;
                    return;
                }

                // After restart the in-memory execution tracker is empty.
                // Require a post-breach cash change or an observed exit;
                // otherwise leave the record pending for reconciliation.
                bool observedSettlementEvidence =
                    _terminalExitObserved ||
                    updatedBalance != _terminalSettlementState.CashValueBaseline;

                if (!observedSettlementEvidence ||
                    nowUtc - _terminalFlatObservedUtc < TerminalPostingGrace ||
                    (_terminalLastCashChangeUtc != DateTime.MinValue &&
                     nowUtc - _terminalLastCashChangeUtc < TerminalPostingGrace))
                {
                    return;
                }

                decimal finalBalance =
                    _terminalSettlementState.EvaluationBalanceAtFailure +
                    (updatedBalance - _terminalSettlementState.CashValueBaseline);

                // Idempotent recovery: the main evaluation may have been
                // saved before the auxiliary settlement marker was saved.
                if (Context.Account.ClosedBalance != finalBalance)
                {
                    _lifecycleManager.ApplyTerminalSettlement(finalBalance);
                }

                // Terminal history is distinct from ProcessTrade: the latter
                // would re-enter AccountEngine after the evaluation FAILED.
                // CashValue gives the net change for the entire terminal close.
                // When there are several snapshots, derive individual gross
                // PnL from execution prices and distribute the aggregate
                // commission/fee difference proportionally to quantity.
                var terminalSnapshots = _pendingClosedTrades.ToList();
                if (terminalSnapshots.Count > 0)
                {
                    decimal cashChange = updatedBalance -
                        _terminalSettlementState.CashValueBaseline;
                    decimal totalGross = terminalSnapshots.Sum(t =>
                        (t.Direction == NinjaTradeDirection.Long ? 1m : -1m) *
                        (t.ExitPrice - t.EntryPrice) * t.Quantity * t.PointValue);
                    decimal totalQuantity = terminalSnapshots.Sum(t => (decimal)t.Quantity);
                    decimal netAdjustment = cashChange - totalGross;
                    decimal assignedNet = 0m;
                    for (int i = 0; i < terminalSnapshots.Count; i++)
                    {
                        NinjaTradeSnapshot pending = terminalSnapshots[i];
                        decimal gross =
                            (pending.Direction == NinjaTradeDirection.Long ? 1m : -1m) *
                            (pending.ExitPrice - pending.EntryPrice) *
                            pending.Quantity * pending.PointValue;
                        decimal net = i == terminalSnapshots.Count - 1
                            ? cashChange - assignedNet
                            : gross + (totalQuantity == 0m ? 0m :
                                netAdjustment * pending.Quantity / totalQuantity);
                        assignedNet += net;
                        NinjaTradeSnapshot completed = _tradeProcessor.CreateSnapshot(
                            pending.ExternalTradeId,
                            pending.AccountName,
                            pending.InstrumentName,
                            pending.Direction,
                            pending.Quantity,
                            pending.EntryPrice,
                            pending.ExitPrice,
                            net,
                            pending.EntryTime,
                            pending.ExitTime,
                            pending.PointValue);
                        _lifecycleManager.RecordTerminalTrade(completed);
                        EnsureClosedTradesDate();
                        if (!_closedTradesToday.Any(t =>
                            string.Equals(t.ExternalTradeId, completed.ExternalTradeId,
                                StringComparison.OrdinalIgnoreCase)))
                            _closedTradesToday.Add(completed);
                    }
                    SaveTodayActivity();
                }
                else
                {
                    WriteOutput("MaC | ADVERTENCIA: balance terminal conciliado " +
                        "sin snapshots de ejecución. Historial pendiente de " +
                        "reconciliación; no se fabrican operaciones.");
                }

                _terminalSettlementState.IsSettlementApplied = true;
                _terminalSettlementState.FinalCashValue = updatedBalance;
                _terminalSettlementStorage.Save(
                    _terminalSettlementFilePath, _terminalSettlementState);

                _pendingClosedTrades.Clear();
                _isAwaitingTerminalSettlement = false;
                AcceptGuardianBaseline("Terminal settlement after failed evaluation.");

                WriteOutput(
                    $"MaC | LIQUIDACIÓN TERMINAL COMPLETADA | " +
                    $"Cuenta: {NinjaAccount.Name} | " +
                    $"CashValue final: {updatedBalance:F2} | " +
                    $"Balance final: {Context.Account.ClosedBalance:F2} | " +
                    $"Estado: {Context.Account.Status}");

                QueueCloudSnapshotSend(EvaluationStateFactory.Create(Context));
                return;
            }

            /*
             * Antes de sincronizar el balance,
             * comprobamos si NinjaTrader lo devolvió
             * sospechosamente al balance inicial.
             *
             * Esta verificación ocurre antes de modificar
             * el estado persistido de la evaluación.
             */
            bool resetDetected =
                _resetDetector.Evaluate(
                    _lastCashValue,
                    updatedBalance,
                    Context.Account.ClosedBalance,
                    Context.Account.StartingBalance,
                    Context.Account.Status,
                    _pendingClosedTrades.Count);

            if (resetDetected)
            {
                /*
                 * Actualizamos solamente el último valor
                 * observado para evitar repetir la alerta
                 * cada 250 milisegundos.
                 *
                 * No llamamos SynchronizeBalance().
                 * El estado persistido de MaC permanece intacto.
                 */
                _lastCashValue =
                    updatedBalance;

                WriteOutput(
                    "MaC Guardian | BALANCE RESET DETECTED | " +
                    $"Cuenta: {NinjaAccount.Name} | " +
                    $"Balance anterior observado: " +
                    $"{Context.Account.ClosedBalance:F2} | " +
                    $"Balance reportado por NinjaTrader: " +
                    $"{updatedBalance:F2} | " +
                    $"Balance inicial: " +
                    $"{Context.Account.StartingBalance:F2} | " +
                    $"Estado preservado: " +
                    $"{Context.Account.Status}");

                ShowResetDetectedMessage();

                return;
            }


            if (_pendingClosedTrades.Count > 0 &&
                updatedBalance != _lastCashValue)
            {
                int openPositionCount =
                    NinjaAccount.Positions.Count(
                        p => p.MarketPosition != MarketPosition.Flat &&
                             p.Quantity > 0);

                WriteOutput(
                    $"MaC DIAGNOSTICO | CashValue observado | " +
                    $"Cuenta: {NinjaAccount.Name} | " +
                    $"Anterior: {_lastCashValue:F2} | " +
                    $"Actual: {updatedBalance:F2} | " +
                    $"Diferencia: {updatedBalance - _lastCashValue:F2} | " +
                    $"Salidas pendientes: {_pendingClosedTrades.Count} | " +
                    $"Posiciones abiertas: {openPositionCount}");
            }


            if (updatedBalance ==
                _lastCashValue)
            {
                return;
            }

            /*
             * Si no existe una operación cerrada pendiente,
             * únicamente sincronizamos el balance oficial.
             */
            if (_pendingClosedTrades.Count == 0)
            {
                _lastCashValue =
                    updatedBalance;

                _lifecycleManager
                    .SynchronizeBalance();

                WriteOutput(
                    $"MaC | CashValue sincronizado | " +
                    $"Cuenta: {NinjaAccount.Name} | " +
                    $"Balance: {_lastCashValue:F2}");

                return;
            }

            decimal realizedPnL =
                updatedBalance -
                _lastCashValue;

            if (realizedPnL == 0m)
            {
                return;
            }

            NinjaTradeSnapshot pendingSnapshot =
                _pendingClosedTrades.Dequeue();

            NinjaTradeSnapshot completedSnapshot =
      _tradeProcessor.CreateSnapshot(
          pendingSnapshot.ExternalTradeId,
          pendingSnapshot.AccountName,
          pendingSnapshot.InstrumentName,
          pendingSnapshot.Direction,
          pendingSnapshot.Quantity,
          pendingSnapshot.EntryPrice,
          pendingSnapshot.ExitPrice,
         realizedPnL,
pendingSnapshot.EntryTime,
pendingSnapshot.ExitTime,
pendingSnapshot.PointValue);
            _lastCashValue =
                updatedBalance;

            var result =
                _lifecycleManager.ProcessTrade(
                    completedSnapshot);

            EnsureClosedTradesDate();

            _closedTradesToday.Add(
                completedSnapshot);

            SaveTodayActivity();

            WriteOutput(
                $"MaC | Today's Activity guardada | " +
                $"Cuenta: {NinjaAccount.Name} | " +
                $"Operaciones: {_closedTradesToday.Count}");

            _lifecycleManager
                .SynchronizeBalance();

            /*
             * Si previamente se detectó una infracción
             * de contratos, se aplica oficialmente después
             * de procesar el resultado de la operación.
             */
            bool contractFailureApplied =
                ApplyPendingContractFailure();

            if (contractFailureApplied)
            {
                WriteOutput(
                    $"MaC | Evaluación finalizada por contratos | " +
                    $"Cuenta: {NinjaAccount.Name} | " +
                    $"Regla: " +
                    $"{Context.Account.FailedRule?.ToString() ?? "No informada"} | " +
                    $"Estado: {Context.Account.Status} | " +
                    $"Motivo: {Context.Account.FailureReason}");
            }
            else if (result.Action ==
                EngineAction.LockDay)
            {
                /*
                 * El Engine conserva su decisión y su estado.
                 *
                 * MaC no cancela órdenes y no envía Flatten.
                 */
                WriteOutput(
                    $"MaC | Jornada marcada por regla de riesgo | " +
                    $"Cuenta: {NinjaAccount.Name} | " +
                    $"Regla: " +
                    $"{result.RuleTriggered?.ToString() ?? "No informada"} | " +
                    $"Motivo: {result.Reason}");
            }
            else if (result.IsAccountFailed ||
                Context.Account.Status ==
                    CoreAccountStatus.Failed)
            {
                /*
                 * El fallo ya fue persistido por el Engine.
                 *
                 * Chart Trader será bloqueado preventivamente
                 * por MaCTradeLockManager.
                 *
                 * No se intervienen órdenes manuales.
                 */
                WriteOutput(
                    $"MaC | Evaluación marcada FAILED | " +
                    $"Cuenta: {NinjaAccount.Name} | " +
                    $"Regla: " +
                    $"{Context.Account.FailedRule?.ToString() ?? "No informada"} | " +
                    $"Motivo: {Context.Account.FailureReason}");
            }

            /*
             * El Engine ya terminó de procesar
             * una operación legítima:
             *
             * - actualizó el balance;
             * - actualizó High Water Mark y Floor;
             * - contabilizó días y estadísticas;
             * - determinó si la cuenta continúa activa
             *   o quedó FAILED;
             * - persistió el nuevo estado.
             *
             * Guardian acepta ahora esa fotografía
             * como la nueva línea base válida.
             */
            try
            {
                int persistedTradeCount =
                    _lifecycleManager
                        .GetTradeHistory()
                        .Count;

                _guardianObserver.AcceptCurrentState(
                    Context.Account,
                    persistedTradeCount,
                    "Official trade processed by MaC Engine.");
            }
            catch (Exception exception)
            {
                /*
                 * Un fallo de Guardian nunca debe interrumpir
                 * el procesamiento normal del Engine.
                 */
                WriteOutput(
                    "[GUARDIAN] Error accepting trade baseline | " +
                    $"Account: {NinjaAccount.Name} | " +
                    exception);
            }

            WriteOutput(
                $"MaC | Operación procesada | " +
                $"Cuenta: {NinjaAccount.Name} | " +
                $"Regla: " +
                $"{result.RuleTriggered?.ToString() ?? "Ninguna"} | " +
                $"PnL realizado: " +
                $"{completedSnapshot.RealizedPnL:F2} | " +
                $"Balance cerrado: " +
                $"{Context.Account.ClosedBalance:F2} | " +
                $"PnL flotante: " +
                $"{Context.UnrealizedPnL:F2} | " +
                $"Daily efectivo: " +
                $"{Context.EffectiveDailyPnL:F2}");



            QueueCloudTradeSend(
    completedSnapshot);
            EvaluationState cloudState =
    EvaluationStateFactory.Create(
        Context);

            QueueCloudSnapshotSend(
                cloudState);
        }
    }

    private void ProcessEntry(
        ExecutionEventArgs e,
        string executionId,
        string instrumentName)
    {
        NinjaTradeDirection direction =
            ConvertDirection(
                e.MarketPosition);

        if (direction ==
            NinjaTradeDirection.Unknown)
        {
            WriteOutput(
                $"MaC | Entrada ignorada | " +
                $"ExecutionId: {executionId} | " +
                $"Dirección inválida: " +
                $"{e.MarketPosition}");

            return;
        }

        _tradeProcessor.RegisterEntry(
            NinjaAccount.Name,
            instrumentName,
            direction,
            e.Quantity,
            Convert.ToDecimal(
                e.Price),
            e.Time);

        int openQuantity =
            _tradeProcessor.GetOpenQuantity(
                NinjaAccount.Name,
                instrumentName);

        decimal equivalentContracts =
            _tradeProcessor.GetTotalEquivalentContracts(
                NinjaAccount.Name);

        var validationResult =
            _lifecycleManager
                .ValidateEntryContracts(
                    openQuantity,
                    e.Time,
                    equivalentContracts);


        WriteOutput(
            $"MaC | Entrada procesada | " +
            $"Instrumento: {instrumentName} | " +
            $"Dirección: {direction} | " +
            $"Cantidad abierta: {openQuantity} | " +
            $"Precio promedio: " +
            $"{_tradeProcessor.GetAverageEntryPrice(
                NinjaAccount.Name,
                instrumentName):F2}");

        if (validationResult.Action !=
            EngineAction.FailAccount)
        {
            return;
        }

        string reason =
            string.IsNullOrWhiteSpace(
                validationResult.Reason)
                ? "Maximum contracts exceeded. " +
                  $"Open quantity: {openQuantity}."
                : validationResult.Reason;

        /*
  * La infracción de contratos es definitiva
  * desde el momento en que se detecta.
  *
  * Guardamos FAILED inmediatamente.
  * No cancelamos órdenes ni enviamos Flatten.
  */
        RuleType failureRule =
            validationResult.RuleTriggered
            ?? RuleType.MaxContracts;

        _lifecycleManager.FailAccount(
            failureRule,
            reason);

        _terminalSettlementCashValueBaseline =
    _lastCashValue;

        /*
         * Si existe una posición abierta,
         * seguimos observando su cierre financiero.
         */
        if (openQuantity > 0 || HasOpenPosition())
        {
            BeginTerminalSettlement();
        }
        AcceptGuardianBaseline("Official maximum-contract failure recorded by MaC Engine.");

        /*
 * Notificamos inmediatamente a Cloud
 * que la evaluación quedó FAILED.
 *
 * El snapshot se conserva en el Outbox
 * si Cloud no está disponible.
 */
        EvaluationState failedCloudState =
            EvaluationStateFactory.Create(
                Context);

        QueueCloudSnapshotSend(
            failedCloudState);

        WriteOutput(
            $"MaC | FALLO CONTRACTUAL INMEDIATO | " +
            $"Cuenta: {NinjaAccount.Name} | " +
            $"Instrumento: {instrumentName} | " +
            $"Contratos equivalentes: {equivalentContracts:F2} | " +
            $"Regla: {failureRule} | " +
            $"Estado: {Context.Account.Status} | " +
            $"Motivo: {reason}");
    }

    private void ProcessExit(
        ExecutionEventArgs e,
        string executionId,
        string instrumentName)
    {


        if (!_tradeProcessor.HasOpenTrade(
                NinjaAccount.Name,
                instrumentName))
        {
            WriteOutput(
                $"MaC | Salida ignorada | " +
                $"ExecutionId: {executionId} | " +
                "No existe operación abierta");

            return;
        }

        NinjaTradeSnapshot snapshot =
           _tradeProcessor.CompleteTrade(
    executionId,
    NinjaAccount.Name,
    instrumentName,
    e.Quantity,
Convert.ToDecimal(
    e.Price),
0m,
e.Time,
Convert.ToDecimal(
    e.Execution.Instrument.MasterInstrument.PointValue));

        _pendingClosedTrades.Enqueue(
            snapshot);

        WriteOutput(
            $"MaC | Salida registrada | " +
            $"Instrumento: {instrumentName} | " +
            $"Cantidad cerrada: " +
            $"{snapshot.Quantity} | " +
            $"Pendientes de CashValue: " +
            $"{_pendingClosedTrades.Count}");
    }

    /// <summary>
    /// Aplica oficialmente una infracción contractual
    /// detectada mientras existía una posición.
    ///
    /// Este método se ejecuta solamente después
    /// de que la operación haya actualizado balance,
    /// estadísticas e historial.
    /// </summary>
    private bool ApplyPendingContractFailure()
    {
        if (!_hasPendingContractFailure)
        {
            return false;
        }

        /*
         * No aplicamos el fallo mientras todavía exista
         * alguna posición abierta en la cuenta.
         *
         * Esto también protege cierres parciales:
         * cerrar 1 de 3 contratos no debe activar todavía
         * el fallo definitivo.
         */
        bool hasOpenPositions =
            NinjaAccount.Positions.Any(
                position =>
                    position.MarketPosition !=
                        MarketPosition.Flat &&
                    position.Quantity > 0);

        if (hasOpenPositions)
        {
            WriteOutput(
                $"MaC | FALLO CONTRACTUAL AÚN PENDIENTE | " +
                $"Cuenta: {NinjaAccount.Name} | " +
                "Todavía existe una posición abierta.");

            return false;
        }
        if (_pendingClosedTrades.Count > 0)
        {
            WriteOutput(
                $"MaC | FALLO CONTRACTUAL AÚN PENDIENTE | " +
                $"Cuenta: {NinjaAccount.Name} | " +
                $"Salidas pendientes de conciliación: " +
                $"{_pendingClosedTrades.Count}");

            return false;
        }
        RuleType failureRule =
            _pendingContractFailureRule;

        string failureReason =
            string.IsNullOrWhiteSpace(
                _pendingContractFailureReason)
                ? "Maximum contracts exceeded."
                : _pendingContractFailureReason;

        var failureResult =
            _lifecycleManager.FailAccount(
                failureRule,
                failureReason);

        /*
         * Limpiamos el estado pendiente después
         * de que el fallo fue persistido oficialmente.
         */
        _hasPendingContractFailure =
            false;

        _pendingContractFailureRule =
            RuleType.MaxContracts;

        _pendingContractFailureReason =
            string.Empty;

        WriteOutput(
            $"MaC | FALLO CONTRACTUAL APLICADO | " +
            $"Cuenta: {NinjaAccount.Name} | " +
            $"Regla: {failureRule} | " +
            $"Estado: {Context.Account.Status} | " +
            $"Motivo: {failureReason}");

        return
            failureResult.Action ==
                EngineAction.FailAccount ||
            Context.Account.Status ==
                CoreAccountStatus.Failed;
    }
    private bool HasOpenPosition()
    {
        return NinjaAccount.Positions.Any(position =>
            position.MarketPosition != MarketPosition.Flat &&
            position.Quantity > 0);
    }

    private void BeginTerminalSettlement()
    {
        if (_terminalSettlementState != null &&
            !_terminalSettlementState.IsSettlementApplied)
        {
            _isAwaitingTerminalSettlement = true;
            return;
        }

        var state = new NinjaTerminalSettlementState
        {
            AccountId = _terminalSettlementAccountId,
            AccountName = NinjaAccount.Name,
            CashValueBaseline = _lastCashValue,
            EvaluationBalanceAtFailure = Context.Account.ClosedBalance,
            FailedAtUtc = DateTime.UtcNow,
            IsSettlementApplied = false,
            FinalCashValue = null
        };

        // Fail closed: never silently proceed without the recovery record.
        _terminalSettlementStorage.Save(_terminalSettlementFilePath, state);
        _terminalSettlementState = state;
        _terminalSettlementCashValueBaseline = state.CashValueBaseline;
        _isAwaitingTerminalSettlement = true;
        _terminalFlatObservedUtc = DateTime.MinValue;
        _terminalLastCashChangeUtc = DateTime.MinValue;
        _terminalExitObserved = false;
    }

    private void RestoreTerminalSettlement()
    {
        NinjaTerminalSettlementState? state =
            _terminalSettlementStorage.LoadForAccount(
                _terminalSettlementFilePath, _terminalSettlementAccountId);
        if (state == null)
        {
            return;
        }
        if (!string.Equals(state.AccountName, NinjaAccount.Name,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Terminal settlement belongs to a different NinjaTrader account.");
        }
        if (Context.Account.Status != CoreAccountStatus.Failed)
        {
            throw new InvalidOperationException(
                "A terminal settlement record exists for a non-FAILED evaluation.");
        }

        _terminalSettlementState = state;
        _terminalSettlementCashValueBaseline = state.CashValueBaseline;
        _isAwaitingTerminalSettlement = !state.IsSettlementApplied;
        _terminalFlatObservedUtc = DateTime.MinValue;
        _terminalLastCashChangeUtc = DateTime.MinValue;
        _terminalExitObserved = false;

        WriteOutput($"MaC | Terminal settlement restored | " +
            $"Account: {NinjaAccount.Name} | Pending: {_isAwaitingTerminalSettlement}");
    }

    private void AcceptGuardianBaseline(string reason)
    {
        try
        {
            _guardianObserver.AcceptCurrentState(
                Context.Account,
                _lifecycleManager.GetTradeHistory().Count,
                reason);
        }
        catch (Exception exception)
        {
            WriteOutput("[GUARDIAN] Baseline update failed | " +
                NinjaAccount.Name + " | " + exception.Message);
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed =
                true;
        }

        _riskMonitor.Change(
            Timeout.Infinite,
            Timeout.Infinite);

        _riskMonitor.Dispose();

        WriteOutput(
            $"MaC | Sesión liberada | " +
            $"Cuenta: {NinjaAccount.Name}");
    }

    private static AccountType ResolveAccountType(
        NinjaAccount account)
    {
        string accountName =
            account.Name?.Trim()
            ?? string.Empty;

        if (ContainsAccountIdentifier(
                accountName,
                "Breeze",
                "25K",
                "25000"))
        {
            return AccountType.Breeze25K;
        }

        if (ContainsAccountIdentifier(
                accountName,
                "Solstice",
                "50K",
                "50000"))
        {
            return AccountType.Solstice50K;
        }

        if (ContainsAccountIdentifier(
                accountName,
                "Summit",
                "75K",
                "75000"))
        {
            return AccountType.Summit75K;
        }

        if (ContainsAccountIdentifier(
                accountName,
                "Ethereal",
                "100K",
                "100000"))
        {
            return AccountType.Ethereal100K;
        }

        if (ContainsAccountIdentifier(
                accountName,
                "Zenith",
                "200K",
                "200000"))
        {
            return AccountType.Zenith200K;
        }

        throw new InvalidOperationException(
            $"No se pudo determinar el tipo de evaluación " +
            $"para la cuenta NinjaTrader '{account.Name}'. " +
            $"El nombre debe incluir 25K, 50K, 75K, " +
            $"100K o 200K.");
    }

    private static bool ContainsAccountIdentifier(
        string accountName,
        params string[] identifiers)
    {
        return identifiers.Any(
            identifier =>
                accountName.IndexOf(
                    identifier,
                    StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static NinjaTradeDirection ConvertDirection(
        MarketPosition marketPosition)
    {
        switch (marketPosition)
        {
            case MarketPosition.Long:

                return NinjaTradeDirection.Long;

            case MarketPosition.Short:

                return NinjaTradeDirection.Short;

            default:

                return NinjaTradeDirection.Unknown;
        }
    }

    private void EnsureClosedTradesDate()
    {
        DateTime today =
     TradingSessionClock.GetCurrentTradingDate();

        if (_closedTradesDate.Date ==
            today)
        {
            return;
        }

        _closedTradesToday.Clear();

        _closedTradesDate =
      TradingSessionClock.GetCurrentTradingDate();

        SaveTodayActivity();

        WriteOutput(
            $"MaC | Today's Activity reiniciada | " +
            $"Cuenta: {NinjaAccount.Name} | " +
            $"Fecha: {today:yyyy-MM-dd}");
    }

    private void RestoreTodayActivity()
    {
        _closedTradesDate =
     TradingSessionClock
         .GetCurrentTradingDate();

        IReadOnlyList<NinjaTradeSnapshot>
            restoredTrades =
                _todayActivityStorage.LoadToday(
                    _todayActivityFilePath,
                    _closedTradesDate);

        _closedTradesToday.Clear();

        _closedTradesToday.AddRange(
            restoredTrades.Where(
                trade =>
                    string.Equals(
                        trade.AccountName,
                        NinjaAccount.Name,
                        StringComparison.OrdinalIgnoreCase)));

        WriteOutput(
            $"MaC | Today's Activity restaurada | " +
            $"Cuenta: {NinjaAccount.Name} | " +
            $"Fecha: {_closedTradesDate:yyyy-MM-dd} | " +
            $"Operaciones: {_closedTradesToday.Count}");
    }

    private void SaveTodayActivity()
    {
        _todayActivityStorage.Save(
            _todayActivityFilePath,
            _closedTradesDate,
            _closedTradesToday);
    }

    private static string CreateTodayActivityFilePath(
        string stateFilePath)
    {
        if (string.IsNullOrWhiteSpace(
                stateFilePath))
        {
            throw new ArgumentException(
                "La ruta del estado es obligatoria.",
                nameof(stateFilePath));
        }

        string? directory =
            Path.GetDirectoryName(
                stateFilePath);

        string fileName =
            Path.GetFileNameWithoutExtension(
                stateFilePath);

        string activityFileName =
            $"{fileName}.today-activity.json";

        if (string.IsNullOrWhiteSpace(
                directory))
        {
            return activityFileName;
        }

        return Path.Combine(
            directory,
            activityFileName);
    }

    private static string CreateExecutionId(
        ExecutionEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(
                e.ExecutionId))
        {
            return e.ExecutionId;
        }

        return
            $"{e.OrderId}|" +
            $"{e.Execution.Instrument?.FullName}|" +
            $"{e.Quantity}|" +
            $"{e.Price}|" +
            $"{e.Time:O}";
    }

    /// <summary>
    /// Muestra una advertencia cuando MaC detecta
    /// una posible reinicialización del balance.
    /// </summary>
    private void ShowResetDetectedMessage()
    {
        try
        {
            Application? application =
                Application.Current;

            if (application == null)
            {
                return;
            }

            application.Dispatcher.BeginInvoke(
                new Action(
                    () =>
                    {
                        MessageBox.Show(
                            "A balance reset or account " +
                            "reinitialization was detected.\n\n" +
                            "Account: " +
                            NinjaAccount.Name +
                            "\n\n" +
                            "The official evaluation state was " +
                            "preserved and requires administrative " +
                            "review.",
                            "MaCPro Funding — Reset Detected",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }));
        }
        catch (Exception exception)
        {
            WriteOutput(
                "MaC Guardian | No fue posible mostrar " +
                "la alerta de reset | " +
                exception.Message);
        }
    }

    /// <summary>
    /// Construye la ruta del archivo persistente
    /// de auditoría de seguridad.
    /// </summary>
    private static string CreateSecurityAuditFilePath(
        string stateFilePath)
    {
        if (string.IsNullOrWhiteSpace(
                stateFilePath))
        {
            throw new ArgumentException(
                "La ruta del estado es obligatoria.",
                nameof(stateFilePath));
        }

        string? directory =
            Path.GetDirectoryName(
                stateFilePath);

        string fileName =
            Path.GetFileNameWithoutExtension(
                stateFilePath);

        string auditFileName =
            $"{fileName}.security-audit.log";

        if (string.IsNullOrWhiteSpace(
                directory))
        {
            return auditFileName;
        }

        return Path.Combine(
            directory,
            auditFileName);
    }
    private void TryEnsureCloudBinding()
    {
        if (_cloudTradeClient != null)
        {
            return;
        }

        DateTime nowUtc =
            DateTime.UtcNow;

        if (_cloudBindingNotFoundRetryAfterUtc !=
        DateTime.MinValue &&
    nowUtc <
        _cloudBindingNotFoundRetryAfterUtc)
        {
            return;
        }

        if (_lastCloudBindingRetryUtc != DateTime.MinValue &&
            nowUtc - _lastCloudBindingRetryUtc <
                CloudBindingRetryInterval)
        {
            return;
        }

        _lastCloudBindingRetryUtc =
            nowUtc;

        if (Interlocked.CompareExchange(
                ref _isCloudBindingRetryRunning,
                1,
                0) != 0)
        {
            return;
        }

        Task.Run(
            () =>
            {
                try
                {
                    MaCCloudOptions? cloudOptions =
                        MaCCloudConfiguration
                            .TryLoadForAccount(
                                NinjaAccount.Name);

                    if (cloudOptions == null)
                    {
                        return;
                    }

                    /*
                     * Si el token apareció mientras la sesión
                     * estaba activa, simplemente creamos el cliente.
                     */
                    if (!string.IsNullOrWhiteSpace(
              cloudOptions.AccountBindingToken))
                    {
                        _cloudBindingNotFoundRetryAfterUtc =
                            DateTime.MinValue;

                        _cloudTradeClient =
                            new MaCCloudTradeClient(
                                cloudOptions.BaseUrl,
                                cloudOptions.ApiKey,
                                cloudOptions.AccountBindingToken,
cloudOptions.InstallationCredential,
cloudOptions.InstallationId);

                        WriteOutput(
                            $"MaC Cloud | Binding local recuperado | " +
                            $"Cuenta: {NinjaAccount.Name}");

                        TryProcessCloudOutbox();

                        return;
                    }

                    WriteOutput(
                        $"MaC Cloud | Re-binding iniciado | " +
                        $"Cuenta: {NinjaAccount.Name}");

                    var credentialStore =
                        new MaCLocalCredentialStore();

                    string installationId =
                        credentialStore
                            .GetOrCreateInstallationId();

                    MaCCloudBindingResponse bindingResponse =
                        MaCCloudTradeClient
                            .BindAccountAsync(
                                cloudOptions.BaseUrl,
                                cloudOptions.ApiKey,
                                NinjaAccount.Name,
                                installationId,
cloudOptions.InstallationCredential)
                            .GetAwaiter()
                            .GetResult();
                    if (!bindingResponse.Success ||
                        string.IsNullOrWhiteSpace(
                            bindingResponse.BindingToken))
                    {
                        if ((int)bindingResponse.StatusCode == 404)
                        {
                            _cloudBindingNotFoundRetryAfterUtc =
                                DateTime.UtcNow +
                                CloudBindingNotFoundRetryInterval;

                            WriteOutput(
                                $"MaC Cloud | Cuenta no encontrada en Cloud; " +
                                $"re-binding pausado | " +
                                $"Cuenta: {NinjaAccount.Name} | " +
                                $"Próximo intento: " +
                                $"{_cloudBindingNotFoundRetryAfterUtc:O}");

                            return;
                        }

                        WriteOutput(
                            $"MaC Cloud | Re-binding pendiente | " +
                            $"Cuenta: {NinjaAccount.Name} | " +
                            $"HTTP: {(int)bindingResponse.StatusCode} " +
                            $"{bindingResponse.StatusCode} | " +
                            $"Error: {bindingResponse.ErrorMessage}");

                        return;
                    }

                    credentialStore
                        .SaveAccountBindingToken(
                            NinjaAccount.Name,
                            bindingResponse.BindingToken);

                    _cloudBindingNotFoundRetryAfterUtc =
    DateTime.MinValue;

                    /*
                     * FIX 401: antes este cliente se creaba
                     * SIN InstallationCredential ni InstallationId,
                     * por eso tras un re-binding las llamadas
                     * devolvían 401 hasta reiniciar NinjaTrader
                     * (al reiniciar se usa el constructor completo).
                     */
                    _cloudTradeClient =
                        new MaCCloudTradeClient(
                            cloudOptions.BaseUrl,
                            cloudOptions.ApiKey,
                            bindingResponse.BindingToken,
                            cloudOptions.InstallationCredential,
                            installationId);

                    WriteOutput(
                        $"MaC Cloud | Re-binding completado | " +
                        $"Cuenta: {NinjaAccount.Name} | " +
                        $"HTTP: {(int)bindingResponse.StatusCode} " +
                        $"{bindingResponse.StatusCode}");

                    /*
                     * Si quedaron trades pendientes en disco,
                     * intentamos procesarlos inmediatamente.
                     */
                    TryProcessCloudOutbox();
                }
                catch (Exception exception)
                {
                    WriteOutput(
                        $"MaC Cloud | Error durante re-binding | " +
                        $"Cuenta: {NinjaAccount.Name} | " +
                        $"Error: {exception.Message}");
                }
                finally
                {
                    Interlocked.Exchange(
                        ref _isCloudBindingRetryRunning,
                        0);
                }
            });
    }

    private void TryProcessCloudOutbox()
    {
        MaCCloudTradeClient? cloudClient =
            _cloudTradeClient;

        MaCCloudOutbox? cloudOutbox =
            _cloudOutbox;

        if (cloudClient == null ||
            cloudOutbox == null)
        {
            return;
        }

        DateTime nowUtc =
            DateTime.UtcNow;

        /*
         * Respetamos el intervalo de retry antes
         * de consultar físicamente el Outbox.
         */
        if (_lastCloudOutboxRetryUtc !=
                DateTime.MinValue &&
            nowUtc - _lastCloudOutboxRetryUtc <
                CloudOutboxRetryInterval)
        {
            return;
        }

        if (Interlocked.CompareExchange(
                ref _isCloudOutboxRetryRunning,
                1,
                0) != 0)
        {
            return;
        }

        _lastCloudOutboxRetryUtc =
            nowUtc;

        _ = Task.Run(
            async () =>
            {
                try
                {
                    if (cloudOutbox.PendingCount == 0)
                    {
                        return;
                    }

                    IReadOnlyList<MaCCloudOutboxItem>
                        pendingItems =
                            cloudOutbox
                                .GetPendingItems();

                    if (pendingItems.Count == 0)
                    {
                        return;
                    }

                    WriteOutput(
                        $"MaC Cloud | Procesando Outbox | " +
                        $"Cuenta: {NinjaAccount.Name} | " +
                        $"Pendientes: {pendingItems.Count}");

                    foreach (MaCCloudOutboxItem item
                             in pendingItems)
                    {
                        /*
                         * Solamente procesamos tipos
                         * reconocidos por MaC.
                         */
                        if (!item.IsTrade &&
                            !item.IsSnapshot)
                        {
                            WriteOutput(
                                $"MaC Cloud | Elemento Outbox ignorado | " +
                                $"Cuenta: {NinjaAccount.Name} | " +
                                $"Tipo: {item.ItemType}");

                            continue;
                        }

                        MaCCloudResponse response =
                            await cloudClient
                                .SendOutboxPayloadAsync(
                                    item)
                                .ConfigureAwait(false);

                        string persistedText =
                            response.Persisted.HasValue
                                ? response.Persisted.Value
                                    ? "TRUE"
                                    : "FALSE"
                                : "N/A";

                        /*
                         * ÉXITO:
                         *
                         * Cloud confirmó el request.
                         * Eliminamos el elemento del Outbox.
                         */
                        if (response.Success)
                        {
                            cloudOutbox.Complete(
                                item);

                            if (item.IsTrade)
                            {
                                WriteOutput(
                                    $"MaC Cloud | Trade Outbox recuperado | " +
                                    $"Cuenta: {NinjaAccount.Name} | " +
                                    $"ExternalTradeId: {item.ItemId} | " +
                                    $"HTTP: {(int)response.StatusCode} " +
                                    $"{response.StatusCode} | " +
                                    $"Persisted: {persistedText} | " +
                                    $"Pendientes: " +
                                    $"{cloudOutbox.PendingCount}");
                            }
                            else
                            {
                                WriteOutput(
                                    $"MaC Cloud | Snapshot Outbox recuperado | " +
                                    $"Cuenta: {NinjaAccount.Name} | " +
                                    $"HTTP: {(int)response.StatusCode} " +
                                    $"{response.StatusCode} | " +
                                    $"Persisted: {persistedText} | " +
                                    $"Pendientes: " +
                                    $"{cloudOutbox.PendingCount}");
                            }

                            continue;
                        }

                        /*
                         * Desde aquí sabemos que el request
                         * NO fue exitoso.
                         */
                        int statusCode =
                            (int)response.StatusCode;

                        /*
                         * Fallos temporales:
                         *
                         * 0   = sin conexión / API caída
                         * 408 = Request Timeout
                         * 429 = Too Many Requests
                         * 5xx = problema del servidor
                         *
                         * Permanecen como .outbox.
                         */
                        bool isTransientFailure =
                            statusCode == 0 ||
                            statusCode == 408 ||
                            statusCode == 429 ||
                            statusCode >= 500;

                        bool isPermanentClientFailure =
                        statusCode >= 400 &&
                        statusCode <= 499 &&
                        statusCode != 408 &&
                        statusCode != 429;

                        /*
                         * Cualquier otro HTTP no exitoso
                         * se considera permanente para
                         * este payload.
                         *
                         * Ejemplos:
                         * 400, 401, 403, 404.
                         */
                        if (isPermanentClientFailure)
                        {
                            cloudOutbox.MarkPermanentFailure(
                                item);

                            if (item.IsTrade)
                            {
                                WriteOutput(
                                    $"MaC Cloud | Trade movido a FAILED | " +
                                    $"Cuenta: {NinjaAccount.Name} | " +
                                    $"ExternalTradeId: {item.ItemId} | " +
                                    $"HTTP: {statusCode} " +
                                    $"{response.StatusCode} | " +
                                    $"Error: {response.ErrorMessage}");
                            }
                            else
                            {
                                WriteOutput(
                                    $"MaC Cloud | Snapshot movido a FAILED | " +
                                    $"Cuenta: {NinjaAccount.Name} | " +
                                    $"HTTP: {statusCode} " +
                                    $"{response.StatusCode} | " +
                                    $"Error: {response.ErrorMessage}");
                            }

                            /*
                             * El elemento salió de la cola activa
                             * y quedó preservado como .failed.
                             *
                             * Podemos continuar con el siguiente.
                             */
                            continue;
                        }

                        /*
                         * FALLO TEMPORAL:
                         *
                         * Conservamos el .outbox.
                         */
                        if (item.IsTrade)
                        {
                            WriteOutput(
                                $"MaC Cloud | Retry trade pendiente | " +
                                $"Cuenta: {NinjaAccount.Name} | " +
                                $"ExternalTradeId: {item.ItemId} | " +
                                $"HTTP: {statusCode} " +
                                $"{response.StatusCode} | " +
                                $"Error: {response.ErrorMessage}");
                        }
                        else
                        {
                            WriteOutput(
                                $"MaC Cloud | Retry snapshot pendiente | " +
                                $"Cuenta: {NinjaAccount.Name} | " +
                                $"HTTP: {statusCode} " +
                                $"{response.StatusCode} | " +
                                $"Error: {response.ErrorMessage}");
                        }

                        /*
                         * Detenemos este ciclo para evitar
                         * bombardear Cloud mientras exista
                         * un problema temporal.
                         */
                        break;
                    }
                }
                catch (Exception exception)
                {
                    WriteOutput(
                        $"MaC Cloud | Error procesando Outbox | " +
                        $"Cuenta: {NinjaAccount.Name} | " +
                        $"Error: {exception}");
                }
                finally
                {
                    Interlocked.Exchange(
                        ref _isCloudOutboxRetryRunning,
                        0);
                }
            });
    }


    private void TryQueueCloudHeartbeat()
    {
        if (_cloudTradeClient == null)
        {
            return;
        }

        DateTime nowUtc =
            DateTime.UtcNow;

        if (_lastCloudHeartbeatUtc !=
                DateTime.MinValue &&
            nowUtc - _lastCloudHeartbeatUtc <
                CloudHeartbeatInterval)
        {
            return;
        }

        _lastCloudHeartbeatUtc =
            nowUtc;

        EvaluationState heartbeatState =
            EvaluationStateFactory.Create(
                Context);

        QueueCloudSnapshotSend(
            heartbeatState);

        WriteOutput(
            $"MaC Cloud | Heartbeat programado | " +
            $"Cuenta: {NinjaAccount.Name} | " +
            $"Estado: {heartbeatState.Status} | " +
            $"Balance: {heartbeatState.CurrentBalance:F2}");
    }
    private void QueueCloudTradeSend(
     NinjaTradeSnapshot snapshot)
    {
        MaCCloudOutbox? cloudOutbox =
            _cloudOutbox;

        /*
         * El Outbox es la primera garantía.
         *
         * El trade debe quedar persistido localmente
         * aunque todavía no exista binding,
         * cliente Cloud o conexión con la API.
         */
        if (cloudOutbox == null)
        {
            return;
        }

        MaCCloudOutboxItem? outboxItem =
            null;

        try
        {
            string payload =
                MaCCloudTradeClient
                    .CreateTradePayload(
                        NinjaAccount.Name,
                        snapshot.ExternalTradeId,
                        snapshot.InstrumentName,
                        snapshot.Direction.ToString(),
                        snapshot.Quantity,
                        snapshot.EntryPrice,
                        snapshot.ExitPrice,
                        snapshot.RealizedPnL,
                        snapshot.EntryTime,
                        snapshot.ExitTime);

            string outboxFilePath =
                cloudOutbox.EnqueueTrade(
                    snapshot.ExternalTradeId,
                    payload);

            outboxItem =
                cloudOutbox
                    .GetPendingItems()
                    .FirstOrDefault(
                        item =>
                            string.Equals(
                                item.FilePath,
                                outboxFilePath,
                                StringComparison.OrdinalIgnoreCase));

            if (outboxItem == null)
            {
                WriteOutput(
                    $"MaC Cloud | Trade guardado pero no pudo " +
                    $"ser recuperado de Outbox | " +
                    $"Cuenta: {NinjaAccount.Name} | " +
                    $"ExternalTradeId: " +
                    $"{snapshot.ExternalTradeId}");

                return;
            }

            WriteOutput(
                $"MaC Cloud | Trade guardado en Outbox | " +
                $"Cuenta: {NinjaAccount.Name} | " +
                $"ExternalTradeId: " +
                $"{snapshot.ExternalTradeId} | " +
                $"Pendientes: {cloudOutbox.PendingCount}");
        }
        catch (Exception exception)
        {
            /*
             * Una falla de almacenamiento Cloud jamás debe
             * interrumpir MaC Engine ni el procesamiento local
             * de la evaluación.
             */
            WriteOutput(
                $"MaC Cloud | Error guardando trade en Outbox | " +
                $"Cuenta: {NinjaAccount.Name} | " +
                $"ExternalTradeId: " +
                $"{snapshot.ExternalTradeId} | " +
                $"Error: {exception}");

            return;
        }

        /*
         * A partir de aquí el trade YA está seguro
         * físicamente en el Outbox.
         *
         * Si todavía no existe binding, no intentamos
         * enviarlo. TryEnsureCloudBinding() continuará
         * intentando recuperar/crear el binding y,
         * cuando lo consiga, TryProcessCloudOutbox()
         * enviará este trade.
         */
        MaCCloudTradeClient? cloudClient =
            _cloudTradeClient;

        if (cloudClient == null)
        {
            WriteOutput(
                $"MaC Cloud | Trade preservado esperando binding | " +
                $"Cuenta: {NinjaAccount.Name} | " +
                $"ExternalTradeId: " +
                $"{snapshot.ExternalTradeId}");

            return;
        }

        _ = Task.Run(
            async () =>
            {
                try
                {
                    MaCCloudResponse response =
                        await cloudClient
                            .SendOutboxPayloadAsync(
                                outboxItem)
                            .ConfigureAwait(false);

                    string persistedText =
                        response.Persisted.HasValue
                            ? response.Persisted.Value
                                ? "TRUE"
                                : "FALSE"
                            : "N/A";

                    WriteOutput(
                        $"MaC Cloud | Trade Outbox enviado | " +
                        $"Cuenta: {NinjaAccount.Name} | " +
                        $"ExternalTradeId: " +
                        $"{snapshot.ExternalTradeId} | " +
                        $"HTTP: {(int)response.StatusCode} " +
                        $"{response.StatusCode} | " +
                        $"Success: {response.Success} | " +
                        $"Persisted: {persistedText}");

                    if (response.Success)
                    {
                        cloudOutbox.Complete(
                            outboxItem);

                        WriteOutput(
                            $"MaC Cloud | Trade confirmado y " +
                            $"eliminado de Outbox | " +
                            $"Cuenta: {NinjaAccount.Name} | " +
                            $"ExternalTradeId: " +
                            $"{snapshot.ExternalTradeId} | " +
                            $"Pendientes: " +
                            $"{cloudOutbox.PendingCount}");

                        return;
                    }

                    /*
                     * Cloud no confirmó el request.
                     *
                     * NO eliminamos el archivo.
                     * Permanecerá disponible para retry.
                     */
                    WriteOutput(
                        $"MaC Cloud | Trade permanece en Outbox | " +
                        $"Cuenta: {NinjaAccount.Name} | " +
                        $"ExternalTradeId: " +
                        $"{snapshot.ExternalTradeId} | " +
                        $"HTTP: {(int)response.StatusCode} " +
                        $"{response.StatusCode} | " +
                        $"Error: {response.ErrorMessage}");
                }
                catch (Exception exception)
                {
                    /*
                     * Internet, API o cualquier excepción
                     * durante el envío no elimina el trade.
                     *
                     * La copia persistente continúa en Outbox.
                     */
                    WriteOutput(
                        $"MaC Cloud | Envío falló; trade preservado " +
                        $"en Outbox | " +
                        $"Cuenta: {NinjaAccount.Name} | " +
                        $"ExternalTradeId: " +
                        $"{snapshot.ExternalTradeId} | " +
                        $"Error: {exception}");
                }
            });
    }

    private void QueueCloudSnapshotSend(
      EvaluationState state)
    {
        MaCCloudOutbox? cloudOutbox =
            _cloudOutbox;

        /*
         * El snapshot debe quedar persistido localmente
         * aunque todavía no exista binding,
         * cliente Cloud o conexión con la API.
         */
        if (cloudOutbox == null)
        {
            return;
        }

        MaCCloudOutboxItem? outboxItem =
            null;

        try
        {
            string payload =
                MaCCloudTradeClient
                    .CreateEvaluationSnapshotPayload(
                        NinjaAccount.Name,
                        state.LastUpdatedUtc,
                        state.Status.ToString(),
                        state.TradingDayStatus.ToString(),
                        state.FailedRule?.ToString(),
                        state.FailureReason,
                        state.FailedAtUtc,
                        state.StartingBalance,
                        state.ClosedBalance,
                        state.CurrentBalance,
                        state.TotalProfit,
                        state.HighWaterMark,
                        state.InitialFloor,
                        state.CurrentFloor,
                        state.DistanceToFloor,
                        state.TradingDate,
                        state.RealizedDailyPnL,
                        state.UnrealizedPnL,
                        state.DailyPnL,
                        state.TradesToday,
                        state.WinningTradesToday,
                        state.LosingTradesToday,
                        state.TradingDaysCompleted,
                        state.BestDayProfit,
                        state.ConsistencyPercentage,
                        state.ConsistencyMet);

            string outboxFilePath =
                cloudOutbox
                    .EnqueueSnapshot(
                        payload);

            outboxItem =
                cloudOutbox
                    .GetPendingItems()
                    .FirstOrDefault(
                        item =>
                            item.IsSnapshot &&
                            string.Equals(
                                item.FilePath,
                                outboxFilePath,
                                StringComparison.OrdinalIgnoreCase));

            if (outboxItem == null)
            {
                WriteOutput(
                    $"MaC Cloud | Snapshot guardado pero no pudo " +
                    $"ser recuperado de Outbox | " +
                    $"Cuenta: {NinjaAccount.Name}");

                return;
            }

            WriteOutput(
                $"MaC Cloud | Snapshot guardado en Outbox | " +
                $"Cuenta: {NinjaAccount.Name}");
        }
        catch (Exception exception)
        {
            WriteOutput(
                $"MaC Cloud | Error guardando snapshot en Outbox | " +
                $"Cuenta: {NinjaAccount.Name} | " +
                $"Error: {exception}");

            return;
        }

        /*
         * El snapshot YA quedó seguro en disco.
         *
         * Si todavía no existe binding,
         * esperamos a que TryEnsureCloudBinding()
         * lo recupere o lo cree.
         *
         * Cuando eso ocurra,
         * TryProcessCloudOutbox() enviará
         * automáticamente el snapshot pendiente.
         */
        MaCCloudTradeClient? cloudClient =
            _cloudTradeClient;

        if (cloudClient == null)
        {
            WriteOutput(
                $"MaC Cloud | Snapshot preservado esperando binding | " +
                $"Cuenta: {NinjaAccount.Name}");

            return;
        }

        _ = Task.Run(
            async () =>
            {
                try
                {
                    MaCCloudResponse response =
                        await cloudClient
                            .SendOutboxPayloadAsync(
                                outboxItem)
                            .ConfigureAwait(false);

                    string persistedText =
                        response.Persisted.HasValue
                            ? response.Persisted.Value
                                ? "TRUE"
                                : "FALSE"
                            : "N/A";

                    WriteOutput(
                        $"MaC Cloud | Snapshot Outbox enviado | " +
                        $"Cuenta: {NinjaAccount.Name} | " +
                        $"HTTP: {(int)response.StatusCode} " +
                        $"{response.StatusCode} | " +
                        $"Success: {response.Success} | " +
                        $"Persisted: {persistedText}");

                    if (response.Success)
                    {
                        cloudOutbox.Complete(
                            outboxItem);

                        WriteOutput(
                            $"MaC Cloud | Snapshot confirmado y " +
                            $"eliminado de Outbox | " +
                            $"Cuenta: {NinjaAccount.Name}");

                        return;
                    }

                    /*
                     * Cloud no confirmó el snapshot.
                     *
                     * El archivo permanece en Outbox
                     * para retry posterior.
                     */
                    WriteOutput(
                        $"MaC Cloud | Snapshot permanece en Outbox | " +
                        $"Cuenta: {NinjaAccount.Name} | " +
                        $"HTTP: {(int)response.StatusCode} " +
                        $"{response.StatusCode} | " +
                        $"Error: {response.ErrorMessage}");
                }
                catch (Exception exception)
                {
                    WriteOutput(
                        $"MaC Cloud | Envío falló; snapshot preservado " +
                        $"en Outbox | " +
                        $"Cuenta: {NinjaAccount.Name} | " +
                        $"Error: {exception}");
                }
            });
    }
    private static void WriteOutput(
        string message)
    {
        global::NinjaTrader.Code.Output.Process(
            message,
            global::NinjaTrader.NinjaScript
                .PrintTo.OutputTab1);
    }
}