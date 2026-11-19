using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Assistente.App.Tools;

/// <summary>
/// Gráfico de linha com área preenchida, no estilo do Gerenciador de Tarefas.
/// Desenha até duas séries (ex.: download e upload).
/// </summary>
public sealed class LineGraph : FrameworkElement
{
    private IReadOnlyList<double> _values = Array.Empty<double>();
    private IReadOnlyList<double>? _secondary;

    /// <summary>Quantidade de pontos visíveis (60 = último minuto com 1 amostra/s).</summary>
    public int Capacity { get; set; } = 60;

    /// <summary>Valor máximo do eixo. null = ajusta automaticamente ao maior valor.</summary>
    public double? Maximum { get; set; } = 100;

    /// <summary>Cor da série secundária (a principal usa a cor de destaque).</summary>
    public Color SecondaryColor { get; set; } = Color.FromRgb(0xF5, 0xA5, 0x24);

    public bool ShowGrid { get; set; } = true;

    public string? Label { get; set; }

    public LineGraph()
    {
        Height = 90;
        SnapsToDevicePixels = true;
    }

    public void SetValues(IReadOnlyList<double> values, IReadOnlyList<double>? secondary = null)
    {
        _values = values;
        _secondary = secondary;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        var accent = (Application.Current.Resources["AccentBrush"] as SolidColorBrush)?.Color ?? Colors.DodgerBlue;

        // Fundo e borda
        var background = new SolidColorBrush(Color.FromArgb(0x10, accent.R, accent.G, accent.B));
        var border = new Pen(new SolidColorBrush(Color.FromArgb(0x55, accent.R, accent.G, accent.B)), 1);
        dc.DrawRoundedRectangle(background, border, new Rect(0.5, 0.5, w - 1, h - 1), 6, 6);

        if (ShowGrid)
        {
            var grid = new Pen(new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)), 1);
            for (var i = 1; i < 4; i++)
            {
                var y = Math.Round(h * i / 4) + 0.5;
                dc.DrawLine(grid, new Point(1, y), new Point(w - 1, y));
            }
            for (var i = 1; i < 6; i++)
            {
                var x = Math.Round(w * i / 6) + 0.5;
                dc.DrawLine(grid, new Point(x, 1), new Point(x, h - 1));
            }
        }

        var max = Maximum ?? AutoMax();
        if (_secondary is not null) DrawSeries(dc, _secondary, max, w, h, SecondaryColor);
        DrawSeries(dc, _values, max, w, h, accent);

        if (Label is not null)
        {
            var text = new FormattedText(Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 11, new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point(6, 4));
        }
    }

    private double AutoMax()
    {
        double max = 1;
        foreach (var v in _values) max = Math.Max(max, v);
        if (_secondary is not null)
            foreach (var v in _secondary) max = Math.Max(max, v);
        return max * 1.15;
    }

    private void DrawSeries(DrawingContext dc, IReadOnlyList<double> values, double max, double w, double h, Color color)
    {
        if (values.Count < 2 || max <= 0) return;

        var step = (w - 2) / Math.Max(1, Capacity - 1);
        var startX = w - 1 - step * (values.Count - 1);

        Point P(int i) => new(startX + step * i, h - 1 - Math.Clamp(values[i] / max, 0, 1) * (h - 4));

        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(P(0), false, false);
            for (var i = 1; i < values.Count; i++) ctx.LineTo(P(i), true, true);
        }
        line.Freeze();

        var area = new StreamGeometry();
        using (var ctx = area.Open())
        {
            ctx.BeginFigure(new Point(startX, h - 1), true, true);
            for (var i = 0; i < values.Count; i++) ctx.LineTo(P(i), true, false);
            ctx.LineTo(new Point(startX + step * (values.Count - 1), h - 1), true, false);
        }
        area.Freeze();

        var fill = new LinearGradientBrush(
            Color.FromArgb(0x66, color.R, color.G, color.B),
            Color.FromArgb(0x08, color.R, color.G, color.B), 90);
        dc.DrawGeometry(fill, null, area);
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(color), 1.6) { LineJoin = PenLineJoin.Round }, line);
    }
}
