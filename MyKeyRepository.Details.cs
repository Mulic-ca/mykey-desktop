using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace MyKey.Desktop;

public sealed partial class MyKeyRepository
{
    private static List<ApiSecretRecord> ReadApiSecrets(string json, string legacyKey) =>
        ApiSecretRecord.Normalize(string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<ApiSecretRecord>>(json) ?? [], legacyKey);

    // Update only detected models. Edits made while the request runs must not be overwritten.
    public bool UpdateDetectedModels(ApiKeyRecord expected, IReadOnlyList<string> models)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = "SELECT api_key, api_secrets, base_url, default_url FROM api_keys WHERE id=$id AND deleted_at IS NULL";
        read.Parameters.AddWithValue("$id", expected.Id);
        using (var reader = read.ExecuteReader())
        {
            if (!reader.Read()) return false;
            var keys = ReadApiSecrets(ReadString(reader, "api_secrets"), ReadString(reader, "api_key"));
            var url = ReadString(reader, "default_url");
            if (string.IsNullOrWhiteSpace(url)) url = ReadString(reader, "base_url");
            if (url != expected.EffectiveUrl || keys.FirstOrDefault(k => k.IsDefault)?.Value != expected.EffectiveKey)
                return false;
        }
        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE api_keys SET models=$models, updated_at=CURRENT_TIMESTAMP WHERE id=$id AND deleted_at IS NULL";
        update.Parameters.AddWithValue("$id", expected.Id);
        update.Parameters.AddWithValue("$models", JsonSerializer.Serialize(models));
        var changed = update.ExecuteNonQuery() == 1;
        transaction.Commit();
        return changed;
    }

    public ContactHistory LoadContactHistory()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT kind, value FROM contact_history ORDER BY last_used DESC, value COLLATE NOCASE";
        using var reader = command.ExecuteReader();
        var emails = new List<string>();
        var phones = new List<string>();
        while (reader.Read())
            (reader.GetString(0) == "email" ? emails : phones).Add(reader.GetString(1));
        return new(emails, phones);
    }

    private static void RememberContacts(SqliteConnection connection, SqliteTransaction transaction, string kind, IEnumerable<string> values, bool seedOnly = false)
    {
        foreach (var value in values.Select(v => v.Trim()).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = seedOnly
                ? "INSERT OR IGNORE INTO contact_history(kind,value,last_used) VALUES($kind,$value,0)"
                : "INSERT INTO contact_history(kind,value,last_used) VALUES($kind,$value,$used) ON CONFLICT(kind,value) DO UPDATE SET last_used=excluded.last_used";
            command.Parameters.AddWithValue("$kind", kind);
            command.Parameters.AddWithValue("$value", value);
            if (!seedOnly) command.Parameters.AddWithValue("$used", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            command.ExecuteNonQuery();
        }
    }
}

public sealed record ContactHistory(IReadOnlyList<string> Emails, IReadOnlyList<string> Phones);
