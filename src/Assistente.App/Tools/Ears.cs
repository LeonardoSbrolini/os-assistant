using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Assistente.App.Tools;

/// <summary>
/// "Orelhas" curvas (cantos côncavos) que fazem um painel preto parecer sair da borda da
/// tela, como o notch/Dynamic Island do Mac.
/// </summary>
public static class Ears
{
    public enum Kind
    {
        /// <summary>Borda de baixo, à esquerda do painel.</summary>
        BottomLeft,
        /// <summary>Borda de baixo, à direita do painel.</summary>
        BottomRight,
        /// <summary>Borda esquerda, acima do painel.</summary>
        LeftTop,
        /// <summary>Borda esquerda, abaixo do painel.</summary>
        LeftBottom,
        /// <summary>Borda direita, acima do painel.</summary>
        RightTop,
    }

    public static Path Make(Kind kind, double r, Brush fill)
    {
        var data = kind switch
        {
            Kind.BottomLeft => $"M0,{r} L{r},{r} L{r},0 A{r},{r} 0 0 1 0,{r} Z",
            Kind.BottomRight => $"M{r},{r} L0,{r} L0,0 A{r},{r} 0 0 0 {r},{r} Z",
            Kind.LeftTop => $"M0,0 L0,{r} L{r},{r} A{r},{r} 0 0 1 0,0 Z",
            Kind.LeftBottom => $"M0,{r} L0,0 L{r},0 A{r},{r} 0 0 0 0,{r} Z",
            _ => $"M{r},0 L{r},{r} L0,{r} A{r},{r} 0 0 0 {r},0 Z",
        };
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return new Path { Data = geometry, Fill = fill, Width = r, Height = r, IsHitTestVisible = false, SnapsToDevicePixels = true };
    }

    public static readonly Brush Black = Frozen(new SolidColorBrush(Color.FromRgb(0, 0, 0)));

    private static Brush Frozen(Brush b)
    {
        b.Freeze();
        return b;
    }
}
