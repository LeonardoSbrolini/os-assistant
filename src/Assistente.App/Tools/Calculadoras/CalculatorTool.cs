using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Assistente.Core.Calculations;

namespace Assistente.App.Tools.Calculadoras;

public sealed class CalculatorTool : ITool
{
    public string Name => "Calculadora";
    public string Category => "Calculadoras";
    public string Glyph => "";
    public int Order => 0;
    public FrameworkElement CreateView() => new CalculatorView();
}

internal sealed class CalculatorView : UserControl
{
    private static readonly string[][] Keys =
    {
        new[] { "C", "(", ")", "⌫" },
        new[] { "7", "8", "9", "÷" },
        new[] { "4", "5", "6", "×" },
        new[] { "1", "2", "3", "−" },
        new[] { "0", ",", "^", "+" },
        new[] { "%", "=" },
    };

    private readonly TextBox _expression = new() { FontSize = 20, Padding = new Thickness(8, 6, 8, 6) };
    private readonly TextBlock _preview = Ui.Caption("");
    private readonly TextBox _history = Ui.Output(80);

    public CalculatorView()
    {
        _expression.TextChanged += (_, _) => UpdatePreview();
        _expression.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Evaluate(); e.Handled = true; }
            else if (e.Key == Key.Escape) { _expression.Clear(); e.Handled = true; }
        };

        var keypad = new StackPanel();
        foreach (var row in Keys)
        {
            var grid = new UniformGrid { Columns = row.Length == 2 ? 2 : 4, Margin = new Thickness(0, 0, 0, 6) };
            foreach (var key in row)
            {
                var button = Ui.Btn(key, () => Press(key), primary: key == "=");
                button.FontSize = 16;
                button.Padding = new Thickness(0, 10, 0, 10);
                button.Margin = new Thickness(0, 0, 6, 0);
                grid.Children.Add(button);
            }
            keypad.Children.Add(grid);
        }

        Content = Ui.Scroll(Ui.Stack(
            Ui.Title("Calculadora"),
            Ui.Caption("Digite a expressão ou use o teclado abaixo. Enter calcula, Esc limpa. Aceita ( ) ^ e %."),
            _expression,
            _preview,
            keypad,
            Ui.Field("Histórico", _history)));
    }

    private void Press(string key)
    {
        switch (key)
        {
            case "C":
                _expression.Clear();
                break;
            case "⌫":
                if (_expression.Text.Length > 0) _expression.Text = _expression.Text[..^1];
                break;
            case "=":
                Evaluate();
                break;
            default:
                _expression.Text += key;
                break;
        }
        _expression.Focus();
        _expression.CaretIndex = _expression.Text.Length;
    }

    private void UpdatePreview()
    {
        _preview.Text = ExpressionEvaluator.TryEvaluate(_expression.Text, out var value)
            ? "= " + Ui.Number(value, 10)
            : "";
    }

    private void Evaluate()
    {
        try
        {
            var value = ExpressionEvaluator.Evaluate(_expression.Text);
            var line = $"{_expression.Text.Trim()} = {Ui.Number(value, 10)}";
            var history = new[] { line }.Concat(Ui.Lines(_history.Text)).Take(20);
            _history.Text = string.Join(Environment.NewLine, history);

            // Sem separador de milhar, para poder continuar a conta.
            _expression.Text = value.ToString("0.##########", Ui.PtBr);
            _expression.CaretIndex = _expression.Text.Length;
        }
        catch (Exception ex)
        {
            _preview.Text = ex.Message;
        }
    }
}
