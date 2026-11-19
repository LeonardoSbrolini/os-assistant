using System;
using System.Windows;
using System.Windows.Controls;
using Assistente.Core.Calculations;
using Assistente.Core.Text;

namespace Assistente.App.Tools.Calculadoras;

public sealed class PercentageTool : ITool
{
    public string Name => "Porcentagem";
    public string Category => "Calculadoras";
    public string Glyph => "";
    public int Order => 3;
    public FrameworkElement CreateView() => new PercentageView();
}

internal sealed class PercentageView : UserControl
{
    public PercentageView()
    {
        Content = Ui.Scroll(Ui.Stack(
            Ui.Title("Porcentagem"),
            Ui.Caption("Os resultados atualizam enquanto você digita."),
            Line("Quanto é", "% de", "10", "250", (x, y) => Ui.Number(Finance.PercentOf(x, y))),
            Line("O valor", "é quantos % de", "50", "200", (x, y) => Ui.Number(Finance.WhatPercent(x, y)) + "%"),
            Line("De", "para (variação)", "100", "135", (x, y) => Ui.Number(Finance.PercentChange(x, y)) + "%"),
            Line("Valor", "± % (aumento/desconto)", "100", "-15", (x, y) => Ui.Number(Finance.ApplyPercent(x, y)))));
    }

    private static UIElement Line(string label1, string label2, string v1, string v2, Func<double, double, string> compute)
    {
        var a = Ui.Input(v1);
        var b = Ui.Input(v2);
        var result = new TextBlock { FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 6) };

        void Update()
        {
            try
            {
                result.Text = NumberParser.TryParse(a.Text, out var x) && NumberParser.TryParse(b.Text, out var y)
                    ? "= " + compute(x, y)
                    : "";
            }
            catch (Exception)
            {
                result.Text = "—";
            }
        }

        a.TextChanged += (_, _) => Update();
        b.TextChanged += (_, _) => Update();
        Update();

        var row = Ui.Row(Ui.Field(label1, a), Ui.Field(label2, b), result);
        row.Margin = new Thickness(0, 0, 0, 14);
        return row;
    }
}
