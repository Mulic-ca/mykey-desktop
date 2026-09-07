using System.Text.RegularExpressions;
using System.Windows;
using System.ComponentModel;
using System.Windows.Media;

namespace MyKey.Desktop;

public sealed class ApiKeyRecord
{
    private string? _searchText;

    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Website { get; init; } = "";
    public string BaseUrl { get; init; } = "";
    public string ApiKey { get; init; } = "";
    public string DefaultUrl { get; init; } = "";
    public string DefaultModel { get; init; } = "";
    public IReadOnlyList<string> Models { get; init; } = [];
    public IReadOnlyList<ManualModelRecord> ManualModels { get; init; } = [];
    public IReadOnlyList<AltUrlRecord> AltUrls { get; init; } = [];
    public bool IsPinned { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public int CardOrder { get; init; }

    public string EffectiveUrl => string.IsNullOrWhiteSpace(DefaultUrl) ? BaseUrl : DefaultUrl;
    public int ModelCount => ModelTags.Length;
    public string ModelCountLabel => ModelCount.ToString();
    public Visibility ModelSectionVisibility => ModelCount == 0 ? Visibility.Collapsed : Visibility.Visible;
    public IReadOnlyList<AltUrlRecord> DisplayAltUrls => AltUrls.Count > 0
        ? AltUrls
        : [new AltUrlRecord { Url = BaseUrl, CompatType = "地址", IsDefault = true }];

    public string[] ModelTags
    {
        get
        {
            var merged = Models
                .Concat(ManualModels.Select(m => m.Name))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var defaultModel = DefaultModel.Trim();
            var rest = merged
                .Where(m => !string.Equals(m, defaultModel, StringComparison.OrdinalIgnoreCase))
                .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!string.IsNullOrWhiteSpace(defaultModel))
                rest.Insert(0, defaultModel);

            return rest.ToArray();
        }
    }

    public IReadOnlyList<ModelTagView> ModelTagViews
    {
        get
        {
            var defaultModel = DefaultModel.Trim();
            return ModelTags.Select((name, index) => new ModelTagView
            {
                Name = name,
                IsDefault = index == 0 && !string.IsNullOrWhiteSpace(defaultModel)
            }).ToArray();
        }
    }

    public ModelTagView? DisplayDefaultModel => ModelTagViews.FirstOrDefault();
    public string PinActionLabel => IsPinned ? "取消置顶" : "置顶";
    public string SearchText => _searchText ??= SearchIndex.Build(
        Name,
        string.Join(' ', Tags),
        Website,
        BaseUrl,
        DefaultUrl,
        ApiKey,
        DefaultModel,
        string.Join(' ', DisplayAltUrls.Select(url => $"{url.Url} {url.CompatType}")),
        string.Join(' ', ModelTags));

    public string CopyBundle()
    {
        var parts = new List<string> { $"Base URL: {EffectiveUrl}", $"API key: {ApiKey}" };
        if (!string.IsNullOrWhiteSpace(DefaultModel))
            parts.Add($"模型: {DefaultModel}");
        return string.Join(", ", parts);
    }

}

public sealed class AltUrlRecord
{
    public string Url { get; init; } = "";
    public string CompatType { get; init; } = "";
    public bool IsDefault { get; init; }
    public string KindLabel => string.IsNullOrWhiteSpace(CompatType) ? "地址" : CompatType;
    public string DefaultBadge => IsDefault ? "默认" : "";
}

public sealed class ManualModelRecord
{
    public string Name { get; init; } = "";
    public bool IsDefault { get; init; }
}

public sealed class ModelTagView
{
    public string Name { get; init; } = "";
    public bool IsDefault { get; init; }
    public string DefaultBadge => IsDefault ? "默认" : "";
}

public sealed class AccountRecord
{
    private string? _searchText;

