using System.Windows;
using System.Windows.Controls;

namespace MyKey.Desktop;

public partial class MainWindow
{
    private readonly HashSet<int> _refreshingModels = [];
    private readonly CancellationTokenSource _modelRefreshCancellation = new();

    private async void RefreshModelsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ApiKeyRecord record } button || !_refreshingModels.Add(record.Id)) return;
        button.IsEnabled = false;
        button.Content = "刷新中…";
        try
        {
            List<string> models;
            try
            {
                models = await ModelDetectionService.DetectAsync(record.EffectiveUrl, record.EffectiveKey, _modelRefreshCancellation.Token);
            }
            catch (Exception ex)
            {
                if (!_modelRefreshCancellation.IsCancellationRequested)
                    RecordDialogs.ShowNoticeDialog(this, ModelDetectionService.DescribeError(ex), "模型刷新失败");
                return;
            }
            if (_modelRefreshCancellation.IsCancellationRequested) return;
            if (!_repository.UpdateDetectedModels(record, models))
            {
                ShowToast("记录已删除或配置已更改，请重新刷新。", TimeSpan.FromSeconds(3));
                return;
            }
            ReloadAfterChange($"已刷新模型，共 {models.Count} 个。");
        }
        catch
        {
            ShowToast("模型列表保存失败，请稍后重试。", TimeSpan.FromSeconds(3));
        }
        finally
        {
            _refreshingModels.Remove(record.Id);
            button.IsEnabled = true;
            button.Content = "刷新模型";
        }
    }
}
