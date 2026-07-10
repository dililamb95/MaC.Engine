using MaC.Core.Interfaces;

#if NET48
using System.Runtime.Serialization.Json;
using System.Text;
#else
using System.Text.Json;
#endif

namespace MaC.Core.Persistence;

public class AccountManagerStorage : IAccountManagerStorage
{
    public void Save(AccountManagerState state, string filePath)
    {
#if NET48
        var serializer = new DataContractJsonSerializer(
            typeof(AccountManagerState));

        using var stream = new MemoryStream();
        serializer.WriteObject(stream, state);

        var json = Encoding.UTF8.GetString(stream.ToArray());
        File.WriteAllText(filePath, json);
#else
        var json = JsonSerializer.Serialize(
            state,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        File.WriteAllText(filePath, json);
#endif
    }

    public AccountManagerState Load(string filePath)
    {
#if NET48
        var json = File.ReadAllText(filePath);
        var bytes = Encoding.UTF8.GetBytes(json);

        using var stream = new MemoryStream(bytes);

        var serializer = new DataContractJsonSerializer(
            typeof(AccountManagerState));

        return serializer.ReadObject(stream) as AccountManagerState
               ?? new AccountManagerState();
#else
        var json = File.ReadAllText(filePath);

        return JsonSerializer.Deserialize<AccountManagerState>(json)
               ?? new AccountManagerState();
#endif
    }
}