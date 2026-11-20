using System;
using System.Collections.Generic;
using System.Linq;

namespace Assistente.App.Tools;

/// <summary>Grupo da barra lateral (ex.: "Sistema") com suas ferramentas (sub-abas).</summary>
public sealed record ToolGroup(string Name, string Glyph, IReadOnlyList<ITool> Tools);

/// <summary>Descobre por reflexão todas as classes que implementam <see cref="ITool"/>.</summary>
public static class ToolRegistry
{
    /// <summary>Ordem e ícone dos grupos na barra lateral. Grupos novos vão para o fim.</summary>
    private static readonly (string Name, string Glyph)[] GroupInfo =
    {
        ("Sistema", ""),
        ("Chat IA", ""),
        ("Música", ""),
        ("Ferramentas", ""),
        ("Calculadoras", ""),
        ("Tempo", ""),
    };

    private static readonly Lazy<IReadOnlyList<ITool>> LazyTools = new(Discover);
    private static readonly Lazy<IReadOnlyList<ToolGroup>> LazyGroups = new(BuildGroups);

    public static IReadOnlyList<ITool> Tools => LazyTools.Value;

    public static IReadOnlyList<ToolGroup> Groups => LazyGroups.Value;

    public static ToolGroup? GroupOf(string toolName) =>
        Groups.FirstOrDefault(g => g.Tools.Any(t => t.Name == toolName));

    private static IReadOnlyList<ITool> Discover() =>
        typeof(ToolRegistry).Assembly.GetTypes()
            .Where(t => typeof(ITool).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false }
                        && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (ITool)Activator.CreateInstance(t)!)
            .OrderBy(t => GroupRank(t.Category))
            .ThenBy(t => t.Category)
            .ThenBy(t => t.Order)
            .ThenBy(t => t.Name)
            .ToList();

    private static IReadOnlyList<ToolGroup> BuildGroups() =>
        Tools.GroupBy(t => t.Category)
            .Select(g => new ToolGroup(g.Key, GlyphOf(g.Key) ?? g.First().Glyph, g.ToList()))
            .ToList();

    private static string? GlyphOf(string group) =>
        GroupInfo.Where(i => i.Name == group).Select(i => i.Glyph).FirstOrDefault();

    private static int GroupRank(string category)
    {
        var i = Array.FindIndex(GroupInfo, g => g.Name == category);
        return i < 0 ? int.MaxValue : i;
    }
}
