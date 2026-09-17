using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace MyKey.Desktop;

public sealed partial class MyKeyRepository
{
    public string DatabasePath { get; }

    public MyKeyRepository(string? databasePath = null)
    {
        var dataDir = databasePath is null ? AppPaths.DataDirectory : Path.GetDirectoryName(Path.GetFullPath(databasePath))!;
        Directory.CreateDirectory(dataDir);

        DatabasePath = databasePath ?? Path.Combine(dataDir, "mykey.db");
        if (databasePath is null && !AppPaths.IsTestMode && !File.Exists(DatabasePath))
        {
            var source = FindExistingDatabase();
            if (source is not null)
                File.Copy(source, DatabasePath);
        }

        BackupBeforeMigration();
        EnsureDatabase();
        PurgeExpired();
    }

    public IReadOnlyList<ApiKeyRecord> LoadApiKeys()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM api_keys WHERE deleted_at IS NULL ORDER BY is_pinned DESC, card_order ASC, id DESC";

        using var reader = command.ExecuteReader();
        var result = new List<ApiKeyRecord>();
        while (reader.Read())
        {
            var baseUrl = ReadString(reader, "base_url");
            var defaultUrl = ReadString(reader, "default_url");
            var altUrls = NormalizeAltUrls(ParseAltUrls(ReadString(reader, "alt_urls")), defaultUrl, baseUrl);

            result.Add(new ApiKeyRecord
            {
                Id = ReadInt(reader, "id"),
                Name = ReadString(reader, "name"),
                Website = ReadString(reader, "website"),
                BaseUrl = baseUrl,
                ApiKey = ReadString(reader, "api_key"),
                Keys = ReadApiSecrets(ReadString(reader, "api_secrets"), ReadString(reader, "api_key")),
                DefaultUrl = altUrls.FirstOrDefault(u => u.IsDefault)?.Url ?? defaultUrl,
                DefaultModel = ReadString(reader, "default_model"),
                Models = ParseStringList(ReadString(reader, "models")),
                ManualModels = ParseManualModels(ReadString(reader, "manual_models")),
                AltUrls = altUrls,
                IsPinned = ReadInt(reader, "is_pinned") != 0,
                Tags = ParseStringList(ReadString(reader, "tags")),
                CardOrder = ReadInt(reader, "card_order")
            });
        }

