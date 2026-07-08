using System.Text.Json;
using MaC.Core.Interfaces;

namespace MaC.Core.Persistence;

public class AccountManagerStorage : IAccountManagerStorage
{
    public void Save(AccountManagerState state, string filePath)
    {
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(filePath, json);
    }

    public AccountManagerState Load(string filePath)
    {
        var json = File.ReadAllText(filePath);

        return JsonSerializer.Deserialize<AccountManagerState>(json)
               ?? new AccountManagerState();
    }
}