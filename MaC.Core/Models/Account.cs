using MaC.Core.Enums;
using MaC.Core.Evaluations;

namespace MaC.Core.Models;

/// <summary>
/// Representa una cuenta de evaluación de Mercado al Cielo.
/// </summary>
public class Account
{
    /// <summary>
    /// Identificador único de la cuenta.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Tipo de cuenta.
    /// </summary>
    public AccountType Type { get; set; }

    /// <summary>
    /// Nombre de la cuenta.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Balance inicial de la cuenta.
    /// </summary>
    public decimal StartingBalance { get; set; }

    /// <summary>
    /// Balance cerrado actual.
    /// </summary>
    public decimal ClosedBalance { get; set; }

    /// <summary>
    /// Máximo balance histórico alcanzado.
    /// </summary>
    public decimal HighWaterMark { get; set; }

    /// <summary>
    /// Piso inicial de la cuenta.
    /// </summary>
    public decimal InitialFloor { get; set; }

    /// <summary>
    /// Piso actual de la cuenta.
    /// </summary>
    public decimal Floor { get; set; }

    /// <summary>
    /// Número de días operados.
    /// </summary>
    public int TradingDays { get; set; }

    /// <summary>
    /// Estado actual de la cuenta.
    /// </summary>
    public AccountStatus Status { get; set; } = AccountStatus.Active;

    /// <summary>
    /// Estado de la sesión de trading del día.
    /// </summary>
    public TradingDayStatus TradingDayStatus { get; set; }
}