using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Assistente.App.Services;

namespace Assistente.App.Tools.Utilitarios;

public sealed class ClipboardTool : ITool
{
    public string Name => "Histórico de cópias";
    public string Category => "Utilitários";
    public string Glyph => "";
    public int Order => -1;
    public FrameworkElement CreateView() => new ClipboardView();
}

internal sealed class ClipboardView : UserControl
{
    private readonly ClipboardHistory _history = ClipboardHistory.Instance;
    private readonly TextBox _search = Ui.Input();
    private readonly ListBox _list = new();
    private readonly TextBlock _status = Ui.Caption("");
    private readonly CheckBox _paused = Ui.Check("Pausar captura");

    public ClipboardView()
    {
        var view = CollectionViewSource.GetDefaultView(_history.Entries);
        view.Filter = o => _search.Text.Length == 0 ||
                           ((ClipboardEntry)o).Text.Contains(_search.Text, StringComparison.OrdinalIgnoreCase);
        _search.TextChanged += (_, _) => view.Refresh();
        _search.ToolTip = "Buscar no histórico";

        _list.ItemsSource = view;
        _list.ItemTemplate = BuildItemTemplate();
        _list.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        _list.MouseDoubleClick += (_, _) => CopySelected();
        _list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) CopySelected();
            else if (e.Key == Key.Delete) DeleteSelected();
        };

        _paused.Checked += (_, _) => _history.Paused = true;
        _paused.Unchecked += (_, _) => _history.Paused = false;

        _history.Entries.CollectionChanged += (_, _) => UpdateStatus();
        UpdateStatus();

        var header = Ui.Stack(
            Ui.Title("Histórico de cópias"),
            Ui.Caption("Tudo que você copia (Ctrl+C) aparece aqui. Clique duas vezes para copiar de novo. " +
                       "Fica só na memória e some ao fechar o app."),
            _search,
            Ui.Inline(
                Ui.Btn("Copiar", CopySelected, primary: true),
                Ui.Btn("Excluir", DeleteSelected),
                Ui.Btn("Limpar tudo", ClearAll),
                _paused),
            _status);
        DockPanel.SetDock(header, Dock.Top);

        var layout = new DockPanel();
        layout.Children.Add(header);
        layout.Children.Add(_list);
        Content = layout;
    }

    private static DataTemplate BuildItemTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));

        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(ClipboardEntry.Preview)));
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        text.SetValue(TextBlock.MaxHeightProperty, 40.0);
        text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        panel.AppendChild(text);

        var details = new FrameworkElementFactory(typeof(TextBlock));
        details.SetBinding(TextBlock.TextProperty, new Binding(nameof(ClipboardEntry.Details)));
        details.SetValue(TextBlock.FontSizeProperty, 11.0);
        details.SetValue(TextBlock.ForegroundProperty, (Brush)Application.Current.Resources["MutedBrush"]);
        panel.AppendChild(details);

        return new DataTemplate { VisualTree = panel };
    }

    private void UpdateStatus() =>
        _status.Text = $"{_history.Entries.Count} itens (máximo {ClipboardHistory.MaxEntries})";

    private void CopySelected()
    {
        if (_list.SelectedItem is not ClipboardEntry entry) return;
        Ui.Copy(entry.Text);
        _status.Text = "Copiado! Já pode colar com Ctrl+V.";
    }

    private void DeleteSelected()
    {
        if (_list.SelectedItem is ClipboardEntry entry) _history.Entries.Remove(entry);
    }

    private void ClearAll()
    {
        if (_history.Entries.Count > 0 && Ui.Confirm("Apagar todo o histórico de cópias?"))
            _history.Entries.Clear();
    }
}
