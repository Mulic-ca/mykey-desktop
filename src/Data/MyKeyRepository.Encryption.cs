using System.IO;
using Microsoft.Data.Sqlite;

namespace MyKey.Desktop;

public sealed partial class MyKeyRepository
{
    private void MigratePlaintextDatabase(string source, bool replacing)
    {
        var staging = DatabasePath + ".migration-" + Guid.NewGuid().ToString("N") + ".db";
        try
        {
            PrepareImportedCopy(source, null, staging, backupLegacy: replacing);
            if (replacing)
            {
                DatabaseCipher.PrepareReplacement(DatabasePath, null);
                File.Replace(staging, DatabasePath, null);
            }
            else File.Move(staging, DatabasePath);
        }
        finally { File.Delete(staging); }
    }

    private void PrepareImportedCopy(string source, string? key, string staging, bool backupLegacy = false)
    {
        ValidateDatabase(source, key);
        DatabaseCipher.Copy(source, key, staging, _databaseKey);
        var candidate = new MyKeyRepository(staging, _databaseKey);
        if (backupLegacy)
        {
            var directory = Path.Combine(Path.GetDirectoryName(DatabasePath)!, "backups");
            var backup = Path.Combine(directory, $"mykey-before-encryption-{Guid.NewGuid():N}.db");
            candidate.ExportDatabase(backup);
        }
        candidate.EnsureDatabase();
        _ = candidate.LoadApiKeys();
        _ = candidate.LoadAccounts();
        ValidateDatabase(staging, _databaseKey);
        DatabaseCipher.PrepareReplacement(staging, _databaseKey);
    }

    internal static SqliteConnection OpenLocalForTests(string path) =>
        DatabaseCipher.Open(path, DatabaseCipher.LocalKey(path, false), SqliteOpenMode.ReadWrite);
}
