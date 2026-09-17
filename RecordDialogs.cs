using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Markup;

namespace MyKey.Desktop;

public static partial class RecordDialogs
{
    private static readonly string[] CompatTypes = ["OpenAI原生", "OpenAI兼容", "Anthropic原生"];
    private static FontFamily UiFont => Application.Current.TryFindResource("AppFont") as FontFamily ?? SystemFonts.MessageFontFamily;
    private const double FieldHeight = 36;

    public static bool ShowConfirmDialog(Window owner, string message, string title = "确认删除", string confirmText = "确认删除")
    {
        var dialog = CreateDialog(owner, title, 360, 360);
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(4, 14, 4, 4)
        };
        panel.Children.Add(new Border
        {
            Width = 48,
            Height = 48,
            CornerRadius = new CornerRadius(24),
            Background = ThemeManager.Legacy(245, 245, 245),
            Child = new TextBlock
            {
                Text = "!",
                FontSize = 24,
                FontFamily = UiFont,
                FontWeight = FontWeights.SemiBold,
                Foreground = InkBrush(),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });
        panel.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 15,
            FontFamily = UiFont,
            Foreground = ThemeManager.Legacy(51, 51, 51),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            LineHeight = 23,
            Margin = new Thickness(0, 14, 0, 18),
            MaxWidth = 285
        });

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var cancel = CreateActionButton("取消", false);
        var ok = CreateActionButton(confirmText, true);
        ok.Margin = new Thickness(10, 0, 0, 0);
        cancel.Click += (_, _) => dialog.DialogResult = false;
        ok.Click += (_, _) => dialog.DialogResult = true;
        actions.Children.Add(cancel);
        actions.Children.Add(ok);
        panel.Children.Add(actions);

        dialog.Content = WrapModalContent(dialog, panel, includeActionsBorder: false);
        return dialog.ShowDialog() == true;
    }

    public static void ShowNoticeDialog(Window owner, string message, string title)
    {
        var dialog = CreateDialog(owner, title, 360, 300);
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(4, 12, 4, 4)
        };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 15,
            FontFamily = UiFont,
            Foreground = ThemeManager.Legacy(51, 51, 51),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 10, 0, 18),
            MaxWidth = 285
        });
        var ok = CreateActionButton("确定", true);
        ok.HorizontalAlignment = HorizontalAlignment.Center;
        ok.Click += (_, _) => dialog.DialogResult = true;
        panel.Children.Add(ok);
        dialog.Content = WrapModalContent(dialog, panel, includeActionsBorder: false);
        dialog.ShowDialog();
    }

    public static void ShowSpecialNoteDialog(Window owner, string note)
    {
        var dialog = CreateDialog(owner, "特殊备注", 640, 620);
        var text = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(note) ? "暂无特殊备注信息" : note,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            BorderBrush = ThemeManager.Legacy(221, 221, 221),
            BorderThickness = new Thickness(1),
            Background = ThemeManager.Legacy(250, 250, 250),
            Foreground = ThemeManager.Legacy(51, 51, 51),
            FontSize = 15,
            FontFamily = UiFont,
            Padding = new Thickness(12, 10, 12, 10),
            MinHeight = 180,
            MaxHeight = 430,
            SelectionBrush = ThemeManager.Legacy(21, 25, 31)
        };
        ApplyTextBoxStyle(text);

        var body = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        body.Children.Add(text);
        dialog.Content = WrapModalContent(dialog, body, includeActionsBorder: false);
        dialog.ShowDialog();
    }

    public static ApiKeyEditData? ShowApiKeyDialog(Window owner, ApiKeyRecord? existing)
    {
        var data = new ApiKeyEditData
        {
            Id = existing?.Id ?? 0,
            Tags = existing?.Tags.ToList() ?? [],
            Name = existing?.Name ?? "",
            Website = existing?.Website ?? "",
            ApiKey = existing?.ApiKey ?? "",
            Keys = existing?.DisplayKeys.ToList() ?? [new() { IsDefault = true }],
            DefaultModel = existing?.DefaultModel ?? "",
            Models = existing?.Models.ToList() ?? [],
            ManualModels = existing?.ManualModels.ToList() ?? [],
            AltUrls = existing?.DisplayAltUrls.ToList() ?? [new AltUrlRecord { CompatType = "OpenAI兼容", IsDefault = true }]
        };

        var dialog = CreateDialog(owner, data.Id > 0 ? "编辑 API 记录" : "新增 API 记录", 720, 780);
        var root = CreateScrollForm();
        var panel = (StackPanel)((ScrollViewer)root).Content;

        var name = AddTextBox(panel, "名称 / 备注", data.Name);
        var website = AddTextBox(panel, "官网链接", data.Website);
        var tags = new TagEditor(panel, data.Tags);
        var keyEditor = new ApiSecretEditor(panel, data.Keys);
        using var detectionCancellation = new CancellationTokenSource();
        dialog.Closed += (_, _) => detectionCancellation.Cancel();

        AddSectionTitle(panel, "Base URL");
        var urlPanel = new StackPanel();
        panel.Children.Add(urlPanel);
        var urlRows = data.AltUrls.Select(a => new AltUrlRowState(a.Url, a.CompatType, a.IsDefault)).ToList();
        if (!urlRows.Any(r => r.IsDefault) && urlRows.Count > 0)
            urlRows[0].IsDefault = true;

        void RenderUrlRows()
        {
            urlPanel.Children.Clear();
            for (var i = 0; i < urlRows.Count; i++)
            {
                var index = i;
                var row = urlRows[index];
                var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(158) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });

                row.UrlBox = new TextBox { Text = row.Url, MinHeight = FieldHeight, Padding = new Thickness(10, 5, 10, 5), FontSize = 14 };
                ApplyTextBoxStyle(row.UrlBox);
                row.CompatBox = new ComboBox { ItemsSource = CompatTypes, SelectedItem = string.IsNullOrWhiteSpace(row.CompatType) ? "OpenAI兼容" : row.CompatType, MinHeight = FieldHeight, Margin = new Thickness(8, 0, 0, 0) };
                ApplyComboBoxStyle(row.CompatBox);
                row.DefaultButton = new RadioButton { Content = "默认", IsChecked = row.IsDefault, GroupName = "api-default-url", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), FontFamily = UiFont, FontSize = 12.5 };
                row.RemoveButton = CreateActionButton("×", false);
                row.RemoveButton.Margin = new Thickness(8, 0, 0, 0);
                row.RemoveButton.MinWidth = 34;

                row.DefaultButton.Checked += (_, _) =>
                {
                    SyncUrlRows();
                    foreach (var item in urlRows)
                        item.IsDefault = false;
                    row.IsDefault = true;
                    RenderUrlRows();
                };
                row.RemoveButton.Click += (_, _) =>
                {
                    SyncUrlRows();
                    if (urlRows.Count <= 1)
                    {
                        ShowNoticeDialog(dialog, "至少需要保留一个 Base URL", "无法删除");
                        return;
                    }

                    urlRows.RemoveAt(index);
                    if (!urlRows.Any(r => r.IsDefault))
                        urlRows[0].IsDefault = true;
                    RenderUrlRows();
                };

                Grid.SetColumn(row.UrlBox, 0);
                Grid.SetColumn(row.CompatBox, 1);
                Grid.SetColumn(row.DefaultButton, 2);
                Grid.SetColumn(row.RemoveButton, 3);
                grid.Children.Add(row.UrlBox);
                grid.Children.Add(row.CompatBox);
                grid.Children.Add(row.DefaultButton);
                grid.Children.Add(row.RemoveButton);
                urlPanel.Children.Add(grid);
            }
        }

        void SyncUrlRows()
        {
            foreach (var row in urlRows)
            {
                row.Url = row.UrlBox?.Text.Trim() ?? row.Url;
                row.CompatType = row.CompatBox?.SelectedItem?.ToString() ?? row.CompatType;
                row.IsDefault = row.DefaultButton?.IsChecked == true;
            }
        }

        RenderUrlRows();
        var addUrl = CreateActionButton("+ 添加地址", false);
        addUrl.HorizontalAlignment = HorizontalAlignment.Stretch;
        addUrl.Click += (_, _) =>
        {
            SyncUrlRows();
            urlRows.Add(new AltUrlRowState("", "OpenAI兼容", urlRows.Count == 0));
            RenderUrlRows();
        };
        panel.Children.Add(addUrl);

        AddSectionTitle(panel, "模型");
        var modelHeader = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        modelHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        modelHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        modelHeader.Children.Add(new TextBlock
        {
            Text = "默认模型",
            Foreground = MutedBrush(),
            FontSize = 13,
            FontFamily = UiFont,
            VerticalAlignment = VerticalAlignment.Center
        });
        var detectedInfo = new TextBlock
        {
            Foreground = MutedBrush(),
            FontSize = 12,
            FontFamily = UiFont,
            FontWeight = FontWeights.Normal,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };
        Grid.SetColumn(detectedInfo, 1);
        modelHeader.Children.Add(detectedInfo);
        panel.Children.Add(modelHeader);

        var defaultModelRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        defaultModelRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        defaultModelRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var defaultModel = new ComboBox { IsEditable = false, MinHeight = FieldHeight };
        ApplyComboBoxStyle(defaultModel);
        defaultModelRow.Children.Add(defaultModel);
        var detectButton = CreateActionButton("检测模型", false);
        detectButton.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(detectButton, 1);
        defaultModelRow.Children.Add(detectButton);
        panel.Children.Add(defaultModelRow);

        panel.Children.Add(new TextBlock
        {
            Text = "手动添加模型",
            Foreground = MutedBrush(),
            FontSize = 13,
            FontFamily = UiFont,
            Margin = new Thickness(0, 0, 0, 6)
        });
        var manualModels = data.ManualModels
            .Where(m => !string.IsNullOrWhiteSpace(m.Name))
            .Select(m => new ManualModelEditState(m.Name.Trim(), m.IsDefault))
            .DistinctBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var manualInputRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        manualInputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        manualInputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var manualModelInput = new TextBox { MinHeight = FieldHeight, Padding = new Thickness(10, 5, 10, 5), FontSize = 14 };
        ApplyTextBoxStyle(manualModelInput);
        var addManualModel = CreateActionButton("+ 添加", false);
        addManualModel.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(addManualModel, 1);
        manualInputRow.Children.Add(manualModelInput);
        manualInputRow.Children.Add(addManualModel);
        panel.Children.Add(manualInputRow);
        var manualModelList = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(manualModelList);

        var suppressDefaultModelChange = false;

        string[] GetMergedModels()
        {
            return data.Models
                .Concat(manualModels.Select(m => m.Name))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        void RenderManualModels()
        {
            manualModelList.Children.Clear();
            foreach (var model in manualModels)
            {
                var tag = new Border
                {
                    Background = ThemeManager.Brush("Subtle"),
                    BorderBrush = ThemeManager.Brush(model.IsDefault ? "Ink" : "SoftBorder"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 3, 5, 3),
                    Margin = new Thickness(0, 0, 6, 6)
                };
                var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var defaultButton = new Button
                {
                    Content = "★",
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = model.IsDefault ? InkBrush() : ThemeManager.Legacy(153, 153, 153),
                    FontFamily = UiFont,
                    FontWeight = model.IsDefault ? FontWeights.Bold : FontWeights.Normal,
                    FontSize = 11,
                    Padding = new Thickness(0, 0, 4, 0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = model.IsDefault ? "取消默认模型" : "设为默认模型"
                };
                defaultButton.Click += (_, _) =>
                {
                    var alreadyDefault = model.IsDefault;
                    foreach (var item in manualModels)
                        item.IsDefault = false;
                    if (!alreadyDefault)
                        model.IsDefault = true;
                    UpdateModelOptions(model.IsDefault ? model.Name : "");
                };
                var nameText = new TextBlock
                {
                    Text = model.Name,
                    FontSize = 12.5,
                    FontFamily = UiFont,
                    Foreground = ThemeManager.Legacy(51, 51, 51),
                    VerticalAlignment = VerticalAlignment.Center
                };
                var remove = new Button
                {
                    Content = "×",
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = ThemeManager.Legacy(153, 153, 153),
                    FontFamily = UiFont,
                    FontSize = 16,
                    Padding = new Thickness(5, 0, 0, 0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "删除"
                };
                remove.Click += (_, _) =>
                {
                    manualModels.Remove(model);
                    UpdateModelOptions();
                };
                row.Children.Add(defaultButton);
                row.Children.Add(nameText);
                row.Children.Add(remove);
                tag.Child = row;
                manualModelList.Children.Add(tag);
            }
        }

        void UpdateModelOptions(string? preferredDefault = null)
        {
            var all = GetMergedModels();
            var manualDefault = manualModels.FirstOrDefault(m => m.IsDefault)?.Name;
            var nextDefault = manualDefault ?? preferredDefault ?? defaultModel.Text.Trim();
            if (!all.Contains(nextDefault, StringComparer.OrdinalIgnoreCase))
                nextDefault = "";
            suppressDefaultModelChange = true;
            defaultModel.ItemsSource = all;
            defaultModel.Text = nextDefault;
            suppressDefaultModelChange = false;
            detectedInfo.Text = data.Models.Count == 0 ? "尚未检测模型。" : $"已检测到 {data.Models.Count} 个模型。";
            RenderManualModels();
        }

        defaultModel.SelectionChanged += (_, _) =>
        {
            if (suppressDefaultModelChange || defaultModel.SelectedItem is null)
                return;

            foreach (var item in manualModels)
                item.IsDefault = false;
            RenderManualModels();
        };
        defaultModel.LostFocus += (_, _) =>
        {
            if (suppressDefaultModelChange)
                return;

            var current = defaultModel.Text.Trim();
            if (!manualModels.Any(m => m.IsDefault && string.Equals(m.Name, current, StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var item in manualModels)
                    item.IsDefault = false;
                RenderManualModels();
            }
        };
        void AddManualModelFromInput()
        {
            var modelName = manualModelInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(modelName))
                return;
            if (manualModels.Any(m => string.Equals(m.Name, modelName, StringComparison.OrdinalIgnoreCase)) ||
                data.Models.Any(m => string.Equals(m, modelName, StringComparison.OrdinalIgnoreCase)))
            {
                ShowNoticeDialog(dialog, "该模型已存在", "提示");
                return;
            }

            manualModels.Add(new ManualModelEditState(modelName, false));
            manualModelInput.Clear();
            UpdateModelOptions();
        }

        addManualModel.Click += (_, _) => AddManualModelFromInput();
        manualModelInput.KeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Enter)
                return;
            e.Handled = true;
            AddManualModelFromInput();
        };
        UpdateModelOptions(data.DefaultModel);

        detectButton.Click += async (_, _) =>
        {
            SyncUrlRows();
            var selectedUrl = urlRows.FirstOrDefault(r => r.IsDefault)?.Url ?? urlRows.FirstOrDefault()?.Url ?? "";
            var keys = keyEditor.GetValues();
            var selectedKey = keys.FirstOrDefault(k => k.IsDefault)?.Value ?? "";
            if (string.IsNullOrWhiteSpace(selectedUrl) || string.IsNullOrWhiteSpace(selectedKey))
            {
                ShowNoticeDialog(dialog, "请先填写默认 Base URL 和 API Key", "无法检测");
                return;
            }

            detectButton.IsEnabled = false;
            detectButton.Content = "检测中...";
            try
            {
                var detected = await ModelDetectionService.DetectAsync(selectedUrl, selectedKey, detectionCancellation.Token);
                if (!dialog.IsVisible) return;
                SyncUrlRows();
                if ((urlRows.FirstOrDefault(r => r.IsDefault)?.Url ?? "") != selectedUrl ||
                    keyEditor.GetValues().FirstOrDefault(k => k.IsDefault)?.Value != selectedKey)
                {
                    ShowNoticeDialog(dialog, "默认地址或 Key 已更改，请重新检测模型。", "配置已更改");
                    return;
                }
                data.Models = detected;
                UpdateModelOptions(defaultModel.Text.Trim());
                ShowNoticeDialog(dialog, $"检测到 {data.Models.Count} 个模型", "检测完成");
            }
            catch (Exception ex)
            {
                if (dialog.IsVisible) ShowNoticeDialog(dialog, ModelDetectionService.DescribeError(ex), "模型检测失败");
            }
            finally
            {
                detectButton.Content = "检测模型";
                detectButton.IsEnabled = true;
            }
        };
        AddActions(dialog, panel, () =>
        {
            SyncUrlRows();
            if (!tags.Validate(dialog)) return false;
            var keys = keyEditor.GetValues();
            if (string.IsNullOrWhiteSpace(name.Text) || keys.Count == 0)
            {
                ShowNoticeDialog(dialog, "名称和 API Key 必填", "缺少信息");
                return false;
            }

            var cleanedUrls = urlRows
                .Where(r => !string.IsNullOrWhiteSpace(r.Url))
                .Select(r => new AltUrlRecord { Url = r.Url.Trim(), CompatType = string.IsNullOrWhiteSpace(r.CompatType) ? "OpenAI兼容" : r.CompatType.Trim(), IsDefault = r.IsDefault })
                .ToList();
            if (cleanedUrls.Count == 0)
            {
                ShowNoticeDialog(dialog, "至少需要一个 Base URL", "缺少信息");
                return false;
            }
            if (!cleanedUrls.Any(r => r.IsDefault))
                cleanedUrls[0] = new AltUrlRecord { Url = cleanedUrls[0].Url, CompatType = cleanedUrls[0].CompatType, IsDefault = true };

            var selectedDefault = defaultModel.Text.Trim();

            data.Name = name.Text.Trim();
            data.Tags = tags.GetValues();
            data.Website = website.Text.Trim();
            data.Keys = keys;
            data.ApiKey = keys.First(k => k.IsDefault).Value;
            data.DefaultModel = selectedDefault;
            data.AltUrls = cleanedUrls;
            data.ManualModels = manualModels.Select(m => new ManualModelRecord { Name = m.Name, IsDefault = m.IsDefault }).ToList();
            return true;
        });

        dialog.Content = WrapModalContent(dialog, root, includeActionsBorder: true);
        return dialog.ShowDialog() == true ? data : null;
    }

    public static AccountGroupEditData? ShowAccountGroupDialog(Window owner, AccountGroupView? group, ContactHistory? history = null)
    {
        var data = new AccountGroupEditData
        {
            OriginalGroupKey = group?.Key ?? "",
            Tags = group?.Tags.ToList() ?? [],
            CardOrder = group?.CardOrder ?? 0,
            Website = group?.Website ?? "",
            WebsiteSub = group?.WebsiteSub ?? "",
            WebsiteRemark = group?.WebsiteRemark ?? "",
            IsPinned = group?.IsPinned ?? false,
            Entries = group?.Accounts.Select(a => new AccountEntryEditData
            {
                Id = a.Id,
                Remark = a.Remark,
                Name = a.Name,
                Password = a.Password,
                Emails = a.Emails.ToList(),
                Phones = a.Phones.ToList(),
                SpecialNote = a.SpecialNote
            }).ToList() ?? [new AccountEntryEditData()]
        };

        var dialog = CreateDialog(owner, group is null ? "新增账号记录" : "编辑账号分组", 760, 800);
        var root = CreateScrollForm();
        var panel = (StackPanel)((ScrollViewer)root).Content;

        var website = AddTextBox(panel, "主网站", data.Website);
        var websiteSub = AddTextBox(panel, "子网站", data.WebsiteSub);
        var websiteRemark = AddTextBox(panel, "网站备注名", data.WebsiteRemark);
        var tags = new TagEditor(panel, data.Tags);

        AddSectionTitle(panel, "账号");
        var tabControl = new TabControl { MinHeight = 360, Margin = new Thickness(0, 0, 0, 10) };
        ApplyTabControlStyle(tabControl);
        panel.Children.Add(tabControl);
        var entryControls = new Dictionary<AccountEntryEditData, AccountEntryControls>();

        StackPanel CreateAccountActions()
        {
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            var addAccount = CreateActionButton("+ 添加账号", false);
            var removeAccount = CreateActionButton("删除当前账号", false);
            removeAccount.Margin = new Thickness(8, 0, 0, 0);
            addAccount.Click += (_, _) =>
            {
                SyncEntries();
                data.Entries.Add(new AccountEntryEditData());
                RenderTabs();
                tabControl.SelectedIndex = data.Entries.Count - 1;
            };
            removeAccount.Click += (_, _) =>
            {
                SyncEntries();
                if (data.Entries.Count <= 1)
                {
                    ShowNoticeDialog(dialog, "至少需要保留一个账号", "无法删除");
                    return;
                }
                var index = tabControl.SelectedIndex < 0 ? 0 : tabControl.SelectedIndex;
                var removed = data.Entries[index];
                if (removed.Id > 0)
                    data.DeletedIds.Add(removed.Id);
                data.Entries.RemoveAt(index);
                RenderTabs();
                tabControl.SelectedIndex = Math.Min(index, data.Entries.Count - 1);
            };
            actions.Children.Add(addAccount);
            actions.Children.Add(removeAccount);
            return actions;
        }

        void RenderTabs()
        {
            tabControl.Items.Clear();
            entryControls.Clear();
            for (var i = 0; i < data.Entries.Count; i++)
            {
                var entry = data.Entries[i];
                var controls = CreateAccountEntryEditor(entry, history ?? new([], []));
                entryControls[entry] = controls;
                controls.Panel.Children.Insert(0, CreateAccountActions());
                tabControl.Items.Add(new TabItem
                {
                    Header = string.IsNullOrWhiteSpace(entry.Remark) ? $"账号{i + 1}" : entry.Remark,
                    Content = controls.Panel
                });
            }
            if (tabControl.Items.Count > 0)
                tabControl.SelectedIndex = Math.Clamp(tabControl.SelectedIndex, 0, tabControl.Items.Count - 1);
        }

        void SyncEntries()
        {
            foreach (var (entry, controls) in entryControls)
            {
                entry.Remark = controls.Remark.Text.Trim();
                entry.Name = controls.Name.Text.Trim();
                entry.Password = controls.Password.Text.Trim();
                entry.Emails = controls.Emails.GetValues();
                entry.Phones = controls.Phones.GetValues();
                entry.SpecialNote = controls.SpecialNote.Text.Trim();
            }
        }

        RenderTabs();

        AddActions(dialog, panel, () =>
        {
            SyncEntries();
            if (!tags.Validate(dialog)) return false;
            data.Tags = tags.GetValues();
            if (string.IsNullOrWhiteSpace(websiteRemark.Text))
            {
                ShowNoticeDialog(dialog, "网站备注名必填", "缺少信息");
                return false;
            }

            if (data.Entries.Count == 0 || data.Entries.Any(e => string.IsNullOrWhiteSpace(e.Name) || string.IsNullOrWhiteSpace(e.Password)))
            {
                ShowNoticeDialog(dialog, "每个账号都需要登录账号和密码", "缺少信息");
                return false;
            }

            data.Website = website.Text.Trim();
            data.WebsiteSub = websiteSub.Text.Trim();
            data.WebsiteRemark = websiteRemark.Text.Trim();
            return true;
        });

        dialog.Content = WrapModalContent(dialog, root, includeActionsBorder: true);
        return dialog.ShowDialog() == true ? data : null;
    }

    private static AccountEntryControls CreateAccountEntryEditor(AccountEntryEditData entry, ContactHistory history)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        var remark = AddTextBox(panel, "标签名", entry.Remark);
        var name = AddTextBox(panel, "登录账号", entry.Name);
        var password = AddTextBox(panel, "密码", entry.Password);
        var noPassword = CreateActionButton("暂无密码", false);
        noPassword.HorizontalAlignment = HorizontalAlignment.Left;
        noPassword.Margin = new Thickness(0, -8, 0, 12);
        noPassword.Click += (_, _) => password.Text = "暂无密码";
        panel.Children.Add(noPassword);
        var emails = AddContactListEditor(panel, "绑定邮箱", "email@example.com", "+ 添加邮箱", entry.Emails, history.Emails);
        var phones = AddContactListEditor(panel, "绑定手机", "手机号码", "+ 添加手机", entry.Phones, history.Phones);
        var specialNote = AddTextBox(panel, "特殊备注（支持 Markdown 文本保存）", entry.SpecialNote, true);
        specialNote.MinHeight = 90;

        return new AccountEntryControls(panel, remark, name, password, emails, phones, specialNote);
    }

    private static ContactListEditor AddContactListEditor(Panel panel, string label, string placeholder, string addButtonText, IEnumerable<string> values, IReadOnlyList<string> history)
    {
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = MutedBrush(),
            FontSize = 13,
            FontFamily = UiFont,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var rows = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
        panel.Children.Add(rows);

        var editor = new ContactListEditor(rows, placeholder, history);
        foreach (var value in values.Where(v => !string.IsNullOrWhiteSpace(v)))
            editor.AddRow(value.Trim());

        var addButton = CreateDashedAddButton(addButtonText);
        addButton.Margin = new Thickness(0, 0, 0, 14);
        addButton.Click += (_, _) =>
        {
            var input = editor.AddRow("");
            input.Focus();
        };
        panel.Children.Add(addButton);

        return editor;
    }

    private static Button CreateDashedAddButton(string text)
    {
        var button = new Button
        {
            Content = text,
            MinHeight = FieldHeight,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(12, 0, 12, 0),
            Background = ThemeManager.Brush("Panel"),
            Foreground = ThemeManager.Legacy(137, 145, 153),
            BorderBrush = ThemeManager.Legacy(204, 204, 204),
            FontFamily = UiFont,
            FontSize = 13,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        button.Template = (ControlTemplate)ThemeManager.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             TargetType="{x:Type Button}">
                <Grid SnapsToDevicePixels="True">
                    <Rectangle x:Name="Chrome"
                               RadiusX="4"
                               RadiusY="4"
                               Fill="{TemplateBinding Background}"
                               Stroke="{TemplateBinding BorderBrush}"
                               StrokeThickness="1"
                               StrokeDashArray="3 2"/>
                    <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                                      VerticalAlignment="{TemplateBinding VerticalContentAlignment}"
                                      Margin="{TemplateBinding Padding}"/>
                </Grid>
                <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                        <Setter TargetName="Chrome" Property="Stroke" Value="#999999"/>
                        <Setter Property="Foreground" Value="#333333"/>
                        <Setter Property="Background" Value="#FAFAFA"/>
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
            """);
        return button;
    }

    private static Button CreateContactRemoveButton()
    {
        var button = new Button
        {
            Content = "×",
            Width = FieldHeight,
            MinHeight = FieldHeight,
            Padding = new Thickness(0),
            Background = ThemeManager.Legacy(245, 245, 245),
            Foreground = ThemeManager.Legacy(118, 118, 118),
            BorderBrush = ThemeManager.Legacy(221, 221, 221),
            BorderThickness = new Thickness(1),
            FontFamily = UiFont,
            FontSize = 17,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        button.Template = (ControlTemplate)ThemeManager.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             TargetType="{x:Type Button}">
                <Border x:Name="Chrome"
                        Background="{TemplateBinding Background}"
                        BorderBrush="{TemplateBinding BorderBrush}"
                        BorderThickness="{TemplateBinding BorderThickness}"
                        CornerRadius="4">
                    <ContentPresenter HorizontalAlignment="Center"
                                      VerticalAlignment="Center"
                                      Margin="{TemplateBinding Padding}"/>
                </Border>
                <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                        <Setter TargetName="Chrome" Property="Background" Value="#FFF5F5"/>
                        <Setter TargetName="Chrome" Property="BorderBrush" Value="#F0B6B6"/>
                        <Setter Property="Foreground" Value="#B42318"/>
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
            """);
        return button;
    }

    private static Window CreateDialog(Window owner, string title, double width, double maxHeight)
    {
        var window = new Window
        {
            Owner = owner,
            Title = title,
            Width = width,
            MaxHeight = maxHeight,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            ShowInTaskbar = false,
            FontFamily = UiFont,
            Background = Brushes.Transparent
        };
        return window;
    }

    private static Border WrapModalContent(Window dialog, UIElement content, bool includeActionsBorder)
    {
        var close = new Button
        {
            Content = "×",
            Background = Brushes.Transparent,
            Foreground = ThemeManager.Legacy(153, 153, 153),
            BorderThickness = new Thickness(0),
            FontSize = 24,
            Padding = new Thickness(8, 0, 8, 2),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        close.FontFamily = UiFont;
        close.Click += (_, _) => dialog.DialogResult = false;

        var header = new Grid { Margin = new Thickness(20, 15, 14, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = dialog.Title,
            FontSize = 18,
            FontFamily = UiFont,
            FontWeight = FontWeights.SemiBold,
            Foreground = InkBrush(),
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(close, 1);
        header.Children.Add(close);

        var body = new Border
        {
            Padding = new Thickness(20),
            Child = content
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(header);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        return new Border
        {
            Background = ThemeManager.Brush("Panel"),
            CornerRadius = new CornerRadius(10),
            BorderBrush = ThemeManager.Legacy(228, 232, 235),
            BorderThickness = new Thickness(1),
            Child = root,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 3,
                Opacity = 0.12
            },
            Margin = new Thickness(0)
        };
    }

    private static ScrollViewer CreateScrollForm()
    {
        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = ThemeManager.Brush("Panel"),
            Content = new StackPanel()
        };
    }

    private static void AddSectionTitle(Panel panel, string text)
    {
        panel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 16,
            FontFamily = UiFont,
            FontWeight = FontWeights.SemiBold,
            Foreground = InkBrush(),
            Margin = new Thickness(0, 12, 0, 12)
        });
    }

    private static FrameworkElement Labeled(string label, FrameworkElement control)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = label, Foreground = MutedBrush(), FontSize = 13, FontFamily = UiFont, Margin = new Thickness(0, 0, 0, 6) });
        panel.Children.Add(control);
        panel.Margin = new Thickness(0, 0, 0, 12);
        return panel;
    }

    private static TextBox AddTextBox(Panel panel, string label, string value, bool multiline = false)
    {
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = MutedBrush(),
            FontSize = 13,
            FontFamily = UiFont,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var box = new TextBox
        {
            Text = value,
            MinHeight = multiline ? 92 : FieldHeight,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            AcceptsReturn = multiline,
            VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
            BorderBrush = ThemeManager.Legacy(211, 217, 222),
            BorderThickness = new Thickness(1),
            Background = ThemeManager.Brush("Panel"),
            Padding = new Thickness(10, 5, 10, 5),
            FontSize = 14,
            FontFamily = UiFont,
            Margin = new Thickness(0, 0, 0, 14)
        };
        ApplyTextBoxStyle(box);

        panel.Children.Add(box);
        return box;
    }

    private static void ApplyTextBoxStyle(TextBox box)
    {
        box.FontFamily = UiFont;
        box.Foreground = ThemeManager.Brush("Ink");
        box.Background = ThemeManager.Brush("Panel");
        box.CaretBrush = ThemeManager.Brush("Ink");
        box.FontWeight = FontWeights.Normal;
        box.Template = (ControlTemplate)ThemeManager.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             TargetType="{x:Type TextBox}"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Border x:Name="Chrome"
                        Background="{TemplateBinding Background}"
                        BorderBrush="{TemplateBinding BorderBrush}"
                        BorderThickness="{TemplateBinding BorderThickness}"
                        CornerRadius="4"
                        SnapsToDevicePixels="True">
                    <ScrollViewer x:Name="PART_ContentHost"
                                  Margin="{TemplateBinding Padding}"
                                  VerticalAlignment="Center"/>
                </Border>
                <ControlTemplate.Triggers>
                    <Trigger Property="IsKeyboardFocused" Value="True">
                        <Setter TargetName="Chrome" Property="BorderBrush" Value="#15191F"/>
                    </Trigger>
                    <Trigger Property="IsEnabled" Value="False">
                        <Setter TargetName="Chrome" Property="Opacity" Value="0.55"/>
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
            """);
    }

    private static void ApplyComboBoxStyle(ComboBox combo)
    {
        combo.SetResourceReference(Control.BorderBrushProperty, "SoftBorder");
        combo.BorderThickness = new Thickness(1);
        combo.SetResourceReference(Control.BackgroundProperty, "Panel");
        combo.SetResourceReference(Control.ForegroundProperty, "Ink");
        combo.Padding = new Thickness(9, 5, 30, 5);
        combo.FontSize = 14;
        combo.FontFamily = UiFont;
        combo.FontWeight = FontWeights.Normal;
        combo.ItemContainerStyle = new Style(typeof(ComboBoxItem))
        {
            Setters =
            {
                new Setter(Control.FontFamilyProperty, UiFont),
                new Setter(Control.FontWeightProperty, FontWeights.Normal),
                new Setter(Control.FontSizeProperty, 13.5),
                new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Ink")),
                new Setter(Control.PaddingProperty, new Thickness(10, 7, 10, 7))
            }
        };
        combo.Template = (ControlTemplate)ThemeManager.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             TargetType="{x:Type ComboBox}">
                <Grid>
                    <ToggleButton x:Name="ToggleButton"
                                  Focusable="False"
                                  ClickMode="Press"
                                  IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}">
                        <ToggleButton.Template>
                            <ControlTemplate TargetType="{x:Type ToggleButton}">
                                <Border x:Name="Chrome"
                                        Background="{Binding Background, RelativeSource={RelativeSource AncestorType=ComboBox}}"
                                        BorderBrush="{Binding BorderBrush, RelativeSource={RelativeSource AncestorType=ComboBox}}"
                                        BorderThickness="{Binding BorderThickness, RelativeSource={RelativeSource AncestorType=ComboBox}}"
                                        CornerRadius="4">
                                    <Grid>
                                        <Path Data="M6,9 L10,13 L14,9"
                                              Stroke="#666666"
                                              StrokeThickness="1.7"
                                              StrokeStartLineCap="Round"
                                              StrokeEndLineCap="Round"
                                              HorizontalAlignment="Right"
                                              VerticalAlignment="Center"
                                              Margin="0,0,10,0"/>
                                    </Grid>
                                </Border>
                                <ControlTemplate.Triggers>
                                    <Trigger Property="IsMouseOver" Value="True">
                                        <Setter TargetName="Chrome" Property="BorderBrush" Value="#BBBBBB"/>
                                    </Trigger>
                                </ControlTemplate.Triggers>
                            </ControlTemplate>
                        </ToggleButton.Template>
                    </ToggleButton>
                    <ContentPresenter x:Name="ContentSite"
                                      IsHitTestVisible="False"
                                      Content="{TemplateBinding SelectionBoxItem}"
                                      ContentStringFormat="{TemplateBinding SelectionBoxItemStringFormat}"
                                      Margin="{TemplateBinding Padding}"
                                      VerticalAlignment="Center"
                                      HorizontalAlignment="Left"/>
                    <TextBox x:Name="PART_EditableTextBox"
                             Foreground="{TemplateBinding Foreground}"
                             CaretBrush="{TemplateBinding Foreground}"
                             Margin="{TemplateBinding Padding}"
                             FontFamily="{TemplateBinding FontFamily}"
                             FontWeight="{TemplateBinding FontWeight}"
                             Background="Transparent"
                             BorderThickness="0"
                             VerticalAlignment="Center"
                             Visibility="Hidden"
                             Focusable="True"/>
                    <Popup x:Name="PART_Popup"
                           Placement="Bottom"
                           IsOpen="{TemplateBinding IsDropDownOpen}"
                           AllowsTransparency="True"
                           Focusable="False"
                           PopupAnimation="Slide">
                        <Border Background="White"
                                BorderBrush="#DDDDDD"
                                BorderThickness="1"
                                CornerRadius="4"
                                MinWidth="{TemplateBinding ActualWidth}"
                                MaxHeight="260">
                            <ScrollViewer Margin="2" CanContentScroll="True">
                                <ItemsPresenter/>
                            </ScrollViewer>
                        </Border>
                    </Popup>
                </Grid>
                <ControlTemplate.Triggers>
                    <Trigger Property="IsKeyboardFocusWithin" Value="True">
                        <Setter Property="BorderBrush" Value="#15191F"/>
                    </Trigger>
                    <Trigger Property="IsEditable" Value="True">
                        <Setter TargetName="ContentSite" Property="Visibility" Value="Hidden"/>
                        <Setter TargetName="PART_EditableTextBox" Property="Visibility" Value="Visible"/>
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
            """);
    }

    private static void ApplyTabControlStyle(TabControl tabControl)
    {
        tabControl.BorderThickness = new Thickness(0);
        tabControl.Background = ThemeManager.Brush("Panel");
        tabControl.Padding = new Thickness(0);
        tabControl.FontFamily = UiFont;
        tabControl.Template = (ControlTemplate)ThemeManager.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             TargetType="{x:Type TabControl}">
                <Grid>
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="*"/>
                    </Grid.RowDefinitions>
                    <TabPanel x:Name="HeaderPanel"
                              Grid.Row="0"
                              IsItemsHost="True"
                              KeyboardNavigation.TabIndex="1"
                              Margin="0,0,0,10"/>
                    <Border Grid.Row="1"
                            Background="White"
                            BorderBrush="#F0F0F0"
                            BorderThickness="0,1,0,0"
                            Padding="0,10,0,0">
                        <ContentPresenter x:Name="PART_SelectedContentHost"
                                          ContentSource="SelectedContent"/>
                    </Border>
                </Grid>
            </ControlTemplate>
            """);
        tabControl.ItemContainerStyle = (Style)ThemeManager.Parse("""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                   xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                   TargetType="{x:Type TabItem}">
                <Setter Property="Padding" Value="11,5"/>
                <Setter Property="Margin" Value="0,0,6,6"/>
                <Setter Property="Foreground" Value="#666666"/>
                <Setter Property="Background" Value="#F5F5F5"/>
                <Setter Property="BorderBrush" Value="#E0E0E0"/>
                <Setter Property="FontWeight" Value="Medium"/>
                <Setter Property="Template">
                    <Setter.Value>
                        <ControlTemplate TargetType="{x:Type TabItem}">
                            <Border x:Name="Chrome"
                                    Background="{TemplateBinding Background}"
                                    BorderBrush="{TemplateBinding BorderBrush}"
                                    BorderThickness="1"
                                    CornerRadius="6"
                                    Padding="{TemplateBinding Padding}">
                                <ContentPresenter ContentSource="Header"
                                                  HorizontalAlignment="Center"
                                                  VerticalAlignment="Center"/>
                            </Border>
                            <ControlTemplate.Triggers>
                                <Trigger Property="IsSelected" Value="True">
                                    <Setter Property="Background" Value="#333333"/>
                                    <Setter Property="Foreground" Value="White"/>
                                    <Setter Property="BorderBrush" Value="#333333"/>
                                </Trigger>
                                <Trigger Property="IsMouseOver" Value="True">
                                    <Setter Property="BorderBrush" Value="#CCCCCC"/>
                                </Trigger>
                            </ControlTemplate.Triggers>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Style>
            """);
    }

    private static void AddActions(Window dialog, Panel panel, Func<bool> validateAndApply)
    {
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };

        var cancel = CreateActionButton("取消", false);
        cancel.Click += (_, _) => dialog.DialogResult = false;

        var save = CreateActionButton("保存", true);
        save.Margin = new Thickness(10, 0, 0, 0);
        save.Click += (_, _) =>
        {
            if (validateAndApply())
                dialog.DialogResult = true;
        };

        actions.Children.Add(cancel);
        actions.Children.Add(save);
        panel.Children.Add(new Border
        {
            BorderBrush = ThemeManager.Legacy(240, 240, 240),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 0, 0, 0),
            Margin = new Thickness(0, 4, 0, 0),
            Child = actions
        });
    }

    private static Button CreateActionButton(string text, bool primary)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 86,
            MinHeight = FieldHeight,
            Padding = new Thickness(14, 0, 14, 0),
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            FontFamily = UiFont,
            Background = primary ? ThemeManager.Brush("Primary") : ThemeManager.Brush("Subtle"),
            Foreground = primary ? ThemeManager.Brush("OnPrimary") : InkBrush()
        };
        button.Template = new ControlTemplate(typeof(Button))
        {
            VisualTree = CreateButtonTemplateFactory()
        };
        return button;
    }

    private static FrameworkElementFactory CreateButtonTemplateFactory()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        return border;
    }

    private static List<string> SplitLines(string text)
    {
        return text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> SplitValues(string text)
    {
        return text
            .Split([',', '，', ';', '；', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Brush InkBrush() => ThemeManager.Legacy(21, 25, 31);
    private static Brush MutedBrush() => ThemeManager.Legacy(89, 96, 105);

    private sealed class AltUrlRowState(string url, string compatType, bool isDefault)
    {
        public string Url { get; set; } = url;
        public string CompatType { get; set; } = compatType;
        public bool IsDefault { get; set; } = isDefault;
        public TextBox? UrlBox { get; set; }
        public ComboBox? CompatBox { get; set; }
        public RadioButton? DefaultButton { get; set; }
        public Button? RemoveButton { get; set; }
    }

    private sealed class ManualModelEditState(string name, bool isDefault)
    {
        public string Name { get; } = name;
        public bool IsDefault { get; set; } = isDefault;
    }

    private sealed class ContactListEditor(StackPanel rowsPanel, string placeholder, IReadOnlyList<string> history)
    {
        public TextBox AddRow(string value)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 7) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var input = new TextBox
            {
                Text = value,
                ToolTip = placeholder,
                MinHeight = FieldHeight,
                BorderBrush = ThemeManager.Legacy(211, 217, 222),
                BorderThickness = new Thickness(1),
                Background = ThemeManager.Brush("Panel"),
                Padding = new Thickness(10, 5, 10, 5),
                FontSize = 14,
                FontFamily = UiFont,
                FontWeight = FontWeights.Normal
            };
            ApplyTextBoxStyle(input);
            AttachContactSuggestions(row, input, history);

            var remove = CreateContactRemoveButton();
            remove.Margin = new Thickness(8, 0, 0, 0);
            remove.Click += (_, _) => rowsPanel.Children.Remove(row);

            row.Children.Add(input);
            Grid.SetColumn(remove, 1);
            row.Children.Add(remove);
            rowsPanel.Children.Add(row);

            return input;
        }

        public List<string> GetValues()
        {
            return rowsPanel.Children
                .OfType<Grid>()
                .Select(row => row.Children.OfType<TextBox>().FirstOrDefault()?.Text.Trim() ?? "")
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();
        }
    }

    private sealed record AccountEntryControls(
        StackPanel Panel,
        TextBox Remark,
        TextBox Name,
        TextBox Password,
        ContactListEditor Emails,
        ContactListEditor Phones,
        TextBox SpecialNote);
}