    public int Id { get; init; }
    public string Remark { get; init; } = "";
    public string Name { get; init; } = "";
    public string Website { get; init; } = "";
    public string WebsiteSub { get; init; } = "";
    public string WebsiteRemark { get; init; } = "";
    public string Password { get; init; } = "";
    public IReadOnlyList<string> Emails { get; init; } = [];
    public IReadOnlyList<string> Phones { get; init; } = [];
    public string SpecialNote { get; init; } = "";
    public int SortOrder { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public int CardOrder { get; init; }
    public bool IsPinned { get; init; }

    public string RemarkLabel => string.IsNullOrWhiteSpace(Remark) ? "账号" : Remark;
    public Visibility EmailsVisibility => Emails.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    public Visibility PhonesVisibility => Phones.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ContactEmptyVisibility => Emails.Count == 0 && Phones.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public string ContactSummary
    {
        get
        {
            var parts = new List<string>();
            if (Emails.Count > 0)
                parts.Add("邮箱：" + string.Join(" / ", Emails));
            if (Phones.Count > 0)
                parts.Add("手机：" + string.Join(" / ", Phones));
            return parts.Count == 0 ? "无绑定邮箱或手机" : string.Join(Environment.NewLine, parts);
        }
    }

    public string NotePreview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SpecialNote))
                return "";

            var compact = SpecialNote.Replace("\r", " ").Replace("\n", " ").Trim();
            return compact.Length > 90 ? compact[..90] + "..." : compact;
        }
    }

    public Visibility NoteButtonVisibility => string.IsNullOrWhiteSpace(SpecialNote) ? Visibility.Collapsed : Visibility.Visible;

    public string SearchText => _searchText ??= SearchIndex.Build(
        Remark,
        string.Join(' ', Tags),
        Name,
        Website,
        WebsiteSub,
        WebsiteRemark,
        Password,
        string.Join(' ', Emails),
        string.Join(' ', Phones),
        SpecialNote);

    public string CopyBundle()
    {
        var lines = new List<string> { $"Website: {WebsiteTitle}", $"Account: {Name}", $"Password: {Password}" };
        if (Emails.Count > 0)
            lines.Add($"Emails: {string.Join(", ", Emails)}");
        if (Phones.Count > 0)
            lines.Add($"Phones: {string.Join(", ", Phones)}");
        if (!string.IsNullOrWhiteSpace(SpecialNote))
            lines.Add($"Note: {SpecialNote}");
        return string.Join(Environment.NewLine, lines);
    }

    public string WebsiteTitle => string.IsNullOrWhiteSpace(WebsiteRemark)
        ? (string.IsNullOrWhiteSpace(Website) ? "未命名网站" : Website)
        : WebsiteRemark;
}

public sealed class AccountGroupView : INotifyPropertyChanged
{
    private string? _ownSearchText;
    private string? _searchText;

