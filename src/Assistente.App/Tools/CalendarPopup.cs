using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Assistente.App.Tools;

/// <summary>
/// Widget de calendário do mês (estilo Mac) que abre ao clicar em qualquer coisa que mostre
/// data ou hora: barra de menus, ilha, tela personalizada, mini widgets, menu do canto...
/// Só um fica aberto por vez; clicar de novo no mesmo lugar fecha.
/// </summary>
public static class CalendarPopup
{
    private static Popup? _popup;
    private static FrameworkElement? _anchor;
    private static FrameworkElement? _lastAnchor;
    private static DateTime _closedAt = DateTime.MinValue;

    /// <summary>O calendário está aberto? (a ilha/dock/cantos não fecham enquanto isso)</summary>
    public static bool IsOpen => _popup is { IsOpen: true };

    /// <summary>Liga o clique de um elemento que mostra data/hora ao calendário.</summary>
    public static void Attach(FrameworkElement element, string? tip = "Abrir calendário")
    {
        element.Cursor = Cursors.Hand;
        if (tip is not null) element.ToolTip = tip;
        element.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Toggle(element);
        };
    }

    /// <summary>Abre embaixo (ou em cima, se não couber) do elemento; se já estiver aberto ali, fecha.</summary>
    public static void Toggle(FrameworkElement anchor)
    {
        // Clique no mesmo lugar logo depois de fechar (o clique fora fecha antes): só fecha.
        if (!IsOpen && ReferenceEquals(_lastAnchor, anchor) && DateTime.Now - _closedAt < TimeSpan.FromMilliseconds(300)) return;
        if (IsOpen)
        {
            var same = ReferenceEquals(_anchor, anchor);
            Close();
            if (same) return;
        }
        Show(anchor);
    }

    public static void Close()
    {
        if (_popup is not null) _popup.IsOpen = false;
    }

    private static void Show(FrameworkElement anchor)
    {
        var today = DateTime.Today;
        var month = new DateTime(today.Year, today.Month, 1);
        var selected = today;
        var body = new StackPanel { Width = 276 };
        var card = new Border
        {
            Background = new LinearGradientBrush(Color.FromRgb(0x1A, 0x1A, 0x20), Color.FromRgb(0x0C, 0x0C, 0x0F), 90),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(16, 14, 16, 14),
            Margin = new Thickness(14, 6, 14, 18),
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.55, Direction = 270 },
            Child = body,
        };
        System.Windows.Documents.TextElement.SetForeground(card, Brushes.White);
        System.Windows.Documents.TextElement.SetFontFamily(card, new FontFamily("Segoe UI"));
        var scale = new ScaleTransform(0.92, 0.92);
        card.RenderTransformOrigin = new Point(0.5, 0);
        card.RenderTransform = scale;

        void Render()
        {
            body.Children.Clear();

            // Dia escolhido em destaque (começa em hoje).
            var big = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 0, 12) };
            var num = Label(selected.Day.ToString(), 34, FontWeights.Light);
            num.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            num.Margin = new Thickness(0, -6, 10, -4);
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(Label(Cap(selected.ToString("dddd", Ui.PtBr)), 14, FontWeights.SemiBold));
            var rel = (selected - today).Days;
            var relText = rel switch
            {
                0 => "Hoje",
                1 => "Amanhã",
                -1 => "Ontem",
                > 0 => $"Daqui a {rel} dias",
                _ => $"Há {-rel} dias",
            };
            var sub = Label(selected.ToString("d 'de' MMMM 'de' yyyy", Ui.PtBr) + " · " + relText, 11.5, FontWeights.Normal);
            sub.Opacity = 0.6;
            words.Children.Add(sub);
            big.Children.Add(num);
            big.Children.Add(words);
            body.Children.Add(big);

            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var arrows = new StackPanel { Orientation = Orientation.Horizontal };
            arrows.Children.Add(new Widgets.PillButton("", () => { month = month.AddMonths(-1); Render(); }, false, 26) { ToolTip = "Mês anterior" });
            arrows.Children.Add(new Widgets.PillButton("", () => { month = new DateTime(today.Year, today.Month, 1); selected = today; Render(); }, false, 26) { ToolTip = "Hoje" });
            arrows.Children.Add(new Widgets.PillButton("", () => { month = month.AddMonths(1); Render(); }, false, 26) { ToolTip = "Próximo mês" });
            DockPanel.SetDock(arrows, System.Windows.Controls.Dock.Right);
            header.Children.Add(arrows);
            header.Children.Add(Label(Cap(month.ToString("MMMM", Ui.PtBr)) + " " + month.Year, 15, FontWeights.SemiBold));
            body.Children.Add(header);

            var grid = new UniformGrid { Columns = 7 };
            foreach (var d in new[] { "D", "S", "T", "Q", "Q", "S", "S" })
            {
                var t = Label(d, 10.5, FontWeights.SemiBold);
                t.Opacity = 0.45;
                t.HorizontalAlignment = HorizontalAlignment.Center;
                t.Margin = new Thickness(0, 0, 0, 4);
                grid.Children.Add(t);
            }
            var start = month.AddDays(-(int)month.DayOfWeek);
            var weeks = ((int)month.DayOfWeek + DateTime.DaysInMonth(month.Year, month.Month) + 6) / 7;
            var hover = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
            for (var i = 0; i < weeks * 7; i++)
            {
                var day = start.AddDays(i);
                var isToday = day == today;
                var isSelected = day == selected && !isToday;
                var t = Label(day.Day.ToString(), 12.5, isToday ? FontWeights.Bold : FontWeights.Normal);
                t.HorizontalAlignment = HorizontalAlignment.Center;
                t.Opacity = day.Month == month.Month ? (day.DayOfWeek is DayOfWeek.Sunday or DayOfWeek.Saturday ? 0.6 : 1) : 0.25;
                var cell = new Border
                {
                    Width = 32,
                    Height = 32,
                    CornerRadius = new CornerRadius(16),
                    Child = t,
                    Margin = new Thickness(1),
                    Background = Brushes.Transparent,
                    Cursor = Cursors.Hand,
                };
                if (isToday)
                {
                    cell.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                    t.Opacity = 1;
                }
                else if (isSelected)
                {
                    cell.BorderThickness = new Thickness(1.5);
                    cell.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
                }
                else
                {
                    cell.MouseEnter += (_, _) => cell.Background = hover;
                    cell.MouseLeave += (_, _) => cell.Background = Brushes.Transparent;
                }
                var d = day;
                cell.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    selected = d;
                    if (d.Month != month.Month || d.Year != month.Year) month = new DateTime(d.Year, d.Month, 1);
                    Render();
                };
                grid.Children.Add(cell);
            }
            body.Children.Add(grid);

            var week = System.Globalization.ISOWeek.GetWeekOfYear(selected);
            var footer = Label($"Semana {week} · dia {selected.DayOfYear} do ano", 11.5, FontWeights.Normal);
            footer.Opacity = 0.55;
            var open = Label("Notificações", 12, FontWeights.Medium);
            open.Cursor = Cursors.Hand;
            open.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            open.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                Close();
                Docking.Native.WinCombo(0x4E);
            };
            var foot = new DockPanel { Margin = new Thickness(2, 10, 2, 0) };
            DockPanel.SetDock(open, System.Windows.Controls.Dock.Right);
            foot.Children.Add(open);
            foot.Children.Add(footer);
            body.Children.Add(foot);
        }

        Render();
        _anchor = anchor;
        _popup = new Popup
        {
            PlacementTarget = anchor,
            Placement = PlacementMode.Custom,
            // Centralizado embaixo do que foi clicado; se não couber (ex.: canto de baixo), em cima.
            CustomPopupPlacementCallback = (popup, target, _) => new[]
            {
                new CustomPopupPlacement(new Point((target.Width - popup.Width) / 2, target.Height), PopupPrimaryAxis.Horizontal),
                new CustomPopupPlacement(new Point((target.Width - popup.Width) / 2, -popup.Height), PopupPrimaryAxis.Horizontal),
            },
            AllowsTransparency = true,
            StaysOpen = false,
            PopupAnimation = PopupAnimation.None,
            Child = card,
        };
        var mine = _popup;
        _popup.Closed += (_, _) =>
        {
            _lastAnchor = anchor;
            _closedAt = DateTime.Now;
            if (ReferenceEquals(_popup, mine))
            {
                _popup = null;
                _anchor = null;
            }
        };
        _popup.IsOpen = true;
        card.BeginAnimation(UIElement.OpacityProperty, Anim.FromTo(0, 1, 160));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim.To(1, 220, Anim.Back));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.To(1, 220, Anim.Back));
    }

    private static TextBlock Label(string text, double size, FontWeight weight) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = weight,
        Foreground = Brushes.White,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpper(s[0], Ui.PtBr) + s[1..];
}
