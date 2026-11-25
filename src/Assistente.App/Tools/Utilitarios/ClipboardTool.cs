using System;
using System.Windows;
using System.Windows.Controls;
using Assistente.App.Services;

namespace Assistente.App.Tools.Utilitarios;

public sealed class ClipboardTool : ITool
{
    public string Name => "Histórico de cópias";
    public string Category => "Ferramentas";
    public string Glyph => "\uE77F";
    public int Order => -1;
    public FrameworkElement CreateView() => new ClipboardView();
}

/// <summary>
/// Histórico do Ctrl+C: um clique copia de novo (com animação de "copiado"). Mostra só alguns
/// itens por vez e rola quando há muitos, para não ficar enorme.
/// </summary>
internal sealed class ClipboardView : UserControl
{
    private readonly ClipboardHistory _history = ClipboardHistory.Instance;
    private readonly TextBox _search = Ui.Input();
    private readonly TextBlock _status = Ui.Caption("");
    private readonly CheckBox _paused = Ui.Check("Pausar captura");
    private readonly ClipboardQuick _list;

    public ClipboardView()
    {
        _list = new ClipboardQuick(6, island: false,
            e => _search.Text.Length == 0 || e.Text.Contains(_search.Text, StringComparison.OrdinalIgnoreCase));
        _list.Margin = new Thickness(0, 10, 0, 0);
        _search.TextChanged += (_, _) => _list.Render();
        _search.ToolTip = "Buscar no histórico";

        _paused.IsChecked = _history.Paused;
        _paused.Checked += (_, _) => _history.Paused = true;
        _paused.Unchecked += (_, _) => _history.Paused = false;

        _history.Entries.CollectionChanged += OnChanged;
        Unloaded += (_, _) => _history.Entries.CollectionChanged -= OnChanged;
        Loaded += (_, _) =>
        {
            _history.Entries.CollectionChanged -= OnChanged;
            _history.Entries.CollectionChanged += OnChanged;
        };
        UpdateStatus();

        Content = Ui.Stack(
            Ui.Title("Histórico de cópias"),
            Ui.Caption("Tudo que você copia (Ctrl+C) aparece aqui. Clique em um item para copiar de novo; botão direito para excluir. " +
                       "Fica só na memória e some ao fechar o app."),
            _search,
            Ui.Inline(Ui.Btn("Limpar tudo", ClearAll), _paused),
            _status,
            _list);
    }

    private void OnChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => UpdateStatus();

    private void UpdateStatus() =>
        _status.Text = $"{_history.Entries.Count} itens (máximo {ClipboardHistory.MaxEntries})";

    private void ClearAll()
    {
        if (_history.Entries.Count > 0 && Ui.Confirm("Apagar todo o histórico de cópias?"))
            _history.Entries.Clear();
    }
}
