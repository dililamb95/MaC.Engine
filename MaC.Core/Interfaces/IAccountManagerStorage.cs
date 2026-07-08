using MaC.Core.Persistence;

namespace MaC.Core.Interfaces;

public interface IAccountManagerStorage
{
    void Save(AccountManagerState state, string filePath);

    AccountManagerState Load(string filePath);
}