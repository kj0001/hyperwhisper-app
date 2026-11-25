using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using HyperWhisper.Data.Entities;
using VocabularyWords = HyperWhisper.PortableApplication.Persistence.VocabularyWords;
using HyperWhisper.Localization;
using HyperWhisper.Models;
using HyperWhisper.Services;

namespace HyperWhisper.Views.Pages;

public partial class VocabularyPage : Page
{
    private readonly VocabularyService _vocabularyService;
    private ObservableCollection<VocabularyItemRow> _items = new();
    private const int MaxKeywords = 100;

    private Guid? _pendingDeleteId;
    private Guid? _editingId;
    private string? _editingSource;
    private bool _showingReplacementField;
    private readonly bool _compactSettingsLayout;
    private bool _isLoadingJargonUi;

    public VocabularyPage()
        : this(compactSettingsLayout: false)
    {
    }

    public VocabularyPage(bool compactSettingsLayout)
    {
        _compactSettingsLayout = compactSettingsLayout;
        InitializeComponent();
        _vocabularyService = VocabularyService.Instance;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RootStack.MaxWidth = _compactSettingsLayout ? 560 : 900;
        _vocabularyService.VocabularyChanged -= OnVocabularyChanged;
        _vocabularyService.VocabularyChanged += OnVocabularyChanged;
        RefreshJargonCard();
        RefreshList();
        UpdateActionChipsEnabled();
        WordBox.Focus();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _vocabularyService.VocabularyChanged -= OnVocabularyChanged;
    }

    private void OnVocabularyChanged(object? sender, EventArgs e) =>
        Dispatcher.Invoke(RefreshList);

    private void RefreshList()
    {
        var items = _vocabularyService.GetAll();

        // Filter out pending delete item during edit
        if (_pendingDeleteId.HasValue)
            items = items.Where(i => i.Id != _pendingDeleteId.Value).ToList();

        _items = new ObservableCollection<VocabularyItemRow>(items.Select(i => new VocabularyItemRow(i)));
        VocabularyList.ItemsSource = _items;

        var isEmpty = items.Count == 0;
        EmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        HeaderRow.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
        VocabularyList.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;

        // Use total count (including hidden item) for the warning
        var totalCount = _vocabularyService.GetAll().Count;
        if (totalCount > MaxKeywords)
        {
            WarningBanner.Visibility = Visibility.Visible;
            WarningText.Text = $"You have {totalCount} vocabulary items. Only the first {MaxKeywords} will be sent for boosting.";
        }
        else
        {
            WarningBanner.Visibility = Visibility.Collapsed;
        }
    }

    // Focus-scoped: only fires while WordBox has keyboard focus. See
    // ShortcutsSettingsPage.xaml.cs for the reference PreviewKeyDown pattern.
    private void WordBox_PreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

