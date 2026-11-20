using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Assistente.App.Services;

namespace Assistente.App.Tools.Chat;

public sealed class ChatTool : ITool
{
    public string Name => "Conversa";
    public string Category => "Chat IA";
    public string Glyph => "";
    public FrameworkElement CreateView() => new ChatView();
}

/// <summary>Chat com IA: balões de conversa, indicador "digitando" e sugestões.</summary>
internal sealed class ChatView : UserControl
{
    private static readonly string[] Suggestions =
    {
        "Me explique juros compostos de um jeito simples",
        "Dê 5 ideias de nome para um projeto",
        "Escreva um e-mail formal pedindo um orçamento",
        "Como deixar o Windows mais rápido?",
    };

    private readonly ChatService _service = new();
    private readonly List<ChatMessage> _history = new();
    private readonly StackPanel _messages = new();
    private readonly ScrollViewer _scroll;
    private readonly TextBox _input = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MaxHeight = 120,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalContentAlignment = VerticalAlignment.Center,
        Padding = new Thickness(10, 8, 10, 8),
    };
    private readonly Button _send;
    private readonly TextBlock _provider = Ui.Caption("");
    private CancellationTokenSource? _cts;
    private FrameworkElement? _typing;

    public ChatView()
    {
        _scroll = Ui.Scroll(_messages);

        _send = new Button
        {
            Style = (Style)Application.Current.Resources["PrimaryButton"],
            Content = "",
            FontFamily = (FontFamily)Application.Current.Resources["IconFont"],
            Width = 42,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Enviar (Enter)",
        };
        _send.Click += (_, _) => Send();
        _input.PreviewKeyDown += (_, e) =>
        {
            // Enter envia; Shift+Enter quebra a linha.
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                e.Handled = true;
                Send();
            }
        };

        var inputRow = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(_send, Dock.Right);
        inputRow.Children.Add(_send);
        inputRow.Children.Add(_input);

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var reset = Ui.Btn("Nova conversa", Reset);
        var config = Ui.Btn("Configurar IA", () => SettingsWindow.Open("Chat IA"));
        var buttons = Ui.Inline(reset, config);
        DockPanel.SetDock(buttons, Dock.Right);
        header.Children.Add(buttons);
        var titles = new StackPanel();
        titles.Children.Add(Ui.Title("Chat IA"));
        titles.Children.Add(_provider);
        header.Children.Add(titles);

        var layout = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(inputRow, Dock.Bottom);
        layout.Children.Add(header);
        layout.Children.Add(inputRow);
        layout.Children.Add(_scroll);
        Content = layout;

        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) return;
            UpdateProvider();
            _input.Focus();
        };
        ShowWelcome();
    }

    private void UpdateProvider()
    {
        var settings = MainWindow.Current!.Settings;
        _provider.Text = settings.ChatProvider == "personalizado"
            ? $"Usando {settings.ChatModel} (sua chave)"
            : "IA gratuita (Pollinations) · sem cadastro";
    }

    private void ShowWelcome()
    {
        _messages.Children.Clear();
        var welcome = Ui.Card("Olá! Como posso ajudar?", "",
            Ui.Caption("Pergunte qualquer coisa. Enter envia, Shift+Enter quebra a linha. As conversas não são salvas."));
        var chips = new WrapPanel();
        foreach (var text in Suggestions)
        {
            var chip = Ui.Btn(text, () =>
            {
                _input.Text = text;
                Send();
            });
            chip.Margin = new Thickness(0, 0, 6, 6);
            chip.FontSize = 12;
            chips.Children.Add(chip);
        }
        ((StackPanel)welcome.Child).Children.Add(chips);
        _messages.Children.Add(welcome);
        Anim.FadeUp(welcome);
    }

    private void Reset()
    {
        _cts?.Cancel();
        _history.Clear();
        ShowWelcome();
        SetBusy(false);
    }

    private async void Send()
    {
        var text = _input.Text.Trim();
        if (text.Length == 0 || _cts is not null) return;

        if (_history.Count == 0) _messages.Children.Clear();
        _input.Clear();
        _history.Add(new ChatMessage("user", text));
        AddBubble(text, fromUser: true);

        SetBusy(true);
        _cts = new CancellationTokenSource();
        try
        {
            var answer = await _service.AskAsync(_history, MainWindow.Current!.Settings, _cts.Token);
            _history.Add(new ChatMessage("assistant", answer));
            AddBubble(answer, fromUser: false);
        }
        catch (OperationCanceledException)
        {
            // Conversa reiniciada.
        }
        catch (Exception ex)
        {
            _history.RemoveAt(_history.Count - 1);
            AddBubble("⚠ " + (ex is System.Net.Http.HttpRequestException { InnerException: not null }
                ? "Sem conexão com o serviço de IA. Verifique sua internet."
                : ex.Message), fromUser: false, error: true);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            SetBusy(false);
            _input.Focus();
        }
    }

    private void SetBusy(bool busy)
    {
        _send.IsEnabled = !busy;
        if (busy)
        {
            _typing = TypingIndicator();
            _messages.Children.Add(_typing);
            ScrollToEnd();
        }
        else if (_typing is not null)
        {
            _messages.Children.Remove(_typing);
            _typing = null;
        }
    }

    private void AddBubble(string text, bool fromUser, bool error = false)
    {
        var content = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(0),
            Foreground = fromUser ? Brushes.White : Ui.Res(error ? "ErrorBrush" : "TextBrush"),
            FontSize = 13.5,
        };

        var bubble = new Border
        {
            CornerRadius = fromUser ? new CornerRadius(14, 14, 4, 14) : new CornerRadius(14, 14, 14, 4),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = fromUser ? new Thickness(60, 0, 4, 10) : new Thickness(0, 0, 60, 10),
            HorizontalAlignment = fromUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Child = content,
        };
        if (fromUser) bubble.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        else
        {
            bubble.Background = Ui.Res("CardBrush");
            bubble.BorderBrush = Ui.Res("CardBorderBrush");
            bubble.BorderThickness = new Thickness(1);
        }

        if (!fromUser && !error)
        {
            // Botão discreto para copiar a resposta.
            var copy = new Button
            {
                Style = (Style)Application.Current.Resources["IconButton"],
                Content = "",
                FontSize = 11,
                Padding = new Thickness(4),
                ToolTip = "Copiar resposta",
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(-4, 4, 0, -4),
            };
            copy.Click += (_, _) =>
            {
                Ui.Copy(text);
                Anim.Pop(copy);
            };
            var stack = new StackPanel();
            bubble.Child = null;
            stack.Children.Add(content);
            stack.Children.Add(copy);
            bubble.Child = stack;
        }

        _messages.Children.Add(bubble);
        Anim.FadeUp(bubble, 0, 10);
        ScrollToEnd();
    }

    private FrameworkElement TypingIndicator()
    {
        var dots = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < 3; i++)
        {
            var dot = new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(3.5), Margin = new Thickness(2, 0, 2, 0) };
            dot.SetResourceReference(Border.BackgroundProperty, "MutedBrush");
            var blink = new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(450))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromMilliseconds(i * 150),
            };
            dot.BeginAnimation(OpacityProperty, blink);
            dots.Children.Add(dot);
        }
        return new Border
        {
            Background = Ui.Res("CardBrush"),
            CornerRadius = new CornerRadius(14, 14, 14, 4),
            Padding = new Thickness(14, 12, 14, 12),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 10),
            Child = dots,
        };
    }

    private void ScrollToEnd() => Dispatcher.BeginInvoke(new Action(() => _scroll.ScrollToEnd()), System.Windows.Threading.DispatcherPriority.Background);
}
