using MaC.NinjaTrader.Services;
using NinjaTrader.Cbi;

namespace MaC.NinjaTrader.Adapters;

public class NinjaAccountListener
{
    private readonly NinjaAccountAdapter _adapter = new();

    public void Start()
    {
        foreach (Account account in Account.All)
        {
            account.ExecutionUpdate -= OnExecutionUpdate;
            account.ExecutionUpdate += OnExecutionUpdate;
        }
    }

    public void Stop()
    {
        foreach (Account account in Account.All)
        {
            account.ExecutionUpdate -= OnExecutionUpdate;
        }
    }

    private void OnExecutionUpdate(object sender, ExecutionEventArgs e)
    {
        var ninjaAccount = sender as Account;

        if (ninjaAccount == null)
        {
            return;
        }

        var context =
            NinjaTradingContextFactory.Create(ninjaAccount);

        global::NinjaTrader.Code.Output.Process(
            $"MaC | " +
            $"Cuenta: {ninjaAccount.Name} | " +
            $"Instrumento: {e.Execution.Instrument?.FullName} | " +
            $"Cantidad: {e.Quantity} | " +
            $"Precio: {e.Price} | " +
            $"Saldo: {context.Account.ClosedBalance}",
            global::NinjaTrader.NinjaScript.PrintTo.OutputTab1);
    }
}