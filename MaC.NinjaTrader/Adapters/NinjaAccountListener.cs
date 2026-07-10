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
        // Por ahora solo verificamos que el evento llegue.
        // En el siguiente paso construiremos el TradingContext
        // y enviaremos la operación al AccountEngine.
    }
}