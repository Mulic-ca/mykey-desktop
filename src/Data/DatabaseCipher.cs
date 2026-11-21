using System.IO;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace MyKey.Desktop;

internal static class DatabaseCipher
{
    internal const string KeyFile = ".mykey-db.key";
    private static readonly object KeyGate = new();

    // Microsoft.Data.Sqlite.Core has no bundled native engine; fail closed if the backend isn't SQLCipher.
    static DatabaseCipher()
    {
        SQLitePCL.Batteries_V2.Init();
        using var connection = new SqliteConnection("Data Source=:memory:;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA cipher_version";
        if (command.ExecuteScalar() is not string { Length: > 0 })
            throw new InvalidOperationException("未能加载数据库加密组件。");
    }

    internal static bool IsPlaintext(string path)
    {
        using var input = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        Span<byte> header = stackalloc byte[16];
        return input.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) == header.Length
            && header.SequenceEqual("SQLite format 3\0"u8);
    }

    internal static string LocalKey(string path, bool allowCreate)
    {
        var keyPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, KeyFile);
        lock (KeyGate)
        {
            byte[]? key = null;
            try
            {
                if (File.Exists(keyPath))
                    key = ProtectedData.Unprotect(File.ReadAllBytes(keyPath), null, DataProtectionScope.CurrentUser);
                else if (allowCreate)
                {
                    key = RandomNumberGenerator.GetBytes(32);
                    var protectedKey = ProtectedData.Protect(key, null, DataProtectionScope.CurrentUser);
                    var temporary = keyPath + "." + Guid.NewGuid().ToString("N");
                    try
                    {
                        using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        { output.Write(protectedKey); output.Flush(flushToDisk: true); }
                        File.Move(temporary, keyPath);
                    }
                    finally { File.Delete(temporary); }
                }
                if (key is not { Length: 32 })
                    throw new InvalidDataException("找不到有效的本机数据库密钥。请使用原 Windows 账户恢复密钥，或从加密备份恢复。");
                return "x'" + Convert.ToHexString(key) + "'";
            }
            catch (CryptographicException)
            { throw new InvalidDataException("当前 Windows 账户无法解锁数据库密钥。原数据库没有被覆盖。"); }
            finally { if (key is not null) CryptographicOperations.ZeroMemory(key); }
        }
    }

    internal static SqliteConnection Open(string path, string? key, SqliteOpenMode mode, bool pooling = false, int timeout = 30)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Password = key ?? "", Mode = mode, Pooling = pooling, DefaultTimeout = timeout
        }.ToString());
        try
        {
            connection.Open();
            using var command = connection.CreateCommand();
            // Do not spill plaintext temporary sort tables to disk.
            command.CommandText = "PRAGMA temp_store=MEMORY; PRAGMA trusted_schema=OFF;";
            command.ExecuteNonQuery();
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    internal static void Copy(string source, string? sourceKey, string destination, string destinationKey)
    {
        // Make the keyed destination the main database; SQLCipher exports logical rows, including committed WAL changes.
        using var connection = Open(destination, destinationKey, SqliteOpenMode.ReadWriteCreate);
        using var command = connection.CreateCommand();
        command.CommandText = "ATTACH DATABASE $path AS input KEY $key";
        command.Parameters.AddWithValue("$path", new Uri(source).AbsoluteUri + "?mode=ro");
        command.Parameters.AddWithValue("$key", sourceKey ?? "");
        command.ExecuteNonQuery();
        command.Parameters.Clear();
        command.CommandText = "PRAGMA input.user_version";
        var version = Convert.ToInt32(command.ExecuteScalar());
        command.CommandText = "PRAGMA input.application_id";
        var applicationId = Convert.ToInt32(command.ExecuteScalar());
        command.CommandText = "SELECT sqlcipher_export('main','input')";
        command.ExecuteScalar();
        command.CommandText = FormattableString.Invariant($"PRAGMA main.user_version={version}; PRAGMA main.application_id={applicationId}; DETACH DATABASE input;");
        command.ExecuteNonQuery();
    }

    internal static void PrepareReplacement(string path, string? key)
    {
        SqliteConnection.ClearAllPools();
        using var connection = Open(path, key, SqliteOpenMode.ReadWrite);
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=DELETE";
        if (!string.Equals(Convert.ToString(command.ExecuteScalar()), "delete", StringComparison.OrdinalIgnoreCase))
            throw new IOException("数据库另有程序正在使用，请关闭后重试。");
    }

    internal static void CheckPassword(string password)
    {
        // SQLCipher interprets the raw-key literal specially; portable passwords always use its passphrase KDF.
        if (password.Length < 12 || password.Length > 256 || password.Contains('\0') || password.StartsWith("x'", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("备份密码须为 12–256 个字符，且不能以 x' 开头或包含空字符。");
    }
}
