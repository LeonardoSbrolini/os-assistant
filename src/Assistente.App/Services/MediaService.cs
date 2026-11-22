using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Control;

namespace Assistente.App.Services;

/// <summary>
/// O que está tocando no Windows (YouTube Music no navegador ou no app, Spotify...),
/// lido pelo controle de mídia do sistema. Usado pela aba Música e pelo mini widget.
/// </summary>
public sealed class MediaService
{
    public static MediaService Instance { get; } = new();

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private GlobalSystemMediaTransportControlsSessionTimelineProperties? _timeline;
    private bool _starting;

    private MediaService() { }

    /// <summary>False se o controle de mídia do Windows não estiver disponível.</summary>
    public bool Available { get; private set; } = true;
    public bool HasSession => _session is not null;
    public string Title { get; private set; } = "";
    public string Artist { get; private set; } = "";
    public string Album { get; private set; } = "";
    public string AppName { get; private set; } = "";
    public ImageSource? Cover { get; private set; }
    public bool IsPlaying { get; private set; }

    /// <summary>Última vez em que algo estava tocando (para esconder o widget depois de muito tempo pausado).</summary>
    public DateTime LastPlayingAt { get; private set; } = DateTime.MinValue;

    public IReadOnlyList<GlobalSystemMediaTransportControlsSession> Sessions { get; private set; } =
        Array.Empty<GlobalSystemMediaTransportControlsSession>();

    public GlobalSystemMediaTransportControlsSession? Session => _session;

    /// <summary>Qualquer mudança (música, play/pausa, sessões). Sempre no thread da interface.</summary>
    public event Action? Changed;

