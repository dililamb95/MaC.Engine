using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MaC.NinjaTrader.Services;
using MaCProFunding.AddOn.Presentation;

namespace MaCProFunding.AddOn
{
    /// <summary>
    /// Lógica de interacción para DashboardV2.xaml.
    ///
    /// Administra la sesión, el selector de cuentas,
    /// la actualización periódica y la apertura
    /// del historial persistente.
    ///
    /// Delega el renderizado visual
    /// en MaCRenderEngine.
    /// </summary>
    public partial class DashboardV2 : UserControl
    {
        private readonly MaCRenderEngine
            _renderer;

        private readonly DispatcherTimer
            _refreshTimer;

        private NinjaAccountSession?
            _session;

        private bool
            _isUpdatingAccounts;

        private TradeHistoryWindow?
            _tradeHistoryWindow;

        /*
         * FIX DASHBOARD — diagnóstico visible.
         *
         * Antes los errores iban a Debug.WriteLine,
         * que NinjaTrader no muestra. Si el refresco
         * fallaba en cada tick, el Dashboard parecía
         * "congelado" sin ninguna pista.
         *
         * Ahora se escriben en la pestaña Output 1,
         * limitados a uno cada 10 s para no inundarla.
         */
        private DateTime _lastSuccessfulRefreshUtc =
            DateTime.MinValue;

        private DateTime _lastErrorLogUtc =
            DateTime.MinValue;

        private int _consecutiveRefreshErrors;

        private static readonly TimeSpan ErrorLogInterval =
            TimeSpan.FromSeconds(10);

        /// <summary>
        /// Momento UTC del último refresco completo.
        /// Útil para mostrar "Actualizado hace X s".
        /// </summary>
        public DateTime LastSuccessfulRefreshUtc =>
            _lastSuccessfulRefreshUtc;

        /// <summary>
        /// Solicita al host seleccionar otra cuenta.
        /// </summary>
        public event EventHandler<string?>?
            AccountSelectionRequested;

        /// <summary>
        /// Solicita al host recargar
        /// la lista oficial de cuentas.
        /// </summary>
        public event EventHandler?
            RefreshAccountsRequested;

        /// <summary>
        /// Constructor utilizado por el Preview
        /// y por el diseñador de Visual Studio.
        /// </summary>
        public DashboardV2()
        {
            InitializeComponent();

            _renderer =
                new MaCRenderEngine(
                    this);

            /*
             * FIX DASHBOARD — prioridad Normal.
             *
             * Con Background, WPF solo ejecuta el tick
             * cuando el hilo de UI está libre. En la
             * apertura del mercado NinjaTrader satura ese
             * hilo con gráficos y DOM, y los ticks se
             * retrasan o se acumulan: el Dashboard parece
             * detenido justo cuando más importa.
             */
            _refreshTimer =
                new DispatcherTimer(
                    DispatcherPriority.Normal)
                {
                    Interval =
                        TimeSpan.FromMilliseconds(
                            500)
                };

            _refreshTimer.Tick +=
                OnRefreshTimerTick;

            Loaded +=
                OnDashboardLoaded;

            Unloaded +=
                OnDashboardUnloaded;
        }

        /// <summary>
        /// Constructor utilizado cuando el Dashboard
        /// recibe una sesión real de NinjaTrader.
        /// </summary>
        public DashboardV2(
            NinjaAccountSession session)
            : this()
        {
            AttachSession(
                session);
        }

        /// <summary>
        /// Conecta o reemplaza la sesión utilizada
        /// por el Dashboard.
        /// </summary>
        public void AttachSession(
            NinjaAccountSession session)
        {
            _session =
                session
                ?? throw new ArgumentNullException(
                    nameof(session));

            RefreshDashboard();

            /*
             * FIX DASHBOARD — causa principal del "congelamiento".
             *
             * Antes el timer solo arrancaba en Loaded y
             * SOLO si ya existía una sesión. Si el Dashboard
             * se abría antes de que NinjaTrader cargara las
             * cuentas SimMaC (conexión lenta, Rithmic tardando,
             * Dashboard abierto antes de conectar), Loaded
             * veía _session == null y no arrancaba el timer.
             *
             * Luego el auto-refresh encontraba la cuenta y
             * llamaba AttachSession, que pintaba UNA vez...
             * y nunca más. Por eso un día funcionaba y otro no:
             * dependía del orden en que cargaba todo.
             */
            EnsureRefreshTimerRunning();
        }

