using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Assistente.App.Tools;

/// <summary>
/// Conteúdo de um grupo da barra lateral: sub-abas no topo (quando o grupo tem
/// mais de uma ferramenta) e a tela da ferramenta escolhida embaixo.
/// </summary>
public sealed class ToolGroupView : DockPanel
{
    private readonly Dictionary<ITool, FrameworkElement> _views = new();
    private readonly Dictionary<ITool, RadioButton> _tabs = new();
    private readonly ContentControl _host = new() { Focusable = false };

    public ToolGroup Group { get; }
    public ITool? Current { get; private set; }

    /// <summary>Disparado quando a sub-aba muda (para lembrar a última ferramenta).</summary>
    public event Action<ITool>? ToolChanged;

    public ToolGroupView(ToolGroup group)
    {
        Group = group;
        LastChildFill = true;

        if (group.Tools.Count > 1)
        {
            var strip = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            foreach (var tool in group.Tools)
            {
                var tab = new RadioButton
                {
                    Style = (Style)Application.Current.Resources["PillTab"],
                    GroupName = "group-" + group.Name,
                    Content = TabContent(tool),
                    Margin = new Thickness(0, 0, 6, 6),
                };
                tab.Checked += (_, _) => Show(tool);
                _tabs[tool] = tab;
                strip.Children.Add(tab);
            }
            SetDock(strip, Dock.Top);
            Children.Add(strip);
        }
        Children.Add(_host);
    }

    private static StackPanel TabContent(ITool tool)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = tool.Glyph,
            FontFamily = (System.Windows.Media.FontFamily)Application.Current.Resources["IconFont"],
            FontSize = 12,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(new TextBlock { Text = tool.Name, VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }

    /// <summary>Mostra a ferramenta (cria a tela na primeira vez).</summary>
    public void Show(ITool tool)
    {
        if (!Group.Tools.Contains(tool)) return;

        if (_tabs.TryGetValue(tool, out var tab) && tab.IsChecked != true)
        {
            tab.IsChecked = true; // dispara Show de novo pelo evento Checked
            return;
        }

        if (!_views.TryGetValue(tool, out var view))
        {
            view = tool.CreateView();
            _views[tool] = view;
        }

        var changed = Current != tool;
        Current = tool;
        _host.Content = view;
        if (changed) Anim.EnterView(view);
        ToolChanged?.Invoke(tool);
    }

    public void Show(string toolName)
    {
        var tool = Group.Tools.FirstOrDefault(t => t.Name == toolName);
        if (tool is not null) Show(tool);
    }

    /// <summary>Mostra a última ferramenta usada no grupo (ou a primeira).</summary>
    public void EnsureShown()
    {
        if (Current is null) Show(Group.Tools[0]);
        else if (_host.Content is FrameworkElement view) Anim.EnterView(view);
    }
}
