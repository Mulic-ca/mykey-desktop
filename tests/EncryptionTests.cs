using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using MyKey.Desktop;

internal static partial class Program
{
    private static void RunMaintenanceFixture()
    {
        var repo = new MyKeyRepository();
        using var connection = MyKeyRepository.OpenLocalForTests(repo.DatabasePath);
        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO api_keys (name,base_url,api_key,deleted_at) VALUES ('expired-fixture','https://fixture.invalid','fixture',1), ('recent-fixture','https://fixture.invalid','fixture',$now)";
            insert.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            insert.ExecuteNonQuery();
        }
        using (var transaction = connection.BeginTransaction())
        {
            Console.WriteLine("LOCKED");
            if (Console.ReadLine() != "release") throw new InvalidOperationException("Fixture lock was not explicitly released");
            transaction.Rollback();
        }
        var until = DateTime.UtcNow.AddSeconds(12);
        using var query = connection.CreateCommand();
        query.CommandText = "SELECT group_concat(name) FROM api_keys";
        do
        {
            if (Equals(query.ExecuteScalar(), "recent-fixture")) { Console.WriteLine("CLEANUP_OK"); return; }
            Thread.Sleep(50);
        } while (DateTime.UtcNow < until);
        throw new InvalidOperationException("Expired fixture was not removed");
    }

    private static void EncryptionTests()
    {
        const string marker = "fixture-confidential-7e65d44";
        var dir = Directory.CreateDirectory(Path.Combine(AppPaths.DataDirectory, "cipher"));
        var path = Path.Combine(dir.FullName, "mykey.db");
        using (var plain = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        {
            plain.Open();
            using var command = plain.CreateCommand();
            command.CommandText = """
                CREATE TABLE api_keys (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, website TEXT, base_url TEXT NOT NULL, api_key TEXT NOT NULL, models TEXT, default_model TEXT, created_at TEXT, updated_at TEXT);
                CREATE TABLE accounts (id INTEGER PRIMARY KEY AUTOINCREMENT, remark TEXT, name TEXT NOT NULL, website TEXT, password TEXT NOT NULL, emails TEXT, created_at TEXT, updated_at TEXT);
                INSERT INTO api_keys(id,name,base_url,api_key) VALUES (73,$marker,'https://fixture.invalid/v1',$marker);
                INSERT INTO accounts(id,name,password) VALUES (19,'demo',$marker);
                """;
            command.Parameters.AddWithValue("$marker", marker);
            command.ExecuteNonQuery();
        }
        var repo = new MyKeyRepository(path);
        Check(repo.LoadApiKeys().Single() is { Id: 73, EffectiveKey: marker } && repo.LoadAccounts().Single() is { Id: 19, Password: marker }, "plaintext migration preserves IDs and secrets");
        SqliteConnection.ClearAllPools();
        Check(!HasPlaintext(path, marker) && !CanReadWithoutKey(path), "entire working database rejects ordinary SQLite access");

        var portable = Path.Combine(dir.FullName, "portable.db");
        const string password = "测试备份 pass'word 42;中文";
        repo.ExportDatabase(portable, password);
        Check(!HasPlaintext(portable, marker) && !CanReadWithoutKey(portable), "portable backup encrypts content independently");
        var otherPath = Path.Combine(Directory.CreateDirectory(Path.Combine(dir.FullName, "other-profile")).FullName, "mykey.db");
        var other = new MyKeyRepository(otherPath);
        other.SaveApiKey(Api("existing-fixture"));
        SqliteConnection.ClearAllPools();
        var original = SHA256.HashData(File.ReadAllBytes(otherPath));
        bool rejected = false;
        try { other.ImportDatabase(portable, "wrong synthetic password"); }
        catch (InvalidDataException) { rejected = true; }
        Check(rejected && original.SequenceEqual(SHA256.HashData(File.ReadAllBytes(otherPath))), "wrong backup password leaves destination untouched");
        var rollback = other.ImportDatabase(portable, password);
        Check(other.LoadApiKeys().Single().EffectiveKey == marker && other.LoadAccounts().Single().Password == marker, "portable backup restores under a different local key");
        Check(!CanReadWithoutKey(rollback), "automatic restore rollback remains encrypted");
        Check(!CanReadWithoutKey(Directory.GetFiles(Path.Combine(dir.FullName, "backups"), "mykey-before-encryption-*.db").Single()), "pre-encryption fallback is already encrypted");
        using (var local = MyKeyRepository.OpenLocalForTests(path))
        {
            using var write = local.CreateCommand();
            write.CommandText = "PRAGMA journal_mode=WAL; INSERT INTO api_keys(name,base_url,api_key) VALUES ($marker,'https://fixture.invalid/v1',$marker)";
            write.Parameters.AddWithValue("$marker", marker + "-wal");
            write.ExecuteNonQuery();
            using var wal = File.Open(path + "-wal", FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var buffer = new MemoryStream();
            wal.CopyTo(buffer);
            Check(buffer.Length > 32 && buffer.ToArray().AsSpan().IndexOf(Encoding.UTF8.GetBytes(marker)) < 0, "written WAL pages do not contain fixture plaintext");
        }
        var keyPath = Path.Combine(Path.GetDirectoryName(otherPath)!, ".mykey-db.key");
        File.Move(keyPath, keyPath + ".missing");
        rejected = false;
        try { _ = new MyKeyRepository(otherPath); }
        catch (InvalidDataException) { rejected = true; }
        Check(rejected && File.Exists(otherPath) && !File.Exists(keyPath), "missing key fails closed without replacing database or generating a new key");
        File.Move(keyPath + ".missing", keyPath);
        Check(new MyKeyRepository(otherPath).LoadApiKeys().Single().EffectiveKey == marker, "restored key reopens the existing database");
    }

    private static bool HasPlaintext(string path, string marker) => File.ReadAllBytes(path).AsSpan().IndexOf(Encoding.UTF8.GetBytes(marker)) >= 0;

    private static bool CanReadWithoutKey(string path)
    {
        try
        {
            using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            c.Open();
            using var command = c.CreateCommand();
            command.CommandText = "SELECT count(*) FROM sqlite_schema";
            _ = command.ExecuteScalar();
            return true;
        }
        catch (SqliteException e) when (e.SqliteErrorCode == 26) { return false; }
    }
}