        return result;
    }

    public IReadOnlyList<AccountRecord> LoadAccounts()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM accounts WHERE deleted_at IS NULL ORDER BY is_pinned DESC, card_order ASC, sort_order ASC, id DESC";

        using var reader = command.ExecuteReader();
        var result = new List<AccountRecord>();
        while (reader.Read())
        {
            result.Add(new AccountRecord
            {
                Id = ReadInt(reader, "id"),
                Remark = ReadString(reader, "remark"),
                Name = ReadString(reader, "name"),
                Website = ReadString(reader, "website"),
                WebsiteSub = ReadString(reader, "website_sub"),
                WebsiteRemark = ReadString(reader, "website_remark"),
                Password = ReadString(reader, "password"),
                Emails = ParseStringList(ReadString(reader, "emails")),
                Phones = ParseStringList(ReadString(reader, "phones")),
                SpecialNote = ReadString(reader, "special_note"),
                SortOrder = ReadInt(reader, "sort_order"),
                IsPinned = ReadInt(reader, "is_pinned") != 0,
                Tags = ParseStringList(ReadString(reader, "tags")),
                CardOrder = ReadInt(reader, "card_order")
            });
        }

        return result;
    }

    public void SaveApiKey(ApiKeyEditData data)
    {
        var tags = TagNames.ForSave(data.Tags);
        var altUrls = NormalizeAltUrls(data.AltUrls, data.DefaultUrl, data.DefaultUrl);
        var defaultUrl = altUrls.First(u => u.IsDefault).Url;
        var keys = ApiSecretRecord.Normalize(data.Keys, data.ApiKey);
        if (keys.Count == 0) throw new ArgumentException("至少需要一把 API Key。");

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();

        if (data.Id > 0)
        {
            command.CommandText = """
                UPDATE api_keys
                SET name = $name,
                    website = $website,
                    base_url = $default_url,
                    api_key = $api_key,
                    api_secrets = $api_secrets,
                    models = $models,
                    default_model = $default_model,
                    alt_urls = $alt_urls,
                    default_url = $default_url,
                    manual_models = $manual_models,
                    tags = $tags,
                    updated_at = CURRENT_TIMESTAMP
                WHERE id = $id
                """;
            command.Parameters.AddWithValue("$id", data.Id);
        }
        else
        {
            command.CommandText = """
                INSERT INTO api_keys (name, website, base_url, api_key, api_secrets, models, default_model, alt_urls, default_url, manual_models, tags)
                VALUES ($name, $website, $default_url, $api_key, $api_secrets, $models, $default_model, $alt_urls, $default_url, $manual_models, $tags)
                """;
        }

        command.Parameters.AddWithValue("$name", data.Name.Trim());
        command.Parameters.AddWithValue("$tags", SerializeList(tags));
        command.Parameters.AddWithValue("$website", NullIfEmpty(data.Website));
        command.Parameters.AddWithValue("$default_url", defaultUrl);
        command.Parameters.AddWithValue("$api_key", keys.First(k => k.IsDefault).Value);
        command.Parameters.AddWithValue("$api_secrets", JsonSerializer.Serialize(keys));
        command.Parameters.AddWithValue("$models", data.Models.Count == 0 ? DBNull.Value : JsonSerializer.Serialize(data.Models));
        command.Parameters.AddWithValue("$default_model", NullIfEmpty(data.DefaultModel));
        command.Parameters.AddWithValue("$alt_urls", JsonSerializer.Serialize(altUrls.Select(u => new Dictionary<string, object?>
        {
            ["url"] = u.Url,
            ["compat_type"] = u.CompatType,
            ["is_default"] = u.IsDefault
        })));
        command.Parameters.AddWithValue("$manual_models", data.ManualModels.Count == 0 ? DBNull.Value : JsonSerializer.Serialize(data.ManualModels.Select(m => new Dictionary<string, object?>
        {
            ["name"] = m.Name,
            ["isDefault"] = m.IsDefault
        })));
        command.ExecuteNonQuery();
    }

    public void DeleteApiKey(int id)
    {
        MoveToTrash("api_keys", [id]);
    }

    public void SaveAccountGroup(AccountGroupEditData data)
    {
        var tags = TagNames.ForSave(data.Tags);
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        try
        {
            MoveToTrash(connection, transaction, "accounts", data.DeletedIds);

            for (var index = 0; index < data.Entries.Count; index++)
            {
                var entry = data.Entries[index];
                if (string.IsNullOrWhiteSpace(entry.Name) || string.IsNullOrWhiteSpace(entry.Password))
                    continue;

                using var command = connection.CreateCommand();
                command.Transaction = transaction;

                if (entry.Id > 0)
                {
                    command.CommandText = """
                        UPDATE accounts
                        SET remark = $remark,
                            name = $name,
                            website = $website,
                            website_sub = $website_sub,
                            website_remark = $website_remark,
                            password = $password,
                            emails = $emails,
                            phones = $phones,
                        special_note = $special_note,
                        sort_order = $sort_order,
                        is_pinned = $is_pinned,
                        tags = $tags,
                        card_order = $card_order,
                        updated_at = CURRENT_TIMESTAMP
                        WHERE id = $id
                        """;
                    command.Parameters.AddWithValue("$id", entry.Id);
                }
                else
                {
                    command.CommandText = """
                        INSERT INTO accounts (remark, name, website, website_sub, website_remark, password, emails, phones, special_note, sort_order, is_pinned, tags, card_order)
                        VALUES ($remark, $name, $website, $website_sub, $website_remark, $password, $emails, $phones, $special_note, $sort_order, $is_pinned, $tags, $card_order)
                        """;
                }

                command.Parameters.AddWithValue("$remark", NullIfEmpty(entry.Remark));
                command.Parameters.AddWithValue("$tags", SerializeList(tags));
                command.Parameters.AddWithValue("$card_order", data.CardOrder);
                command.Parameters.AddWithValue("$name", entry.Name.Trim());
                command.Parameters.AddWithValue("$website", NullIfEmpty(data.Website));
                command.Parameters.AddWithValue("$website_sub", NullIfEmpty(data.WebsiteSub));
                command.Parameters.AddWithValue("$website_remark", data.WebsiteRemark.Trim());
                command.Parameters.AddWithValue("$password", entry.Password.Trim());
                command.Parameters.AddWithValue("$emails", SerializeList(entry.Emails));
                command.Parameters.AddWithValue("$phones", SerializeList(entry.Phones));
                command.Parameters.AddWithValue("$special_note", NullIfEmpty(entry.SpecialNote));
                command.Parameters.AddWithValue("$sort_order", index);
                command.Parameters.AddWithValue("$is_pinned", data.IsPinned ? 1 : 0);
                command.ExecuteNonQuery();
                RememberContacts(connection, transaction, "email", entry.Emails);
                RememberContacts(connection, transaction, "phone", entry.Phones);
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public void DeleteAccounts(IEnumerable<int> ids)
    {
        MoveToTrash("accounts", ids);
    }

    public void SetApiKeyPinned(int id, bool isPinned)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE api_keys SET is_pinned = $is_pinned WHERE id = $id";
        command.Parameters.AddWithValue("$is_pinned", isPinned ? 1 : 0);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void SetAccountGroupPinned(IEnumerable<int> ids, bool isPinned)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var id in ids.Distinct())
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE accounts SET is_pinned = $is_pinned WHERE id = $id";
            command.Parameters.AddWithValue("$is_pinned", isPinned ? 1 : 0);
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void ExportDatabase(string destinationPath)
    {
        var destination = Path.GetFullPath(destinationPath);
        if (string.Equals(destination, Path.GetFullPath(DatabasePath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("不能覆盖当前正在使用的数据库");

        var destinationDirectory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
            Directory.CreateDirectory(destinationDirectory);

        var temporaryPath = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using var source = OpenConnection();
            using var target = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = temporaryPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString());
            target.Open();
            source.BackupDatabase(target);
            target.Close();
            source.Close();

            File.Move(temporaryPath, destination, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public string ImportDatabase(string sourcePath)
    {
        var source = Path.GetFullPath(sourcePath);
        if (string.Equals(source, Path.GetFullPath(DatabasePath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("所选文件就是当前数据库");

        ValidateDatabase(source);

        var dataDirectory = Path.GetDirectoryName(DatabasePath)
            ?? throw new InvalidOperationException("无法确定数据库目录");
        var stagingPath = Path.Combine(dataDirectory, ".import-" + Guid.NewGuid().ToString("N") + ".db");
        var backupDirectory = Path.Combine(dataDirectory, "backups");
        Directory.CreateDirectory(backupDirectory);
        var backupPath = GetUniqueBackupPath(backupDirectory);

        using (var input = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = source, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        using (var staging = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = stagingPath, Pooling = false }.ToString()))
        {
            input.Open();
            staging.Open();
            input.BackupDatabase(staging);
        }
        try
        {
            ValidateDatabase(stagingPath);
            SqliteConnection.ClearAllPools();
            File.Replace(stagingPath, DatabasePath, backupPath, true);

            try
            {
                EnsureDatabase();
                _ = LoadApiKeys();
                _ = LoadAccounts();
            }
            catch
            {
                SqliteConnection.ClearAllPools();
                File.Copy(backupPath, DatabasePath, true);
                EnsureDatabase();
                throw;
            }
        }
        finally
        {
            if (File.Exists(stagingPath))
                File.Delete(stagingPath);
        }

        return backupPath;
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={DatabasePath}");
        connection.Open();
        return connection;
    }

    private static void ValidateDatabase(string databasePath)
    {
        if (!File.Exists(databasePath))
            throw new FileNotFoundException("数据库文件不存在", databasePath);

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        connection.Open();

        using (var integrity = connection.CreateCommand())
        {
            integrity.CommandText = "PRAGMA quick_check";
            if (!string.Equals(integrity.ExecuteScalar()?.ToString(), "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("数据库完整性检查未通过");
        }

        ValidateTable(connection, "api_keys", ["id", "name", "website", "base_url", "api_key", "models", "default_model"]);
        ValidateTable(connection, "accounts", ["id", "remark", "name", "website", "password", "emails"]);
    }

    private static void ValidateTable(SqliteConnection connection, string tableName, IReadOnlyList<string> requiredColumns)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{tableName}\")";
        using var reader = command.ExecuteReader();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
            columns.Add(reader.GetString(1));

        if (columns.Count == 0)
            throw new InvalidDataException($"缺少数据表：{tableName}");

        var missing = requiredColumns.Where(column => !columns.Contains(column)).ToArray();
        if (missing.Length > 0)
            throw new InvalidDataException($"数据表 {tableName} 缺少字段：{string.Join("、", missing)}");
    }

    private static string GetUniqueBackupPath(string backupDirectory)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var candidate = Path.Combine(backupDirectory, $"mykey-before-import-{timestamp}.db");
        if (!File.Exists(candidate))
            return candidate;

        return Path.Combine(backupDirectory, $"mykey-before-import-{timestamp}-{Guid.NewGuid():N}.db");
    }

    private void EnsureDatabase()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS api_keys (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                website TEXT,
                base_url TEXT NOT NULL,
                api_key TEXT NOT NULL,
                models TEXT,
                default_model TEXT,
                alt_urls TEXT,
                default_url TEXT,
                manual_models TEXT,
                is_pinned INTEGER DEFAULT 0,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS accounts (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                remark TEXT,
                name TEXT NOT NULL,
                website TEXT,
                website_sub TEXT,
                website_remark TEXT,
                password TEXT NOT NULL,
                emails TEXT,
                phones TEXT,
                special_note TEXT,
                sort_order INTEGER DEFAULT 0,
                is_pinned INTEGER DEFAULT 0,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            );
            CREATE TABLE IF NOT EXISTS contact_history (
                kind TEXT NOT NULL,
                value TEXT NOT NULL COLLATE NOCASE,
                last_used INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (kind, value)
            );
            """;
        command.ExecuteNonQuery();

        EnsureColumn(connection, "api_keys", "alt_urls", "TEXT");
        EnsureColumn(connection, "api_keys", "api_secrets", "TEXT");
        EnsureColumn(connection, "api_keys", "default_url", "TEXT");
        EnsureColumn(connection, "api_keys", "manual_models", "TEXT");
        EnsureColumn(connection, "api_keys", "is_pinned", "INTEGER DEFAULT 0");
        EnsureColumn(connection, "accounts", "phones", "TEXT");
        EnsureColumn(connection, "accounts", "website_remark", "TEXT");
        EnsureColumn(connection, "accounts", "special_note", "TEXT");
        EnsureColumn(connection, "accounts", "sort_order", "INTEGER DEFAULT 0");
        EnsureColumn(connection, "accounts", "website_sub", "TEXT");
        EnsureColumn(connection, "accounts", "is_pinned", "INTEGER DEFAULT 0");
        foreach (var table in new[] { "api_keys", "accounts" })
        {
            EnsureColumn(connection, table, "tags", "TEXT");
            EnsureColumn(connection, table, "card_order", "INTEGER DEFAULT 0");
            EnsureColumn(connection, table, "deleted_at", "INTEGER");
            EnsureColumn(connection, table, "delete_batch", "TEXT");
        }
        // Seed contacts already saved before 1.1.5, without changing their recency.
        var accounts = LoadAccounts();
        using var transaction = connection.BeginTransaction();
        RememberContacts(connection, transaction, "email", accounts.SelectMany(a => a.Emails), seedOnly: true);
        RememberContacts(connection, transaction, "phone", accounts.SelectMany(a => a.Phones), seedOnly: true);
        transaction.Commit();
    }

    private static void EnsureColumn(SqliteConnection connection, string table, string column, string type)
    {
        using var check = connection.CreateCommand();
        check.CommandText = $"SELECT {column} FROM {table} LIMIT 1";

        try
        {
            check.ExecuteScalar();
        }
        catch (SqliteException)
        {
            using var alter = connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type}";
            alter.ExecuteNonQuery();
        }
    }

    private static string? FindExistingDatabase()
    {
        var cursor = new DirectoryInfo(AppContext.BaseDirectory);
        while (cursor is not null)
        {
            var candidate = Path.Combine(cursor.FullName, "mykey", "mykey.db");
            if (File.Exists(candidate))
                return candidate;

            cursor = cursor.Parent;
        }

        var sibling = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "mykey", "mykey.db"));
        return File.Exists(sibling) ? sibling : null;
    }

    private static IReadOnlyList<string> ParseStringList(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json)?.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray() ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<ManualModelRecord> ParseManualModels(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return [];

            var result = new List<ManualModelRecord>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var name = item.GetString() ?? "";
                    if (!string.IsNullOrWhiteSpace(name))
                        result.Add(new ManualModelRecord { Name = name });
                    continue;
                }

                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("name", out var nameElement))
                    continue;

                var modelName = nameElement.GetString() ?? "";
                if (string.IsNullOrWhiteSpace(modelName))
                    continue;

                var isDefault =
                    item.TryGetProperty("isDefault", out var isDefaultElement) && isDefaultElement.ValueKind == JsonValueKind.True
                    || item.TryGetProperty("is_default", out var snakeElement) && snakeElement.ValueKind == JsonValueKind.True;

                result.Add(new ManualModelRecord { Name = modelName, IsDefault = isDefault });
            }

            return result;
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<AltUrlRecord> ParseAltUrls(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return [];

            var result = new List<AltUrlRecord>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var url = item.TryGetProperty("url", out var urlElement) ? urlElement.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(url))
                    continue;

                var compat = "";
                if (item.TryGetProperty("compat_type", out var compatElement))
                    compat = compatElement.GetString() ?? "";
                else if (item.TryGetProperty("alias", out var aliasElement))
                    compat = aliasElement.GetString() ?? "";

                var isDefault =
                    item.TryGetProperty("is_default", out var defaultElement) && defaultElement.ValueKind == JsonValueKind.True
                    || item.TryGetProperty("isDefault", out var camelElement) && camelElement.ValueKind == JsonValueKind.True
                    || item.TryGetProperty("default", out var legacyElement) && legacyElement.ValueKind == JsonValueKind.True;

                result.Add(new AltUrlRecord { Url = url, CompatType = compat, IsDefault = isDefault });
            }

            return result;
        }
        catch
        {
            return [];
        }
    }

    private static List<AltUrlRecord> NormalizeAltUrls(IEnumerable<AltUrlRecord> source, string defaultUrl, string baseUrl)
    {
        var rows = source
            .Where(row => !string.IsNullOrWhiteSpace(row.Url))
            .Select(row => new AltUrlRecord
            {
                Url = row.Url.Trim(),
                CompatType = string.IsNullOrWhiteSpace(row.CompatType) ? "地址" : row.CompatType.Trim(),
                IsDefault = row.IsDefault
            })
            .ToList();

        if (rows.Count == 0 && !string.IsNullOrWhiteSpace(baseUrl))
            rows.Add(new AltUrlRecord { Url = baseUrl.Trim(), CompatType = "地址", IsDefault = true });

        if (rows.Count == 0)
            throw new InvalidOperationException("至少需要一个 Base URL。");

        var normalizedDefault = string.IsNullOrWhiteSpace(defaultUrl) ? baseUrl : defaultUrl;
        var defaultIndex = rows.FindIndex(row => row.IsDefault);
        if (defaultIndex < 0 && !string.IsNullOrWhiteSpace(normalizedDefault))
            defaultIndex = rows.FindIndex(row => string.Equals(row.Url, normalizedDefault, StringComparison.OrdinalIgnoreCase));
        if (defaultIndex < 0)
            defaultIndex = 0;

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            rows[i] = new AltUrlRecord { Url = row.Url, CompatType = row.CompatType, IsDefault = i == defaultIndex };
        }

        return rows;
    }

    private static string ReadString(SqliteDataReader reader, string column)
    {
        var index = reader.GetOrdinal(column);
        return reader.IsDBNull(index) ? "" : reader.GetString(index);
    }

    private static int ReadInt(SqliteDataReader reader, string column)
    {
        var index = reader.GetOrdinal(column);
        return reader.IsDBNull(index) ? 0 : reader.GetInt32(index);
    }

    private static object NullIfEmpty(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    }

    private static object SerializeList(IEnumerable<string> values)
    {
        var items = values.Select(v => v.Trim()).Where(v => !string.IsNullOrWhiteSpace(v)).ToArray();
        return items.Length == 0 ? DBNull.Value : JsonSerializer.Serialize(items);
    }
}