    public async void Start()
    {
        if (_manager is not null || _starting) return;
        _starting = true;
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.SessionsChanged += (_, _) => OnUi(RefreshAsync);
            _manager.CurrentSessionChanged += (_, _) => OnUi(RefreshAsync);
            await RefreshAsync();
        }
        catch (Exception)
        {
            Available = false;
            Changed?.Invoke();
        }
        finally
        {
            _starting = false;
        }
    }

    public void Select(GlobalSystemMediaTransportControlsSession session)
    {
        Attach(session);
        _ = RefreshAsync();
    }

    public void TogglePlayPause() => Control(s => s.TryTogglePlayPauseAsync(), VkMediaPlayPause);
    public void Next() => Control(s => s.TrySkipNextAsync(), VkMediaNext);
    public void Previous() => Control(s => s.TrySkipPreviousAsync(), VkMediaPrev);

    public static void VolumeMute() => PressKey(VkVolumeMute);
    public static void VolumeDown() => PressKey(VkVolumeDown, 2);
    public static void VolumeUp() => PressKey(VkVolumeUp, 2);

    /// <summary>Posição atual e duração da música (zero se o player não informa).</summary>
    public (TimeSpan Position, TimeSpan Duration) GetTimeline()
    {
        if (_timeline is null || _timeline.EndTime <= TimeSpan.Zero) return (TimeSpan.Zero, TimeSpan.Zero);
        var position = _timeline.Position;
        if (IsPlaying) position += DateTimeOffset.Now - _timeline.LastUpdatedTime;
        if (position > _timeline.EndTime) position = _timeline.EndTime;
        if (position < TimeSpan.Zero) position = TimeSpan.Zero;
        return (position, _timeline.EndTime);
    }

    // ---------- Sessões ----------

    private static void OnUi(Func<Task> action) =>
        Application.Current?.Dispatcher.BeginInvoke(new Func<Task>(action));

    private void Attach(GlobalSystemMediaTransportControlsSession? session)
    {
        if (ReferenceEquals(_session, session)) return;
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnMediaChanged;
            _session.PlaybackInfoChanged -= OnPlaybackChanged;
            _session.TimelinePropertiesChanged -= OnTimelineChanged;
        }
        _session = session;
        if (_session is not null)
        {
            _session.MediaPropertiesChanged += OnMediaChanged;
            _session.PlaybackInfoChanged += OnPlaybackChanged;
            _session.TimelinePropertiesChanged += OnTimelineChanged;
        }
    }

    private void OnMediaChanged(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) =>
        OnUi(async () => { await RefreshMediaAsync(); Changed?.Invoke(); });

    private void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) =>
        OnUi(() => { RefreshPlayback(); Changed?.Invoke(); return Task.CompletedTask; });

    private void OnTimelineChanged(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e) =>
        OnUi(() => { ReadTimeline(); return Task.CompletedTask; });

    private async Task RefreshAsync()
    {
        if (_manager is null) return;
        List<GlobalSystemMediaTransportControlsSession> sessions;
        try
        {
            sessions = _manager.GetSessions().ToList();
        }
        catch (Exception)
        {
            sessions = new();
        }
        Sessions = sessions;

        // Mantém o player escolhido; senão prefere um que esteja tocando, depois o "atual" do Windows.
        var keep = _session is null ? null : sessions.FirstOrDefault(s => s.SourceAppUserModelId == _session.SourceAppUserModelId);
        var playing = sessions.FirstOrDefault(IsSessionPlaying);
        var preferred = keep is not null && (IsSessionPlaying(keep) || playing is null)
            ? keep
            : playing ?? SafeCurrent() ?? sessions.FirstOrDefault();
        Attach(preferred);

        if (_session is null)
        {
            Clear();
        }
        else
        {
            await RefreshMediaAsync();
            RefreshPlayback();
            ReadTimeline();
        }
        Changed?.Invoke();
    }

    private GlobalSystemMediaTransportControlsSession? SafeCurrent()
    {
        try { return _manager?.GetCurrentSession(); }
        catch (Exception) { return null; }
    }

    private static bool IsSessionPlaying(GlobalSystemMediaTransportControlsSession s)
    {
        try { return s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; }
        catch (Exception) { return false; }
    }

    private void Clear()
    {
        Title = Artist = Album = AppName = "";
        Cover = null;
        IsPlaying = false;
        _timeline = null;
    }

    private async Task RefreshMediaAsync()
    {
        var session = _session;
        if (session is null) return;
        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            if (!ReferenceEquals(session, _session)) return;
            Title = string.IsNullOrWhiteSpace(props.Title) ? "Sem título" : props.Title;
            Artist = props.Artist ?? "";
            Album = props.AlbumTitle ?? "";
            AppName = FriendlyAppName(session.SourceAppUserModelId);
            Cover = props.Thumbnail is null ? null : await LoadThumbnailAsync(props.Thumbnail);
        }
        catch (Exception)
        {
            // A sessão pode ter sido fechada no meio da leitura.
        }
    }

    private void RefreshPlayback()
    {
        IsPlaying = _session is not null && IsSessionPlaying(_session);
        if (IsPlaying) LastPlayingAt = DateTime.Now;
    }

    private void ReadTimeline()
    {
        try { _timeline = _session?.GetTimelineProperties(); }
        catch (Exception) { _timeline = null; }
    }

    private async void Control(Func<GlobalSystemMediaTransportControlsSession, Windows.Foundation.IAsyncOperation<bool>> action, byte fallbackKey)
    {
        try
        {
            if (_session is not null && await action(_session)) return;
        }
        catch (Exception)
        {
            // Usa a tecla de mídia abaixo.
        }
        PressKey(fallbackKey);
    }

    private static async Task<ImageSource?> LoadThumbnailAsync(Windows.Storage.Streams.IRandomAccessStreamReference reference)
    {
        try
        {
            using var stream = await reference.OpenReadAsync();
            using var input = stream.AsStreamForRead();
            var memory = new MemoryStream();
            await input.CopyToAsync(memory);
            memory.Position = 0;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = memory;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string FriendlyAppName(string id)
    {
        var lower = id.ToLowerInvariant();
        if (lower.Contains("chrome")) return "Google Chrome";
        if (lower.Contains("msedge") || lower.Contains("edge")) return "Microsoft Edge";
        if (lower.Contains("firefox")) return "Firefox";
        if (lower.Contains("opera")) return "Opera";
        if (lower.Contains("brave")) return "Brave";
        if (lower.Contains("spotify")) return "Spotify";
        if (lower.Contains("youtube")) return "YouTube Music";
        if (lower.Contains("zune") || lower.Contains("media")) return "Player do Windows";
        var name = Path.GetFileNameWithoutExtension(id.Split('!')[0]);
        return string.IsNullOrWhiteSpace(name) ? id : name;
    }

    // Teclas de mídia/volume do Windows (funcionam mesmo sem sessão de mídia).
    private const byte VkVolumeMute = 0xAD;
    private const byte VkVolumeDown = 0xAE;
    private const byte VkVolumeUp = 0xAF;
    private const byte VkMediaNext = 0xB0;
    private const byte VkMediaPrev = 0xB1;
    private const byte VkMediaPlayPause = 0xB3;
    private const uint KeyEventKeyUp = 0x0002;

    private static void PressKey(byte key, int times = 1)
    {
        for (var i = 0; i < times; i++)
        {
            keybd_event(key, 0, 0, UIntPtr.Zero);
            keybd_event(key, 0, KeyEventKeyUp, UIntPtr.Zero);
        }
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
}
