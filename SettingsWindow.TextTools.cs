using System.Windows;
using System.Windows.Controls;
using Egoist.Voice.Core;
using Egoist.Voice.Services;
using Microsoft.Win32;

namespace Egoist.Voice;

public partial class SettingsWindow
{
    private CancellationTokenSource? _textOperation;
    private bool _loadingTextSettings;
    private string? _textEditorSource;

    private void LoadTextSettings(DictationSettings settings)
    {
        _loadingTextSettings = true;
        try
        {
            RuntimeProfileText.Text = VoiceRuntimeProfile.Label;
            AutoQwenCheck.IsChecked = settings.StartLocalQwen;
            AutoQwenCheck.IsEnabled = StartLocalQwenButton.IsEnabled = _mainWindow.CanStartLocalQwen;
            LocalQwenStatusText.Text = _mainWindow.CanStartLocalQwen ? _mainWindow.LocalQwenStatus
                : "Для встроенного запуска нужна отдельно установленная Qwen3-4B GGUF. Можно использовать сервер ниже.";
            FormattingModeCombo.SelectedIndex = settings.FormatWithQwen ? 1 : 0;
            TextEndpointBox.Text = settings.TextModelEndpoint;
            if (!string.IsNullOrWhiteSpace(settings.TextModelId))
            {
                TextModelCombo.ItemsSource = new[] { settings.TextModelId };
                TextModelCombo.SelectedIndex = 0;
            }
            TextModelStatusText.Text = string.IsNullOrWhiteSpace(settings.TextModelId)
                ? "Модель не выбрана. Быстрая диктовка работает независимо."
                : "Модель выбрана. Кнопка «Найти модели» проверит доступность сервера.";
            TranslationEngineCard.Visibility = VoiceRuntimeProfile.IsPortable ? Visibility.Collapsed : Visibility.Visible;
        }
        finally { _loadingTextSettings = false; }
    }

