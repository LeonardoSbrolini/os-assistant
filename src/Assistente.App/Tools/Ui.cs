using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Assistente.Core.Text;

namespace Assistente.App.Tools;

/// <summary>
/// Pequenos construtores de UI para as ferramentas ficarem curtas e consistentes.
/// </summary>
public static class Ui
{
    public static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    // ---------- Layout ----------

    /// <summary>Pilha vertical com espaçamento padrão entre os itens.</summary>
    public static StackPanel Stack(params UIElement[] children)
    {
        var panel = new StackPanel();
        foreach (var child in children)
        {
            if (child is FrameworkElement fe && fe.Margin == default) fe.Margin = new Thickness(0, 0, 0, 10);
            panel.Children.Add(child);
        }
        return panel;
    }

    /// <summary>Linha com colunas de mesma largura.</summary>
    public static Grid Row(params UIElement[] children)
    {
        var grid = new Grid();
        for (var i = 0; i < children.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (children[i] is FrameworkElement fe && i < children.Length - 1)
                fe.Margin = new Thickness(fe.Margin.Left, fe.Margin.Top, 8, fe.Margin.Bottom);
            Grid.SetColumn(children[i], i);
            grid.Children.Add(children[i]);
        }
        return grid;
    }

    /// <summary>Linha horizontal com itens no tamanho natural.</summary>
    public static WrapPanel Inline(params UIElement[] children)
    {
        var panel = new WrapPanel();
        foreach (var child in children)
        {
            if (child is FrameworkElement fe && fe.Margin == default) fe.Margin = new Thickness(0, 0, 8, 0);
            panel.Children.Add(child);
        }
        return panel;
    }

    public static ScrollViewer Scroll(UIElement content) => new()
    {
        Content = content,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Focusable = false,
    };

    /// <summary>Tabela de "rótulo: valor".</summary>
    public static Grid KeyValues(params (string Key, string Value)[] items)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < items.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var key = new TextBlock { Text = items[i].Key, Foreground = Res("MutedBrush"), Margin = new Thickness(0, 2, 12, 2) };
            var value = new TextBox
            {
                Text = items[i].Value,
                IsReadOnly = true,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Padding = new Thickness(0),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 2),
            };
            Grid.SetRow(key, i);
            Grid.SetRow(value, i);
            Grid.SetColumn(value, 1);
            grid.Children.Add(key);
            grid.Children.Add(value);
        }
        return grid;
    }

    /// <summary>Cartão com título e ícone, usado para agrupar seções.</summary>
    public static Border Card(string title, string? glyph, params UIElement[] children)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        if (glyph is not null)
        {
            var icon = new TextBlock
            {
                Text = glyph,
                FontFamily = (FontFamily)Application.Current.Resources["IconFont"],
                FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            header.Children.Add(icon);
        }
        header.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });

        var body = new StackPanel();
        body.Children.Add(header);
        foreach (var child in children)
        {
            if (child is FrameworkElement fe && fe.Margin == default) fe.Margin = new Thickness(0, 0, 0, 8);
            body.Children.Add(child);
        }

        return new Border
        {
            Background = Res("CardBrush"),
            BorderBrush = Res("CardBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 12, 14, 8),
            Margin = new Thickness(0, 0, 0, 12),
            Child = body,
        };
    }

    // ---------- Textos ----------

    public static TextBlock Title(string text) => new()
    {
        Text = text,
        FontSize = 18,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 0, 0, 2),
    };

    public static TextBlock Section(string text) => new()
    {
        Text = text,
        FontSize = 14,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 8, 0, 4),
    };

    public static TextBlock Caption(string text) => new()
    {
        Text = text,
        Foreground = Res("MutedBrush"),
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
    };

    public static TextBlock Big(string text = "") => new()
    {
        Text = text,
        FontSize = 22,
        FontWeight = FontWeights.SemiBold,
        TextWrapping = TextWrapping.Wrap,
    };

    public static TextBlock Error() => new()
    {
        Foreground = Res("ErrorBrush"),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 10),
    };

    // ---------- Entradas ----------

    public static TextBox Input(string text = "") => new() { Text = text };

    /// <summary>Rótulo pequeno acima do controle.</summary>
    public static StackPanel Field(string label, UIElement control)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Res("MutedBrush"),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 3),
        });
        panel.Children.Add(control);
        return panel;
    }

    public static TextBox Output(double minHeight = 0) => new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        FontFamily = (FontFamily)Application.Current.Resources["MonoFont"],
        VerticalContentAlignment = VerticalAlignment.Top,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        MinHeight = minHeight,
        MaxHeight = 260,
    };

    public static Button Btn(string text, Action onClick, bool primary = false)
    {
        var button = new Button { Content = text };
        if (primary) button.Style = (Style)Application.Current.Resources["PrimaryButton"];
        button.Click += (_, _) => onClick();
        return button;
    }

    public static CheckBox Check(string text, bool isChecked = false) => new() { Content = text, IsChecked = isChecked };

    public static RadioButton Radio(string text, string group, bool isChecked = false) =>
        new() { Content = text, GroupName = group, IsChecked = isChecked };

    /// <summary>Executa <paramref name="action"/> quando Enter for pressionado em qualquer um dos campos.</summary>
    public static void OnEnter(Action action, params TextBox[] boxes)
    {
        foreach (var box in boxes)
            box.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                action();
                e.Handled = true;
            };
    }

    // ---------- Números ----------

    public static double ReadNumber(TextBox box, string fieldName)
    {
        if (!NumberParser.TryParse(box.Text, out var value))
            throw new FormatException($"Valor inválido em \"{fieldName}\".");
        return value;
    }

    public static string Money(double value) => value.ToString("C2", PtBr);

    public static string Number(double value, int maxDecimals = 4) =>
        value.ToString("#,##0." + new string('#', maxDecimals), PtBr);

    // ---------- Ações ----------

    /// <summary>Executa a ação e mostra a mensagem de erro (se houver) no TextBlock.</summary>
    public static void Safe(TextBlock error, Action action)
    {
        try
        {
            error.Text = "";
            action();
        }
        catch (Exception ex)
        {
            error.Text = ex.Message;
        }
    }

    public static void Copy(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            Clipboard.SetDataObject(text, true);
        }
        catch (Exception)
        {
            // A área de transferência pode estar ocupada por outro programa.
        }
    }

    public static bool Confirm(string message)
    {
        MainWindow.SuppressCollapse++;
        try
        {
            return MessageBox.Show(message, "Assistente", MessageBoxButton.YesNo, MessageBoxImage.Question)
                   == MessageBoxResult.Yes;
        }
        finally
        {
            MainWindow.SuppressCollapse--;
        }
    }

    public static string[] Lines(string text) =>
        text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
}
