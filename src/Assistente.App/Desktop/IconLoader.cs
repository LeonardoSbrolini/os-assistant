using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Assistente.App.Desktop;

/// <summary>
/// Ícones em alta resolução para os atalhos da tela personalizada: ícone "jumbo" (256 px) dos
/// apps pelo próprio Explorer, ícone dos sites procurado na página (apple-touch-icon, manifest,
/// favicon) e imagens escolhidas pela pessoa.
/// </summary>
internal static class IconLoader
{
    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Assistente", "Icones");

    /// <summary>Cache dos ícones de sites (pasta nova: a antiga guardava ícones pequenos/borrados).</summary>
    private static readonly string SiteFolder = Path.Combine(Root, "sites-hd");

    /// <summary>Cópias das imagens que a pessoa escolheu (não quebra se o original for apagado).</summary>
    internal static readonly string CustomFolder = Path.Combine(Root, "meus");

    // ---------- Imagens do computador ----------

    /// <summary>Abre uma imagem (png, jpg, ico, bmp, gif, webp...) pegando o maior tamanho que ela tiver.</summary>
    internal static BitmapSource? LoadFile(string? path, int maxSize = 512)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            return Decode(stream, maxSize);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Copia a imagem escolhida para a pasta do Assistente e devolve o novo caminho.</summary>
    internal static string? ImportImage(string path)
    {
        try
        {
            if (LoadFile(path) is null) return null;
            Directory.CreateDirectory(CustomFolder);
            var copy = Path.Combine(CustomFolder, Guid.NewGuid().ToString("N")[..12] + Path.GetExtension(path).ToLowerInvariant());
            File.Copy(path, copy, true);
            return copy;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Apaga uma cópia que não é mais usada (só dentro da pasta do Assistente).</summary>
    internal static void DeleteImported(string? path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && Path.GetFullPath(path).StartsWith(Path.GetFullPath(CustomFolder), StringComparison.OrdinalIgnoreCase))
                File.Delete(path);
        }
        catch (Exception)
        {
            // Sem problema: fica a sobra.
        }
    }

    private static BitmapSource? Decode(Stream stream, int maxSize)
    {
        var decoder = BitmapDecoder.Create(stream,
            BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
        // .ico tem vários tamanhos: o maior (e com mais cores) é o mais nítido.
        var frame = decoder.Frames
            .OrderByDescending(f => f.PixelWidth * f.PixelHeight)
            .ThenByDescending(f => f.Format.BitsPerPixel)
            .FirstOrDefault();
        if (frame is null || frame.PixelWidth <= 0) return null;
        BitmapSource result = frame;
        var longest = Math.Max(frame.PixelWidth, frame.PixelHeight);
        if (longest > maxSize)
        {
            var f = (double)maxSize / longest;
            result = new TransformedBitmap(frame, new ScaleTransform(f, f));
        }
        result = new FormatConvertedBitmap(result, PixelFormats.Pbgra32, null, 0);
        var copy = new WriteableBitmap(result);
        copy.Freeze();
        return copy;
    }

    // ---------- Apps, arquivos e pastas ----------

    private static readonly Dictionary<string, BitmapSource?> AppCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Ícone grande do programa/atalho/pasta, do jeito que o Explorer mostra em "Ícones extra grandes".
    /// Antes vinha o ícone de 32 px esticado, por isso ficava borrado.
    /// </summary>
    internal static BitmapSource? AppIcon(string path)
    {
        if (AppCache.TryGetValue(path, out var cached)) return cached;
        BitmapSource? image = null;
        try
        {
            image = ShellImage(path, 256);
        }
        catch (Exception)
        {
            image = null;
        }
        return AppCache[path] = image;
    }

    private static BitmapSource? ShellImage(string path, int size)
    {
        var iid = typeof(IShellItemImageFactory).GUID;
        if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var obj) != 0 || obj is not IShellItemImageFactory factory) return null;
        try
        {
            if (factory.GetImage(new NativeSize { Cx = size, Cy = size }, SiigbfIconOnly | SiigbfBiggerSizeOk, out var hbitmap) != 0 || hbitmap == IntPtr.Zero)
                return null;
            try
            {
                return Trim(FromHBitmap(hbitmap));
            }
            finally
            {
                DeleteObject(hbitmap);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(factory);
        }
    }

