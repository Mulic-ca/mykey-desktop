using Microsoft.Data.Sqlite;

namespace MyKey.Desktop;

public sealed partial class MyKeyRepository
{
    private void BackupBeforeMigration()
    {
        if (!System.IO.File.Exists(DatabasePath)) return;
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM pragma_table_info('api_keys') WHERE name='deleted_at'";
        if (Convert.ToInt32(command.ExecuteScalar()) != 0) return;
        var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(DatabasePath)!, "backups", $"mykey-before-1.1.0-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db");
        ExportDatabase(path);
    }
    public void PurgeExpired(DateTimeOffset? now = null)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM api_keys WHERE deleted_at <= $cutoff; DELETE FROM accounts WHERE deleted_at <= $cutoff;";
        command.Parameters.AddWithValue("$cutoff", (now ?? DateTimeOffset.UtcNow).AddDays(-30).ToUnixTimeSeconds());
        command.ExecuteNonQuery();
    }

    private void MoveToTrash(string table, IEnumerable<int> ids)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        MoveToTrash(connection, transaction, table, ids);
        transaction.Commit();
    }

    private static void MoveToTrash(SqliteConnection connection, SqliteTransaction transaction, string table, IEnumerable<int> ids)
    {
        var batch = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var id in ids.Distinct())
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"UPDATE {table} SET deleted_at=$now, delete_batch=$batch WHERE id=$id AND deleted_at IS NULL";
            command.Parameters.AddWithValue("$now", now);
            command.Parameters.AddWithValue("$batch", batch);
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<TrashItem> LoadTrash()
    {
        PurgeExpired();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 'api_keys' AS kind, delete_batch, name AS title, deleted_at, count(*) AS count
            FROM api_keys WHERE deleted_at IS NOT NULL GROUP BY delete_batch
            UNION ALL
            SELECT 'accounts', delete_batch, COALESCE(NULLIF(website_remark,''), NULLIF(website,''), name), deleted_at, count(*)
            FROM accounts WHERE deleted_at IS NOT NULL GROUP BY delete_batch
            ORDER BY deleted_at DESC
            """;
        using var reader = command.ExecuteReader();
        var result = new List<TrashItem>();
        while (reader.Read())
            result.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(3)), reader.GetInt32(4)));
        return result;
    }

    public bool RestoreTrash(TrashItem item)
    {
        PurgeExpired();
        return ChangeTrash(item, false) > 0;
    }

    public void DeleteTrash(TrashItem item) => ChangeTrash(item, true);

    private int ChangeTrash(TrashItem item, bool permanent)
    {
        var table = item.Kind == "api_keys" ? "api_keys" : item.Kind == "accounts" ? "accounts" : throw new ArgumentException("无效记录类型");
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = permanent
            ? $"DELETE FROM {table} WHERE delete_batch=$batch AND deleted_at IS NOT NULL"
            : $"UPDATE {table} SET deleted_at=NULL, delete_batch=NULL WHERE delete_batch=$batch AND deleted_at IS NOT NULL";
        command.Parameters.AddWithValue("$batch", item.Batch);
        return command.ExecuteNonQuery();
    }

    public void SaveCardOrder(bool api, IEnumerable<IReadOnlyList<int>> groups)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        int order = 0;
        foreach (var ids in groups)
        {
            foreach (var id in ids)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = $"UPDATE {(api ? "api_keys" : "accounts")} SET card_order=$order WHERE id=$id AND deleted_at IS NULL";
                command.Parameters.AddWithValue("$order", order);
                command.Parameters.AddWithValue("$id", id);
                command.ExecuteNonQuery();
            }
            order++;
        }
        transaction.Commit();
    }
}

public sealed record TrashItem(string Kind, string Batch, string Title, DateTimeOffset DeletedAt, int Count)
{
    public string Description => $"{(Kind == "api_keys" ? "API" : $"账号 · {Count} 个")} · {DeletedAt.LocalDateTime:yyyy-MM-dd HH:mm} 删除 · 剩余 {Math.Max(0, (int)Math.Ceiling((DeletedAt.AddDays(30) - DateTimeOffset.UtcNow).TotalDays))} 天";
}

public static class TagNames
{
    public static List<string> Parse(string text) => Normalize(text.Split([',', '，', ';', '；', '\n'], StringSplitOptions.RemoveEmptyEntries));
    public static List<string> Normalize(IEnumerable<string> tags) => tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
