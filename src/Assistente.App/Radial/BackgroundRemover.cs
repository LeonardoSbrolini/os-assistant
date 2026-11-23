using System;
using System.Collections.Generic;

namespace Assistente.App.Radial;

/// <summary>
/// Remoção de fundo simples, sem internet: estima a cor do fundo pelas bordas da imagem,
/// preenche a partir das bordas tudo que for parecido com ela e deixa transparente.
/// O contorno do objeto ganha uma transparência suave para não ficar serrilhado.
/// </summary>
public static class BackgroundRemover
{
    /// <summary>
    /// Altera <paramref name="bgra"/> (BGRA, 4 bytes por pixel) no lugar.
    /// Retorna true quando o fundo parecia ter muitas cores (resultado pode não ficar bom).
    /// </summary>
    public static bool Apply(byte[] bgra, int width, int height)
    {
        if (width < 3 || height < 3) return false;

        // 1) Cor do fundo: mediana dos pixels da borda (resiste a um objeto encostando na borda).
        var border = new List<int>();
        for (var x = 0; x < width; x++) { border.Add(x); border.Add((height - 1) * width + x); }
        for (var y = 1; y < height - 1; y++) { border.Add(y * width); border.Add(y * width + width - 1); }
        var bs = new List<byte>(border.Count);
        var gs = new List<byte>(border.Count);
        var rs = new List<byte>(border.Count);
        foreach (var i in border)
        {
            bs.Add(bgra[i * 4]);
            gs.Add(bgra[i * 4 + 1]);
            rs.Add(bgra[i * 4 + 2]);
        }
        double bb = Median(bs), bg = Median(gs), br = Median(rs);

        // Quanto as bordas variam em torno dessa cor define a tolerância.
        double spread = 0;
        foreach (var i in border) spread += Dist(bgra, i, bb, bg, br);
        spread /= border.Count;
        var tol = Math.Clamp(28 + spread * 1.2, 28, 85);
        var busy = spread > 45;

        // 2) Preenchimento a partir das bordas.
        var n = width * height;
        var isBg = new bool[n];
        var queue = new int[n];
        int head = 0, tail = 0;
        foreach (var i in border)
        {
            if (!isBg[i] && Dist(bgra, i, bb, bg, br) <= tol)
            {
                isBg[i] = true;
                queue[tail++] = i;
            }
        }
        while (head < tail)
        {
            var i = queue[head++];
            var x = i % width;
            var y = i / width;
            if (x > 0) Visit(i - 1);
            if (x < width - 1) Visit(i + 1);
            if (y > 0) Visit(i - width);
            if (y < height - 1) Visit(i + width);
        }

        void Visit(int j)
        {
            if (isBg[j] || Dist(bgra, j, bb, bg, br) > tol) return;
            isBg[j] = true;
            queue[tail++] = j;
        }

        // 3) Aplica: fundo transparente; contorno (até 2 px do fundo) com transparência suave.
        var near = new byte[n]; // 0 = longe, 1 = vizinho do fundo, 2 = a 2 px
        for (var i = 0; i < n; i++)
        {
            if (!isBg[i]) continue;
            var x = i % width;
            var y = i / width;
            for (var dy = -2; dy <= 2; dy++)
            for (var dx = -2; dx <= 2; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                var j = ny * width + nx;
                if (isBg[j]) continue;
                var ring = (byte)Math.Max(Math.Abs(dx), Math.Abs(dy));
                if (near[j] == 0 || ring < near[j]) near[j] = ring;
            }
        }

        for (var i = 0; i < n; i++)
        {
            if (isBg[i])
            {
                bgra[i * 4] = bgra[i * 4 + 1] = bgra[i * 4 + 2] = bgra[i * 4 + 3] = 0;
                continue;
            }
            if (near[i] == 0) continue;
            // Contorno: compara com a cor "cheia" do objeto ali perto (o pixel mais diferente do
            // fundo num raio de 2 px). Metade do caminho = meio transparente.
            var d = Dist(bgra, i, bb, bg, br);
            var full = d;
            int cx = i % width, cy = i / width;
            for (var dy = -2; dy <= 2; dy++)
            for (var dx = -2; dx <= 2; dx++)
            {
                int nx = cx + dx, ny = cy + dy;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                var j = ny * width + nx;
                if (!isBg[j]) full = Math.Max(full, Dist(bgra, j, bb, bg, br));
            }
            var a = full <= 0 ? 1 : Math.Clamp((d - tol * 0.35) / Math.Max(1, full - tol * 0.35), 0, 1);
            var alpha = bgra[i * 4 + 3] * a;
            // Tira um pouco da cor do fundo que "vazou" no contorno.
            if (a > 0.05 && a < 1)
            {
                bgra[i * 4] = Unmix(bgra[i * 4], bb, a);
                bgra[i * 4 + 1] = Unmix(bgra[i * 4 + 1], bg, a);
                bgra[i * 4 + 2] = Unmix(bgra[i * 4 + 2], br, a);
            }
            bgra[i * 4 + 3] = (byte)Math.Round(alpha);
        }

        // PNG do WPF espera Bgra32 sem pré-multiplicação: nada mais a fazer.
        return busy;
    }

    private static byte Unmix(byte c, double bgc, double a) =>
        (byte)Math.Clamp(Math.Round((c - bgc * (1 - a)) / a), 0, 255);

    private static double Dist(byte[] px, int i, double b, double g, double r)
    {
        var db = px[i * 4] - b;
        var dg = px[i * 4 + 1] - g;
        var dr = px[i * 4 + 2] - r;
        // Transparente já é fundo.
        if (px[i * 4 + 3] < 16) return 0;
        return Math.Sqrt(db * db * 0.8 + dg * dg * 1.2 + dr * dr);
    }

    private static double Median(List<byte> values)
    {
        values.Sort();
        return values[values.Count / 2];
    }
}