        private void EnsureRefreshTimerRunning()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(
                    new Action(
                        EnsureRefreshTimerRunning));

                return;
            }

            if (IsLoaded &&
                !_refreshTimer.IsEnabled)
            {
                _refreshTimer.Start();
            }
        }

        /// <summary>
        /// Recibe del host la lista oficial de cuentas
        /// monitoreadas y actualiza el selector.
        /// </summary>
        public void SetAccounts(
            IReadOnlyList<string>?
                accountNames,
            string?
                selectedAccountName)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(
                    new Action(
                        () =>
                            SetAccounts(
                                accountNames,
                                selectedAccountName)));

                return;
            }

            List<string> safeAccountNames =
                accountNames?
                    .Where(
                        accountName =>
                            !string.IsNullOrWhiteSpace(
                                accountName))
                    .Distinct(
                        StringComparer
                            .OrdinalIgnoreCase)
                    .ToList()
                ?? new List<string>();

            _isUpdatingAccounts =
                true;

            try
            {
                PremiumAccountSelector.ItemsSource =
                    null;

                PremiumAccountSelector.ItemsSource =
                    safeAccountNames;

                string? validSelection =
                    safeAccountNames
                        .FirstOrDefault(
                            accountName =>
                                string.Equals(
                                    accountName,
                                    selectedAccountName,
                                    StringComparison
                                        .OrdinalIgnoreCase));

                PremiumAccountSelector.SelectedItem =
                    validSelection
                    ?? safeAccountNames
                        .FirstOrDefault();
            }
            finally
            {
                _isUpdatingAccounts =
                    false;
            }
        }

        /// <summary>
        /// Sincroniza únicamente la cuenta seleccionada
        /// sin reconstruir toda la lista.
        /// </summary>
        public void SetSelectedAccount(
            string?
                accountName)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(
                    new Action(
                        () =>
                            SetSelectedAccount(
                                accountName)));

                return;
            }

            _isUpdatingAccounts =
                true;

            try
            {
                string? matchingAccount =
                    PremiumAccountSelector
                        .Items
                        .Cast<object>()
                        .OfType<string>()
                        .FirstOrDefault(
                            availableAccount =>
                                string.Equals(
                                    availableAccount,
                                    accountName,
                                    StringComparison
                                        .OrdinalIgnoreCase));

                PremiumAccountSelector.SelectedItem =
                    matchingAccount;
            }
            finally
            {
                _isUpdatingAccounts =
                    false;
            }
        }

        /// <summary>
        /// Obtiene una fotografía del estado,
        /// actualiza los controles principales,
        /// dibuja el historial real de equity
        /// y presenta las operaciones recientes.
        /// </summary>
        public void RefreshDashboard()
        {
            if (_session == null)
            {
                return;
            }

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(
                    new Action(
                        RefreshDashboard));

                return;
            }

            try
            {
                var state =
                    _session
                        .GetEvaluationState();

                if (state == null)
                {
                    return;
                }

                _renderer.Render(
                    state);

                var tradeHistory =
                    _session
                        .GetTradeHistory();

                EquityPerformanceChart.SetData(
                    tradeHistory,
                    state.StartingBalance);

                var todayClosedTrades =
                    _session
                        .GetTodayClosedTrades();

                _renderer.RenderLatestTrades(
                    todayClosedTrades);

                if (_consecutiveRefreshErrors > 0)
                {
                    WriteOutput(
                        "MaC Dashboard | Refresco recuperado | " +
                        $"Cuenta: {_session.NinjaAccount.Name} | " +
                        $"Errores previos: {_consecutiveRefreshErrors}");
                }

                _consecutiveRefreshErrors = 0;

                _lastSuccessfulRefreshUtc =
                    DateTime.UtcNow;
            }
            catch (Exception exception)
            {
                _consecutiveRefreshErrors++;

                DateTime nowUtc =
                    DateTime.UtcNow;

                if (nowUtc - _lastErrorLogUtc >=
                    ErrorLogInterval)
                {
                    _lastErrorLogUtc = nowUtc;

                    WriteOutput(
                        "MaC Dashboard | ERROR al actualizar | " +
                        $"Cuenta: {_session?.NinjaAccount.Name ?? "?"} | " +
                        $"Errores seguidos: {_consecutiveRefreshErrors} | " +
                        $"Último refresco OK (UTC): " +
                        $"{(_lastSuccessfulRefreshUtc == DateTime.MinValue ? "nunca" : _lastSuccessfulRefreshUtc.ToString("HH:mm:ss"))} | " +
                        exception);
                }
            }
        }

        private static void WriteOutput(
            string message)
        {
            try
            {
                global::NinjaTrader.Code.Output.Process(
                    message,
                    global::NinjaTrader.NinjaScript
                        .PrintTo.OutputTab1);
            }
            catch
            {
                // El diagnóstico nunca debe romper el Dashboard.
            }
        }

        /// <summary>
        /// Abre el historial persistente completo
        /// de la evaluación seleccionada.
        /// </summary>
        private void OnViewAllTradesClick(
            object sender,
            RoutedEventArgs e)
        {
            if (_session == null)
            {
                return;
            }

            try
            {
                if (_tradeHistoryWindow != null)
                {
                    if (_tradeHistoryWindow.WindowState ==
                        WindowState.Minimized)
                    {
                        _tradeHistoryWindow.WindowState =
                            WindowState.Normal;
                    }

                    _tradeHistoryWindow.Activate();
                    _tradeHistoryWindow.Focus();

                    return;
                }

                var state =
                    _session
                        .GetEvaluationState();

                var tradeHistory =
                    _session
                        .GetTradeHistory();

                _tradeHistoryWindow =
                    new TradeHistoryWindow(
                        state.AccountName,
                        tradeHistory);

                Window? ownerWindow =
                    Window.GetWindow(
                        this);

                if (ownerWindow != null)
                {
                    _tradeHistoryWindow.Owner =
                        ownerWindow;
                }

                _tradeHistoryWindow.Closed +=
                    OnTradeHistoryWindowClosed;

                _tradeHistoryWindow.Show();
                _tradeHistoryWindow.Activate();
                _tradeHistoryWindow.Focus();
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    "No fue posible abrir el historial de operaciones.\n\n" +
                    exception.Message,
                    "MaCPro Funding — Trade History",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void OnTradeHistoryWindowClosed(
            object? sender,
            EventArgs e)
        {
            if (_tradeHistoryWindow != null)
            {
                _tradeHistoryWindow.Closed -=
                    OnTradeHistoryWindowClosed;
            }

            _tradeHistoryWindow =
                null;
        }

        private void OnPremiumAccountSelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (_isUpdatingAccounts)
            {
                return;
            }

            string? selectedAccountName =
                PremiumAccountSelector
                    .SelectedItem
                as string;

            AccountSelectionRequested?.Invoke(
                this,
                selectedAccountName);
        }

        private void OnPremiumRefreshAccountsClick(
            object sender,
            RoutedEventArgs e)
        {
            RefreshAccountsRequested?.Invoke(
                this,
                EventArgs.Empty);
        }
        private async void ActivateEngineButton_Click(
    object sender,
    RoutedEventArgs e)
        {
            string activationCode =
                ActivationCodeTextBox.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(activationCode))
            {
                ActivationStatusText.Text =
                    "Ingresa tu código de activación.";

                return;
            }
            MaCCloudOptions? cloudOptions =
        MaCCloudConfiguration.TryLoadBase();

            if (cloudOptions == null ||
                string.IsNullOrWhiteSpace(cloudOptions.InstallationId))
            {
                ActivationStatusText.Text =
                    "No fue posible preparar esta instalación para la activación.";

                return;
            }
            ActivationStatusText.Text =
    "Activando MaCPro...";

            ActivateEngineButton.IsEnabled = false;

            string? installationCredential;

            try
            {
                installationCredential =
                    await MaCCloudTradeClient.RedeemInstallationAsync(
                        cloudOptions.BaseUrl,
                        activationCode,
                        cloudOptions.InstallationId);
            }
            catch (InvalidOperationException ex)
            {
                // Do not disclose the activation code or credential in UI/logs.
                if (ex.Message.Contains("HTTP 401"))
                    ActivationStatusText.Text =
                        "Código inválido, vencido o ya utilizado. Genera uno nuevo.";
                else if (ex.Message.Contains("HTTP 409"))
                    ActivationStatusText.Text =
                        "La instalación ya está registrada con otro usuario. Contacta soporte.";
                else if (ex.Message.Contains("HTTP 429"))
                    ActivationStatusText.Text =
                        "Demasiados intentos. Espera antes de volver a activar.";
                else
                    ActivationStatusText.Text =
                        "Cloud rechazó la activación (" + ex.Message + ").";

                ActivateEngineButton.IsEnabled = true;
                return;
            }
            catch
            {
                ActivationStatusText.Text =
                    "No fue posible conectar con MaCPro. Verifica tu conexión e intenta nuevamente.";

                ActivateEngineButton.IsEnabled = true;

                return;
            }
            if (string.IsNullOrWhiteSpace(installationCredential))
            {
                ActivationStatusText.Text =
                    "El código de activación no es válido o ha expirado.";

                ActivateEngineButton.IsEnabled = true;

                return;
            }

            MaCLocalCredentialStore credentialStore =
                new MaCLocalCredentialStore();

            credentialStore.SaveInstallationCredential(
                installationCredential);

            ActivationStatusText.Text =
                "MaCPro fue activado correctamente.";
            RefreshAccountsRequested?.Invoke(
    this,
    EventArgs.Empty);
            ActivationOverlay.Visibility =
                Visibility.Collapsed;
        }

        private void UpdateActivationOverlay()
        {
            MaCCloudOptions? cloudOptions =
                MaCCloudConfiguration.TryLoadBase();

            bool isActivated =
                cloudOptions != null &&
                !string.IsNullOrWhiteSpace(
                    cloudOptions.InstallationCredential);

            ActivationOverlay.Visibility =
                isActivated
                    ? Visibility.Collapsed
                    : Visibility.Visible;
        }

        private void OnDashboardLoaded(
            object sender,
            RoutedEventArgs e)
        {
            UpdateActivationOverlay();

            /*
             * FIX DASHBOARD — el timer arranca siempre.
             * Si todavía no hay sesión, RefreshDashboard
             * simplemente no hace nada en cada tick; en
             * cuanto llegue la sesión, empieza a pintar.
             */
            if (!_refreshTimer.IsEnabled)
            {
                _refreshTimer.Start();
            }

            RefreshDashboard();
        }

        private void OnDashboardUnloaded(
            object sender,
            RoutedEventArgs e)
        {
            if (_refreshTimer.IsEnabled)
            {
                _refreshTimer.Stop();
            }

            if (_tradeHistoryWindow != null)
            {
                TradeHistoryWindow windowToClose =
                    _tradeHistoryWindow;

                _tradeHistoryWindow =
                    null;

                windowToClose.Closed -=
                    OnTradeHistoryWindowClosed;

                windowToClose.Close();
            }
        }

        private void OnRefreshTimerTick(
            object?
                sender,
            EventArgs e)
        {
            RefreshDashboard();
        }
    }
}