    /// <summary>Copia um HBITMAP de 32 bits mantendo a transparência.</summary>
    private static BitmapSource? FromHBitmap(IntPtr hbitmap)
    {
        if (GetObject(hbitmap, Marshal.SizeOf<NativeBitmap>(), out var info) == 0) return null;
        int w = info.Width, h = Math.Abs(info.Height);
        if (w <= 0 || h <= 0) return null;
        var header = new BitmapInfoHeader
        {
            Size = Marshal.SizeOf<BitmapInfoHeader>(),
            Width = w,
            Height = -h, // de cima para baixo
            Planes = 1,
            BitCount = 32,
        };
        var pixels = new byte[w * h * 4];
        var dc = GetDC(IntPtr.Zero);
        try
        {
            if (GetDIBits(dc, hbitmap, 0, (uint)h, pixels, ref header, 0) == 0) return null;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, dc);
        }
        // Sem nenhum alfa (imagem sem transparência): deixa opaca.
        var anyAlpha = false;
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0) { anyAlpha = true; break; }
        }
        if (!anyAlpha)
        {
            for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        }
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, pixels, w * 4);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>
    /// Programas antigos não têm ícone de 256 px: o Windows devolve o de 48 px no meio de um
    /// quadro vazio. Corta a sobra transparente para o ícone ocupar o espaço todo.
    /// </summary>
    private static BitmapSource? Trim(BitmapSource? bmp)
    {
        if (bmp is null) return null;
        int w = bmp.PixelWidth, h = bmp.PixelHeight;
        var px = new byte[w * h * 4];
        bmp.CopyPixels(px, w * 4, 0);
        int left = w, top = h, right = -1, bottom = -1;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                if (px[(y * w + x) * 4 + 3] < 8) continue;
                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                if (y > bottom) bottom = y;
            }
        }
        if (right < 0) return null; // imagem vazia
        var cw = right - left + 1;
        var ch = bottom - top + 1;
        // Ícone já ocupa quase tudo: deixa como está (mantém a margem do desenho original).
        if (cw > w * 0.8 || ch > h * 0.8) return bmp;
        var side = Math.Max(cw, ch);
        var x0 = Math.Clamp(left - (side - cw) / 2, 0, Math.Max(0, w - side));
        var y0 = Math.Clamp(top - (side - ch) / 2, 0, Math.Max(0, h - side));
        side = Math.Min(side, Math.Min(w - x0, h - y0));
        var cropped = new CroppedBitmap(bmp, new Int32Rect(x0, y0, side, side));
        var copy = new WriteableBitmap(cropped);
        copy.Freeze();
        return copy;
    }

    // ---------- Sites ----------

    private static readonly HttpClient Http = CreateHttp();
    private static readonly Dictionary<string, Task<BitmapSource?>> Sites = new(StringComparer.OrdinalIgnoreCase);

    private static HttpClient CreateHttp()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 8,
        };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        // Alguns sites só devolvem a página (e os ícones) para navegadores.
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0 Safari/537.36");
        http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "pt-BR,pt;q=0.9,en;q=0.8");
        return http;
    }

    private static string CacheFile(string host) =>
        Path.Combine(SiteFolder, string.Concat(host.Split(Path.GetInvalidFileNameChars())) + ".png");

    /// <summary>Ícone grande do site (procurado uma vez e guardado no computador). Null = não achou.</summary>
    internal static Task<BitmapSource?> SiteIconAsync(string url)
    {
        string host;
        try { host = new Uri(url).Host; }
        catch (Exception) { return Task.FromResult<BitmapSource?>(null); }
        if (host.Length == 0) return Task.FromResult<BitmapSource?>(null);
        lock (Sites)
        {
            if (Sites.TryGetValue(host, out var task)) return task;
            var scheme = url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? "http" : "https";
            return Sites[host] = Task.Run(() => LoadSite(host, scheme));
        }
    }

    /// <summary>Esquece o ícone guardado do site e procura de novo.</summary>
    internal static void ForgetSite(string url)
    {
        try
        {
            var host = new Uri(url).Host;
            lock (Sites) Sites.Remove(host);
            File.Delete(CacheFile(host));
        }
        catch (Exception)
        {
            // Nada guardado.
        }
    }

    private static async Task<BitmapSource?> LoadSite(string host, string scheme)
    {
        var file = CacheFile(host);
        if (File.Exists(file) && LoadFile(file) is { } saved) return saved;

        var best = await FindSiteIcon(host, scheme).ConfigureAwait(false);
        if (best is null) return null;
        try
        {
            Directory.CreateDirectory(SiteFolder);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(best));
            using var stream = File.Create(file);
            encoder.Save(stream);
        }
        catch (Exception)
        {
            // Sem cache: procura de novo na próxima vez.
        }
        return best;
    }

    private sealed record Candidate(string Url, int Size);

    /// <summary>
    /// Procura o melhor ícone: lê a página e o manifest (apple-touch-icon de 180 px, ícones de
    /// 192/512 px...), tenta os endereços padrão e, por último, serviços de favicon.
    /// Para no primeiro com 128 px ou mais; senão fica com o maior encontrado.
    /// </summary>
    private static async Task<BitmapSource?> FindSiteIcon(string host, string scheme)
    {
        var origin = $"{scheme}://{host}/";
        var candidates = new List<Candidate>();
        try
        {
            using var response = await Http.GetAsync(origin, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            var baseUri = response.RequestMessage?.RequestUri ?? new Uri(origin);
            var html = await ReadLimited(response, 768 * 1024).ConfigureAwait(false);
            candidates.AddRange(FromHtml(html, baseUri));
            if (ManifestUrl(html, baseUri) is { } manifest)
                candidates.AddRange(await FromManifest(manifest).ConfigureAwait(false));
            // Página redirecionou para outro endereço (ex.: www.): os padrões valem lá também.
            if (!string.Equals(baseUri.Host, host, StringComparison.OrdinalIgnoreCase))
            {
                var other = $"{baseUri.Scheme}://{baseUri.Host}/";
                candidates.Add(new Candidate(other + "apple-touch-icon.png", 180));
                candidates.Add(new Candidate(other + "favicon.ico", 32));
            }
        }
        catch (Exception)
        {
            // Página fora do ar ou bloqueada: seguem os padrões e os serviços.
        }
        candidates.Add(new Candidate(origin + "apple-touch-icon.png", 180));
        candidates.Add(new Candidate(origin + "apple-touch-icon-precomposed.png", 179));
        candidates.Add(new Candidate(origin + "favicon.ico", 32));
        candidates.Add(new Candidate($"https://www.google.com/s2/favicons?domain={Uri.EscapeDataString(host)}&sz=256", 64));
        candidates.Add(new Candidate($"https://icons.duckduckgo.com/ip3/{Uri.EscapeDataString(host)}.ico", 33));
        candidates.Add(new Candidate($"https://icon.horse/icon/{Uri.EscapeDataString(host)}", 31));

        BitmapSource? best = null;
        var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in candidates.OrderByDescending(c => c.Size))
        {
            if (!tried.Add(c.Url) || tried.Count > 12) continue;
            var img = await Download(c.Url).ConfigureAwait(false);
            if (img is null) continue;
            if (best is null || img.PixelWidth > best.PixelWidth) best = img;
            if (best.PixelWidth >= 128) break;
        }
        // O Google devolve um globo de 16 px quando não acha nada: melhor a letra.
        return best is { PixelWidth: >= 32 } ? best : null;
    }

    private static async Task<string> ReadLimited(HttpResponseMessage response, int limit)
    {
        await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        var buffer = new byte[limit];
        var total = 0;
        int read;
        while (total < limit && (read = await stream.ReadAsync(buffer.AsMemory(total, limit - total)).ConfigureAwait(false)) > 0) total += read;
        return System.Text.Encoding.UTF8.GetString(buffer, 0, total);
    }

    private static readonly Regex LinkTag = new(@"<link\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Attr = new(@"([\w:-]+)\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.Compiled);

    private static Dictionary<string, string> Attrs(string tag)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Attr.Matches(tag))
        {
            var v = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value;
            d[m.Groups[1].Value] = WebUtility.HtmlDecode(v);
        }
        return d;
    }

    private static IEnumerable<Candidate> FromHtml(string html, Uri baseUri)
    {
        // <base href> muda a base dos endereços relativos.
        var baseTag = Regex.Match(html, @"<base\b[^>]*href\s*=\s*[""']([^""']+)", RegexOptions.IgnoreCase);
        if (baseTag.Success && Uri.TryCreate(baseUri, baseTag.Groups[1].Value, out var b)) baseUri = b;

        foreach (Match m in LinkTag.Matches(html))
        {
            var a = Attrs(m.Value);
            if (!a.TryGetValue("rel", out var rel) || !a.TryGetValue("href", out var href) || href.Length == 0) continue;
            rel = rel.ToLowerInvariant();
            if (!rel.Contains("icon") || rel.Contains("mask-icon")) continue;
            if (IsSvg(href, a.GetValueOrDefault("type"))) continue; // o WPF não abre SVG
            if (!Uri.TryCreate(baseUri, href, out var uri) || uri.Scheme is not ("http" or "https")) continue;
            var size = ParseSize(a.GetValueOrDefault("sizes"));
            if (size == 0) size = rel.Contains("apple-touch") ? 180 : 32;
            yield return new Candidate(uri.ToString(), size);
        }
    }

    private static Uri? ManifestUrl(string html, Uri baseUri)
    {
        foreach (Match m in LinkTag.Matches(html))
        {
            var a = Attrs(m.Value);
            if (a.TryGetValue("rel", out var rel) && rel.Equals("manifest", StringComparison.OrdinalIgnoreCase) &&
                a.TryGetValue("href", out var href) && Uri.TryCreate(baseUri, href, out var uri) && uri.Scheme is "http" or "https")
                return uri;
        }
        return null;
    }

    private static async Task<List<Candidate>> FromManifest(Uri manifest)
    {
        var list = new List<Candidate>();
        try
        {
            var json = await Http.GetStringAsync(manifest).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("icons", out var icons) || icons.ValueKind != JsonValueKind.Array) return list;
            foreach (var icon in icons.EnumerateArray())
            {
                if (!icon.TryGetProperty("src", out var src) || src.GetString() is not { Length: > 0 } s) continue;
                var type = icon.TryGetProperty("type", out var t) ? t.GetString() : null;
                var purpose = icon.TryGetProperty("purpose", out var p) ? p.GetString() ?? "" : "";
                if (purpose.Contains("monochrome", StringComparison.OrdinalIgnoreCase) || IsSvg(s, type)) continue;
                if (!Uri.TryCreate(manifest, s, out var uri)) continue;
                var size = ParseSize(icon.TryGetProperty("sizes", out var z) ? z.GetString() : null);
                // "maskable" tem margem grande; prefere o "any" do mesmo tamanho.
                if (purpose.Contains("maskable", StringComparison.OrdinalIgnoreCase) && !purpose.Contains("any", StringComparison.OrdinalIgnoreCase)) size -= 2;
                list.Add(new Candidate(uri.ToString(), size <= 0 ? 96 : Math.Min(size, 512)));
            }
        }
        catch (Exception)
        {
            // Manifest inválido: ignora.
        }
        return list;
    }

    private static bool IsSvg(string href, string? type) =>
        (type?.Contains("svg", StringComparison.OrdinalIgnoreCase) ?? false) ||
        href.Split('?', '#')[0].EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ||
        href.StartsWith("data:image/svg", StringComparison.OrdinalIgnoreCase);

    /// <summary>"180x180" ou "16x16 32x32 64x64" → o maior lado; "any"/vazio → 0.</summary>
    private static int ParseSize(string? sizes)
    {
        if (string.IsNullOrWhiteSpace(sizes)) return 0;
        var best = 0;
        foreach (var part in sizes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var xy = part.ToLowerInvariant().Split('x');
            if (xy.Length == 2 && int.TryParse(xy[0], out var w)) best = Math.Max(best, w);
        }
        return best;
    }

    private static async Task<BitmapSource?> Download(string url)
    {
        try
        {
            byte[] bytes;
            if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var comma = url.IndexOf(',');
                if (comma < 0 || !url[..comma].Contains("base64", StringComparison.OrdinalIgnoreCase)) return null;
                bytes = Convert.FromBase64String(url[(comma + 1)..]);
            }
            else
            {
                using var response = await Http.GetAsync(url).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var type = response.Content.Headers.ContentType?.MediaType ?? "";
                if (type.Contains("html", StringComparison.OrdinalIgnoreCase) || type.Contains("svg", StringComparison.OrdinalIgnoreCase)) return null;
                bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
            if (bytes.Length < 40) return null;
            using var stream = new MemoryStream(bytes);
            return Decode(stream, 512);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ---------- Windows ----------

    private const int SiigbfBiggerSizeOk = 0x1;
    private const int SiigbfIconOnly = 0x4;

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(NativeSize size, int flags, out IntPtr hbitmap);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize { public int Cx, Cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBitmap
    {
        public int Type, Width, Height, WidthBytes;
        public ushort Planes, BitsPixel;
        public IntPtr Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size, Width, Height;
        public short Planes, BitCount;
        public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindCtx, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object? item);

    [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr h, int size, out NativeBitmap bitmap);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BitmapInfoHeader info, uint usage);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
}