        if (e.Key == Key.Enter && ctrl)
        {
            e.Handled = true;
            OpenReplacementField();
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            AddVocabularyItem();
        }
        else if (e.Key == Key.Escape && _showingReplacementField)
        {
            e.Handled = true;
            CancelReplacementField();
        }
    }

    private void ReplacementBox_PreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

        if (e.Key == Key.Enter && ctrl)
        {
            e.Handled = true;
            AddVocabularyItem();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CancelReplacementField();
        }
    }

    private void WordBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        WordPlaceholder.Visibility = string.IsNullOrEmpty(WordBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ClearError();
        UpdateActionChipsEnabled();
    }

    private void ReplacementBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ReplacementPlaceholder.Visibility = string.IsNullOrEmpty(ReplacementBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ClearError();
        UpdateActionChipsEnabled();
    }

    // A submit error describes the text that was submitted; once the user
    // edits that text or starts editing another row, it no longer applies.
    private void ClearError()
    {
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void UpdateActionChipsEnabled()
    {
        var hasWord = !string.IsNullOrEmpty(WordBox.Text);
        AddWordChip.IsEnabled = hasWord;
        ReplaceWithChip.IsEnabled = hasWord && !_showingReplacementField;
        ReplaceChip.IsEnabled = hasWord && !string.IsNullOrEmpty(ReplacementBox.Text);
    }

    private void AddWordChip_Click(object sender, RoutedEventArgs e) => AddVocabularyItem();

    private void ReplaceWithChip_Click(object sender, RoutedEventArgs e) => OpenReplacementField();

    private void ReplaceChip_Click(object sender, RoutedEventArgs e) => AddVocabularyItem();

    private void OpenReplacementField()
    {
        if (string.IsNullOrEmpty(WordBox.Text)) return;
        _showingReplacementField = true;
        ReplacementBorder.Visibility = Visibility.Visible;
        UpdateActionChipsEnabled();
        Dispatcher.BeginInvoke(new Action(() => ReplacementBox.Focus()), DispatcherPriority.Render);
    }

    private void CancelReplacementField()
    {
        _showingReplacementField = false;
        ReplacementBox.Text = string.Empty;
        ReplacementBorder.Visibility = Visibility.Collapsed;
        UpdateActionChipsEnabled();
        WordBox.Focus();
    }

    private void AddVocabularyItem()
    {
        var word = WordBox.Text;
        if (string.IsNullOrEmpty(word)) return;
        var replacement = string.IsNullOrEmpty(ReplacementBox.Text) ? null : ReplacementBox.Text;

        var source = _editingId.HasValue ? _editingSource : "manual";

        // Issue #1777: a pasted multi-line text becomes one word per non-empty line.
        // Each line gets the usual empty and duplicate checks, and the replacement
        // (which may span lines) goes with every line. Blank text stays one entry,
        // so TryAdd still answers "Word cannot be empty."
        var lines = VocabularyWords.SplitLines(word);
        if (lines.Count == 0)
            lines = [word];
        var refused = new List<(string Line, string? Error)>();
        foreach (var line in lines)
        {
            if (!_vocabularyService.TryAdd(line, replacement, out var lineError, excludeId: _editingId, source: source))
                refused.Add((line, lineError));
        }

        if (refused.Count == lines.Count)
        {
            // Nothing was added: the field keeps its text, as before.
            ErrorText.Text = lines.Count == 1
                ? (refused.FirstOrDefault().Error ?? "Unable to add word.")
                : RefusedLinesMessage(refused);
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        // Delete old item only after new item is successfully saved
        if (_pendingDeleteId.HasValue)
        {
            _vocabularyService.Delete(_pendingDeleteId.Value);
            _pendingDeleteId = null;
            _editingId = null;
            _editingSource = null;
        }

        ClearError();
        WordBox.Text = string.Empty;
        ReplacementBox.Text = string.Empty;
        _showingReplacementField = false;
        ReplacementBorder.Visibility = Visibility.Collapsed;
        ResetEditMode();
        UpdateActionChipsEnabled();
        WordBox.Focus();
        RefreshList();

        // Some lines of a paste can be refused while others are added; name only the
        // refused ones. Set after the field is cleared, whose TextChanged hides the error.
        if (refused.Count > 0)
        {
            ErrorText.Text = RefusedLinesMessage(refused);
            ErrorText.Visibility = Visibility.Visible;
        }
    }

    private static string RefusedLinesMessage(List<(string Line, string? Error)> refused) =>
        $"Not added: {string.Join(", ", refused.Select(r => r.Line))}. {refused[0].Error ?? "Unable to add word."}";

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton button || button.Tag is not Guid id) return;

        var item = _vocabularyService.GetAll().FirstOrDefault(v => v.Id == id);
        if (item == null) return;

        ClearError();
        _pendingDeleteId = id;
        _editingId = id;
        _editingSource = item.Source;

        WordBox.Text = item.Word;
        ReplacementBox.Text = item.Replacement ?? "";

        // Auto-open the replacement field if the item being edited already
        // has a replacement, mirroring macOS.
        if (!string.IsNullOrEmpty(item.Replacement))
        {
            _showingReplacementField = true;
            ReplacementBorder.Visibility = Visibility.Visible;
        }
        else
        {
            _showingReplacementField = false;
            ReplacementBorder.Visibility = Visibility.Collapsed;
        }

        AddWordChipLabel.Text = Loc.S("common.update");
        CancelButton.Visibility = Visibility.Visible;

        RefreshList();
        UpdateActionChipsEnabled();
        InputSection.BringIntoView();
        WordBox.Focus();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _pendingDeleteId = null;
        _editingId = null;
        _editingSource = null;
        WordBox.Text = string.Empty;
        ReplacementBox.Text = string.Empty;
        _showingReplacementField = false;
        ReplacementBorder.Visibility = Visibility.Collapsed;
        ClearError();
        ResetEditMode();
        UpdateActionChipsEnabled();
        WordBox.Focus();
        RefreshList();
    }

    private void ResetEditMode()
    {
        AddWordChipLabel.Text = Loc.S("vocabulary.action.addWord");
        CancelButton.Visibility = Visibility.Collapsed;
        if (!_editingId.HasValue)
        {
            _editingSource = null;
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton button || button.Tag is not Guid id) return;

        _vocabularyService.Delete(id);
        ClearError();
        RefreshList();
    }

    private void RefreshJargonCard()
    {
        _isLoadingJargonUi = true;
        try
        {
            UseJargonFileCheck.IsChecked = SettingsService.Instance.UseJargonFile;
            JargonPathText.Text = AppPaths.JargonFilePath;
            var count = JargonService.Instance.GetCurrentCharacterCount();
            JargonCharCountText.Text = $"{count:N0} / {JargonService.MaxCharacters:N0} chars";
        }
        finally
        {
            _isLoadingJargonUi = false;
        }
    }

    private void UseJargonFileCheck_CheckedChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoadingJargonUi) return;
        SettingsService.Instance.UseJargonFile = UseJargonFileCheck.IsChecked == true;
        RefreshJargonCard();
    }

    private void EditJargonButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            JargonService.Instance.EnsureFileExistsForEdit();
            Process.Start(new ProcessStartInfo(AppPaths.JargonFilePath) { UseShellExecute = true });
            RefreshJargonCard();
        }
        catch (Exception ex)
        {
            LoggingService.Warn($"VocabularyPage: Failed to open jargon.md: {ex.Message}");
            System.Windows.MessageBox.Show(
                "Could not open jargon.md in your default editor.",
                "Jargon Guide",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ResetJargonButton_Click(object sender, RoutedEventArgs e)
    {
        var confirm = System.Windows.MessageBox.Show(
            "Reset jargon.md back to the default starter terms? Any custom edits in jargon.md will be overwritten.",
            "Reset Jargon Guide",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            JargonService.Instance.ResetToDefaults();
            RefreshJargonCard();
        }
        catch (Exception ex)
        {
            LoggingService.Warn($"VocabularyPage: Failed to reset jargon.md: {ex.Message}");
            System.Windows.MessageBox.Show(
                "Could not reset jargon.md.",
                "Jargon Guide",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private sealed class VocabularyItemRow
    {
        public VocabularyItemRow(VocabularyItem item)
        {
            Id = item.Id;
            Word = item.Word;
            Replacement = item.Replacement;
            Source = item.Source;
            IsSourceVisible = !string.IsNullOrWhiteSpace(Source);
            SourceBadgeText = string.Equals(Source, "auto-learn", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Source, "auto-learned", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Source, "autoLearned", StringComparison.OrdinalIgnoreCase)
                    ? Loc.S("vocabulary.autoLearned.badge")
                    : string.Equals(Source, "manual", StringComparison.OrdinalIgnoreCase)
                        ? Loc.S("vocabulary.manual.badge")
                    : Source ?? "";
        }

        public Guid Id { get; }
        public string Word { get; }
        public string? Replacement { get; }
        public string? Source { get; }
        public bool IsSourceVisible { get; }
        public string SourceBadgeText { get; }
    }
}
