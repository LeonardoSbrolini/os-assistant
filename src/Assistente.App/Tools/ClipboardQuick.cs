using System;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Assistente.App.Services;

namespace Assistente.App.Tools;

/// <summary>
/// Lista compacta do histórico de cópias (Ctrl+C): um clique copia de novo, com uma animação
/// de "copiado". Mostra só alguns itens de cada vez e rola quando há muitos.
/// Usada na ilha (aba Copiados) e na ferramenta Histórico de cópias.
/// </summary>
internal sealed class ClipboardQuick : UserControl
{
    public const double RowHeight = 44;

    private readonly ClipboardHistory _history = ClipboardHistory.Instance;
    private readonly StackPanel _rows = new();
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _empty;
    private readonly bool _island;
    private readonly Func<ClipboardEntry, bool>? _filter;
    private readonly DispatcherTimer _later = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private DateTime _holdUntil = DateTime.MinValue;

    /// <param name="visibleRows">Quantos itens aparecem antes de precisar rolar.</param>
    /// <param name="island">Visual preto da ilha (true) ou cores do tema (false).</param>
    public ClipboardQuick(int visibleRows, bool island, Func<ClipboardEntry, bool>? filter = null)
    {
        _island = island;
        _filter = filter;
        _scroll = new ScrollViewer
        {
            Content = _rows,
            MaxHeight = visibleRows * (RowHeight + 4),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
        };
        _empty = new TextBlock
        {
            Text = "Nada copiado ainda. Copie um texto com Ctrl+C e ele aparece aqui.",
            FontSize = 12,
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(8, 18, 8, 18),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var host = new Grid();
        host.Children.Add(_scroll);
        host.Children.Add(_empty);
        Content = host;

        _later.Tick += (_, _) =>
        {
            // Durante a animação de "copiado" a lista espera um pouco para se reorganizar.
            if (DateTime.Now < _holdUntil) return;
            _later.Stop();
            Render();
        };
        NotifyCollectionChangedEventHandler changed = (_, _) => { _later.Stop(); _later.Start(); };
        Loaded += (_, _) => _history.Entries.CollectionChanged += changed;
        Unloaded += (_, _) => _history.Entries.CollectionChanged -= changed;
        Render();
    }

    /// <summary>Menu do botão direito abriu/fechou (a ilha não deve fechar enquanto ele está aberto).</summary>
    public event Action<bool>? MenuOpenChanged;

    /// <summary>Refaz a lista (ex.: mudou a busca).</summary>
    public void Render()
    {
        _rows.Children.Clear();
        var items = _history.Entries.Where(e => _filter?.Invoke(e) ?? true).ToList();
        foreach (var entry in items) _rows.Children.Add(Row(entry));
        _empty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _scroll.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private Brush RowBrush(byte alpha) => _island
        ? new SolidColorBrush(Color.FromArgb(alpha, 0xFF, 0xFF, 0xFF))
        : new SolidColorBrush(Color.FromArgb(alpha, 0x80, 0x80, 0x80));

    private FrameworkElement Row(ClipboardEntry entry)
    {
        var text = new TextBlock
        {
            Text = entry.Preview,
            FontSize = 12.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var time = new TextBlock
        {
            Text = entry.CopiedAt.ToString("HH:mm"),
            FontSize = 10.5,
            Opacity = 0.5,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var icon = new TextBlock
        {
            Text = "", // copiar
            FontFamily = (FontFamily)Application.Current.Resources["IconFont"],
            FontSize = 12,
            Opacity = 0.55,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var line = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(time, Dock.Right);
        line.Children.Add(icon);
        line.Children.Add(time);
        line.Children.Add(text);

        // Selo "Copiado!" que aparece por cima ao clicar.
        var check = new TextBlock
        {
            Text = "", // visto
            FontFamily = (FontFamily)Application.Current.Resources["IconFont"],
            FontSize = 12,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var copiedLabel = new TextBlock { Text = "Copiado!", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
        var badgeRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        badgeRow.Children.Add(check);
        badgeRow.Children.Add(copiedLabel);
        var badgeScale = new ScaleTransform(0.6, 0.6);
        var badge = new Border
        {
            CornerRadius = new CornerRadius(10),
            Opacity = 0,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = badgeScale,
            Child = badgeRow,
        };
        badge.SetResourceReference(Border.BackgroundProperty, "AccentBrush");

        var grid = new Grid();
        grid.Children.Add(line);
        grid.Children.Add(badge);

        var bg = RowBrush(0x10);
        var scale = new ScaleTransform(1, 1);
        var row = new Border
        {
            Height = RowHeight,
            Margin = new Thickness(0, 0, 0, 4),
            Padding = new Thickness(12, 0, 12, 0),
            CornerRadius = new CornerRadius(12),
            Background = bg,
            Cursor = Cursors.Hand,
            ToolTip = entry.Text.Length > 600 ? entry.Text[..600] + "…" : entry.Text,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = scale,
            Child = grid,
        };
        badge.Margin = new Thickness(-12, 0, -12, 0);
        row.MouseEnter += (_, _) => row.Background = RowBrush(0x24);
        row.MouseLeave += (_, _) => row.Background = bg;
        row.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _holdUntil = DateTime.Now.AddMilliseconds(1100);
            Ui.Copy(entry.Text);
            // Animação: a linha "afunda" e volta, e o selo "Copiado!" cresce por cima e some.
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim.FromTo(0.96, 1, 320, Anim.Back));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.FromTo(0.9, 1, 320, Anim.Back));
            badgeScale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim.FromTo(0.6, 1, 260, Anim.Back));
            badgeScale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.FromTo(0.6, 1, 260, Anim.Back));
            var fade = new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames { Duration = Anim.D(1000) };
            fade.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(1, System.Windows.Media.Animation.KeyTime.FromPercent(0.15)));
            fade.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(1, System.Windows.Media.Animation.KeyTime.FromPercent(0.75)));
            fade.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(0, System.Windows.Media.Animation.KeyTime.FromPercent(1)));
            badge.BeginAnimation(OpacityProperty, fade);
        };

        var menu = new ContextMenu();
        var remove = new MenuItem { Header = "Excluir do histórico" };
        remove.Click += (_, _) => _history.Entries.Remove(entry);
        menu.Items.Add(remove);
        menu.Opened += (_, _) => MenuOpenChanged?.Invoke(true);
        menu.Closed += (_, _) => MenuOpenChanged?.Invoke(false);
        row.ContextMenu = menu;
        return row;
    }
}