    private void FormattingMode_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _loadingTextSettings || !IsLoaded) return;
        var current = _settingsService.Load();
        _settingsService.Save(current with { FormatWithQwen = FormattingModeCombo.SelectedIndex == 1, FormatBudgetSeconds = 2 });
        _mainWindow.ApplyDictationSettings();
    }

    private void AutoQwen_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || _loadingTextSettings || !IsLoaded) return;
        var current = _settingsService.Load();
        _settingsService.Save(current with { StartLocalQwen = AutoQwenCheck.IsChecked == true });
        _mainWindow.ApplyDictationSettings();
    }

    private async void StartLocalQwen_OnClick(object sender, RoutedEventArgs e)
    {
        StartLocalQwenButton.IsEnabled = false;
        try
        {
            if (await _mainWindow.StartLocalQwenAsync())
            {
                var settings = _settingsService.Load() with { TextModelEndpoint = LocalQwenHost.Endpoint, TextModelId = LocalQwenHost.ModelId };
                _settingsService.Save(settings);
                _mainWindow.ApplyDictationSettings();
                LoadTextSettings(settings);
                TextModelStatusText.Text = "Локальная Qwen подключена. Можно включить оформление диктовки.";
            }
            LocalQwenStatusText.Text = _mainWindow.LocalQwenStatus;
        }
        finally { StartLocalQwenButton.IsEnabled = _mainWindow.CanStartLocalQwen; }
    }

    private void TextEndpoint_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading || _loadingTextSettings) return;
        if (!LocalTextFormatter.TryGetEndpoint(TextEndpointBox.Text.Trim(), out var endpoint))
        {
            TextModelStatusText.Text = "Укажите локальный адрес: http://127.0.0.1:11434/v1 или http://127.0.0.1:1234/v1.";
            return;
        }
        var current = _settingsService.Load();
        var address = endpoint.AbsoluteUri.TrimEnd('/');
        if (current.TextModelEndpoint == address) return;
        _settingsService.Save(current with { TextModelEndpoint = address, TextModelId = "" });
        _loadingTextSettings = true;
        TextModelCombo.ItemsSource = null;
        _loadingTextSettings = false;
        _mainWindow.ApplyDictationSettings();
    }

    private async void FindTextModels_OnClick(object sender, RoutedEventArgs e)
    {
        if (_textOperation is not null) return;
        var address = TextEndpointBox.Text.Trim();
        if (!LocalTextFormatter.TryGetEndpoint(address, out _))
        {
            TextEndpoint_OnLostFocus(sender, e);
            return;
        }
        using var operation = BeginTextOperation();
        FindTextModelsButton.IsEnabled = false;
        TextModelStatusText.Text = "Проверяю локальный сервер…";
        try
        {
            var models = await _mainWindow.GetTextModelsAsync(address, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            _loadingTextSettings = true;
            var previous = _settingsService.Load().TextModelId;
            TextModelCombo.ItemsSource = models;
            TextModelCombo.SelectedItem = models.FirstOrDefault(id => id == previous)
                ?? models.FirstOrDefault(id => id.Contains("qwen", StringComparison.OrdinalIgnoreCase))
                ?? models.FirstOrDefault();
            _loadingTextSettings = false;
            SaveTextModel();
            TextModelStatusText.Text = models.Count == 0
                ? "Текстовый сервер не найден или в нём нет моделей. Запустите Ollama / LM Studio с текстовой Qwen."
                : $"Доступно моделей: {models.Count}. Выберите текстовую Qwen. Диктовка ограничивает ожидание двумя секундами.";
        }
        catch (OperationCanceledException) { TextModelStatusText.Text = "Проверка отменена."; }
        finally { _loadingTextSettings = false; FindTextModelsButton.IsEnabled = true; EndTextOperation(); }
    }

    private void TextModel_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && !_loadingTextSettings && IsLoaded) SaveTextModel();
    }

    private void SaveTextModel()
    {
        if (!LocalTextFormatter.TryGetEndpoint(TextEndpointBox.Text.Trim(), out var endpoint)) return;
        var current = _settingsService.Load();
        _settingsService.Save(current with
        {
            TextModelEndpoint = endpoint.AbsoluteUri.TrimEnd('/'),
            TextModelId = TextModelCombo.SelectedItem as string ?? ""
        });
        _mainWindow.ApplyDictationSettings();
    }

    private CancellationTokenSource BeginTextOperation()
    {
        var operation = new CancellationTokenSource();
        _textOperation = operation;
        OpenAudioButton.IsEnabled = PunctuateTextButton.IsEnabled = CorrectTextButton.IsEnabled = ClearTextButton.IsEnabled = false;
        EditorTextBox.IsReadOnly = true;
        CancelTextButton.IsEnabled = true;
        TextWorkProgress.Visibility = Visibility.Visible;
        return operation;
    }

    private void EndTextOperation()
    {
        _textOperation = null;
        OpenAudioButton.IsEnabled = PunctuateTextButton.IsEnabled = CorrectTextButton.IsEnabled = ClearTextButton.IsEnabled = true;
        EditorTextBox.IsReadOnly = false;
        CancelTextButton.IsEnabled = false;
        TextWorkProgress.Visibility = Visibility.Collapsed;
    }

    private async void OpenAudio_OnClick(object sender, RoutedEventArgs e)
    {
        if (_textOperation is not null) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Распознать аудиофайл локально", CheckFileExists = true,
            Filter = "Аудио|*.wav;*.mp3;*.m4a;*.flac;*.wma|Все файлы|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;
        await TranscribeIntoEditorAsync((progress, token) =>
            _mainWindow.TranscribeFileForEditorAsync(dialog.FileName, progress, token));
    }

    private async void HistoryTranscribeButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_textOperation is not null)
        {
            ShowTextAndActivate();
            return;
        }
        if (sender is not System.Windows.Controls.Button { Tag: string id }) return;
        ShowTextAndActivate();
        await TranscribeIntoEditorAsync((progress, token) =>
            _mainWindow.TranscribeHistoryForEditorAsync(id, progress, token));
        RefreshHistory();
    }

    private async Task TranscribeIntoEditorAsync(
        Func<IProgress<ModelProgress>, CancellationToken, Task<TranscriptionResult>> transcribe)
    {
        using var operation = BeginTextOperation();
        TextWorkStatus.Text = "Распознаю запись…";
        try
        {
            var progress = new Progress<ModelProgress>(p =>
            {
                if (!operation.IsCancellationRequested) TextWorkStatus.Text = p.Label;
            });
            var result = await transcribe(progress, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            EditorTextBox.Text = result.Text;
            TextWorkStatus.Text = $"Распознано за {result.Elapsed.TotalSeconds:0.00} с · {result.Text.Length:N0} символов";
        }
        catch (OperationCanceledException) { TextWorkStatus.Text = "Отменено. Предыдущий текст сохранён."; }
        catch (InvalidOperationException ex) { TextWorkStatus.Text = ex.Message; }
        catch (Exception) { TextWorkStatus.Text = "Не удалось прочитать аудио. Проверьте формат файла и доступность моделей."; }
        finally { EndTextOperation(); }
    }

    private void PunctuateText_OnClick(object sender, RoutedEventArgs e) => _ = EditTextAsync(false);
    private void CorrectText_OnClick(object sender, RoutedEventArgs e) => _ = EditTextAsync(true);

    private async Task EditTextAsync(bool correctWords)
    {
        if (_textOperation is not null) return;
        var source = string.IsNullOrEmpty(EditorTextBox.SelectedText) ? EditorTextBox.Text : EditorTextBox.SelectedText;
        if (string.IsNullOrWhiteSpace(source)) { TextWorkStatus.Text = "Сначала вставьте текст или откройте аудиофайл."; return; }
        var settings = _settingsService.Load();
        if (string.IsNullOrWhiteSpace(settings.TextModelId))
        {
            TextWorkStatus.Text = "Выберите текстовую модель во вкладке «Модели».";
            return;
        }
        using var operation = BeginTextOperation();
        TextResultCard.Visibility = Visibility.Collapsed;
        TextWorkStatus.Text = correctWords ? "Qwen предлагает исправления…" : "Qwen оформляет текст…";
        try
        {
            var result = await _mainWindow.EditTextAsync(source, settings.TextModelEndpoint, settings.TextModelId, correctWords, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            TextWorkStatus.Text = result.Message + $" · {result.Elapsed.TotalSeconds:0.00} с";
            if (result.Status is TextFormattingStatus.Applied or TextFormattingStatus.Unchanged)
            {
                _textEditorSource = EditorTextBox.Text;
                EditorResultBox.Text = result.Text;
                TextResultCard.Visibility = Visibility.Visible;
                EditorResultBox.Focus();
                EditorResultBox.BringIntoView();
            }
        }
        catch (OperationCanceledException) { TextWorkStatus.Text = "Редактирование отменено."; }
        catch (Exception) { TextWorkStatus.Text = "Редактирование не удалось. Исходный текст сохранён."; }
        finally { EndTextOperation(); }
    }

    private void CancelText_OnClick(object sender, RoutedEventArgs e) => _textOperation?.Cancel();
    private void ClearText_OnClick(object sender, RoutedEventArgs e)
    {
        EditorTextBox.Clear(); EditorResultBox.Clear();
        TextResultCard.Visibility = Visibility.Collapsed;
        _textEditorSource = null;
        TextWorkStatus.Text = "Текст очищен из окна.";
    }

    private void EditorText_OnChanged(object sender, TextChangedEventArgs e)
    {
        if (TextResultCard is not null && _textEditorSource != EditorTextBox.Text)
        {
            TextResultCard.Visibility = Visibility.Collapsed;
            EditorResultBox.Clear();
            _textEditorSource = null;
        }
    }

    private async void CopySource_OnClick(object sender, RoutedEventArgs e) => await CopyEditorTextAsync(EditorTextBox.Text);
    private async void CopyResult_OnClick(object sender, RoutedEventArgs e) => await CopyEditorTextAsync(EditorResultBox.Text);
    private async Task CopyEditorTextAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        try { await new ClipboardService().CopyAsync(text, CancellationToken.None); TextWorkStatus.Text = "Скопировано. Вставьте текст через Ctrl+V."; }
        catch (Exception) { TextWorkStatus.Text = "Буфер обмена занят. Повторите копирование."; }
    }

    public void ShowTextAndActivate() { ShowAndActivate(); SettingsTabs.SelectedItem = TextTab; }
    public void ScrollTextResultForPreview() { UpdateLayout(); }
    private void OpenModelsTab_OnClick(object sender, RoutedEventArgs e) => ShowModelsAndActivate();
    public void ShowModelsAndActivate() { ShowAndActivate(); SettingsTabs.SelectedItem = ModelsTab; }
    public void ShowTextPreview(bool filled)
    {
        ShowTextAndActivate();
        if (!filled) return;
        EditorTextBox.Text = "сегодня проверим новую версию сначала запишем короткую фразу затем откроем аудиофайл";
        _textEditorSource = EditorTextBox.Text;
        EditorResultBox.Text = "Сегодня проверим новую версию.\n\nСначала запишем короткую фразу, затем откроем аудиофайл.";
        TextResultCard.Visibility = Visibility.Visible;
        TextWorkStatus.Text = "Пример оформления · текст не сохраняется на диск";
    }
}