    public string Key { get; init; } = "";
    public string Website { get; init; } = "";
    public string WebsiteSub { get; init; } = "";
    public string WebsiteRemark { get; init; } = "";
    public IReadOnlyList<AccountRecord> Accounts { get; init; } = [];
    private int _activeIndex;
    public int ActiveIndex
    {
        get => _activeIndex;
        set
        {
            if (_activeIndex == value) return;
            _activeIndex = value;
            PropertyChanged?.Invoke(this, new(nameof(ActiveAccount)));
            PropertyChanged?.Invoke(this, new(nameof(Tabs)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<string> Tags => TagNames.Normalize(Accounts.SelectMany(a => a.Tags));
    public int CardOrder => Accounts.Count == 0 ? 0 : Accounts.Min(a => a.CardOrder);

    public AccountRecord ActiveAccount => Accounts.Count == 0 ? new AccountRecord() : Accounts[Math.Clamp(ActiveIndex, 0, Accounts.Count - 1)];
    public bool IsPinned => Accounts.Any(account => account.IsPinned);
    public string PinActionLabel => IsPinned ? "取消置顶" : "置顶";
    public string WebsiteTitle => string.IsNullOrWhiteSpace(WebsiteRemark)
        ? (string.IsNullOrWhiteSpace(Website) ? ActiveAccount.WebsiteTitle : Website)
        : WebsiteRemark;
    public string WebsiteLinks => string.Join("   ", new[] { Website, WebsiteSub }.Where(s => !string.IsNullOrWhiteSpace(s)));
    public IReadOnlyList<WebsiteLinkView> WebsiteLinkViews => new[] { Website, WebsiteSub }
        .Where(url => !string.IsNullOrWhiteSpace(url))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(url => new WebsiteLinkView { Url = url.Trim() })
        .ToArray();
    public string AccountCountLabel => Accounts.Count <= 1 ? "1 个账号" : $"{Accounts.Count} 个账号";
    public IReadOnlyList<AccountTabView> Tabs => Accounts.Select((account, index) => new AccountTabView
    {
        GroupKey = Key,
        AccountId = account.Id,
        Index = index,
        Label = string.IsNullOrWhiteSpace(account.Remark) ? $"账号{index + 1}" : account.Remark,
        IsActive = index == ActiveIndex
    }).ToArray();
    public string OwnSearchText => _ownSearchText ??= SearchIndex.Build(Website, WebsiteSub, WebsiteRemark, WebsiteTitle, string.Join(' ', Tags));
    public string SearchText => _searchText ??= $"{OwnSearchText} {string.Join(' ', Accounts.Select(account => account.SearchText))}";
}

public sealed class WebsiteLinkView
{
    public string Url { get; init; } = "";
}

public sealed class AccountTabView
{
    public string GroupKey { get; init; } = "";
    public int AccountId { get; init; }
    public int Index { get; init; }
    public string Label { get; init; } = "";
    public bool IsActive { get; init; }
    public Brush Background => ThemeManager.Brush(IsActive ? "Primary" : "Subtle");
    public Brush Foreground => ThemeManager.Brush(IsActive ? "OnPrimary" : "Ink");
}

public sealed class ApiKeyEditData
{
    public List<string> Tags { get; set; } = [];
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Website { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string DefaultModel { get; set; } = "";
    public List<AltUrlRecord> AltUrls { get; set; } = [];
    public List<string> Models { get; set; } = [];
    public List<ManualModelRecord> ManualModels { get; set; } = [];
    public string DefaultUrl => AltUrls.FirstOrDefault(u => u.IsDefault)?.Url ?? AltUrls.FirstOrDefault()?.Url ?? "";
}

public sealed class AccountGroupEditData
{
    public List<string> Tags { get; set; } = [];
    public int CardOrder { get; set; }
    public string OriginalGroupKey { get; set; } = "";
    public string Website { get; set; } = "";
    public string WebsiteSub { get; set; } = "";
    public string WebsiteRemark { get; set; } = "";
    public bool IsPinned { get; set; }
    public List<AccountEntryEditData> Entries { get; set; } = [];
    public List<int> DeletedIds { get; set; } = [];
}

public sealed class AccountEntryEditData
{
    public int Id { get; set; }
    public string Remark { get; set; } = "";
    public string Name { get; set; } = "";
    public string Password { get; set; } = "";
    public List<string> Emails { get; set; } = [];
    public List<string> Phones { get; set; } = [];
    public string SpecialNote { get; set; } = "";
}

public static class AccountGrouping
{
    public static IReadOnlyList<AccountGroupView> BuildGroups(IEnumerable<AccountRecord> accounts, IReadOnlyDictionary<string, int> activeIndexes)
    {
        var groups = new Dictionary<string, AccountGroupViewBuilder>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        foreach (var account in accounts)
        {
            var key = NormalizeWebsite(account.Website);
            if (string.IsNullOrWhiteSpace(key))
                key = !string.IsNullOrWhiteSpace(account.WebsiteRemark)
                    ? "__remark__" + account.WebsiteRemark.Trim().ToLowerInvariant()
                    : "__no_website__" + account.Id;

            if (!groups.TryGetValue(key, out var builder))
            {
                builder = new AccountGroupViewBuilder
                {
                    Key = key,
                    Website = account.Website,
                    WebsiteSub = account.WebsiteSub,
                    WebsiteRemark = account.WebsiteRemark
                };
                groups[key] = builder;
                order.Add(key);
            }

            builder.Accounts.Add(account);
        }

        return order.Select(key =>
        {
            var builder = groups[key];
            var orderedAccounts = builder.Accounts
                .OrderBy(a => a.SortOrder)
                .ThenByDescending(a => a.Id)
                .ToArray();
            var active = activeIndexes.TryGetValue(key, out var index) ? index : 0;
            if (active < 0 || active >= orderedAccounts.Length)
                active = 0;

            return new AccountGroupView
            {
                Key = builder.Key,
                Website = builder.Website,
                WebsiteSub = builder.WebsiteSub,
                WebsiteRemark = builder.WebsiteRemark,
                Accounts = orderedAccounts,
                ActiveIndex = active
            };
        }).ToArray();
    }

    private static string NormalizeWebsite(string website)
    {
        if (string.IsNullOrWhiteSpace(website))
            return "";

        var normalized = website.Trim().ToLowerInvariant();
        normalized = Regex.Replace(normalized, "^https?://", "");
        normalized = normalized.TrimEnd('/');
        return normalized;
    }

    private sealed class AccountGroupViewBuilder
    {
        public string Key { get; init; } = "";
        public string Website { get; init; } = "";
        public string WebsiteSub { get; init; } = "";
        public string WebsiteRemark { get; init; } = "";
        public List<AccountRecord> Accounts { get; } = [];
    }
}
