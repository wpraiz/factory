using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace Factory.Services;

/// <summary>
/// Player unico do app: toca MP3 dentro da janela (barra fixa no rodape), sem abrir programa
/// externo. Construido na thread de UI; os eventos do MediaPlayer chegam de outra thread e
/// sao despachados. Nao gasta credito de nada — so reproduz arquivo local.
/// </summary>
public sealed partial class AudioPlayer : ObservableObject, IDisposable
{
    private readonly MediaPlayer _mp = new() { AudioCategory = MediaPlayerAudioCategory.Media };
    private readonly DispatcherQueue _dq = DispatcherQueue.GetForCurrentThread();

    [ObservableProperty] private bool _hasTrack;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _path = "";
    [ObservableProperty] private double _progress;      // 0..100
    [ObservableProperty] private string _timeText = "";
    [ObservableProperty] private string _playGlyph = ""; // Play / Pause

    public AudioPlayer()
    {
        _mp.PlaybackSession.PositionChanged += (s, _) => _dq.TryEnqueue(() => Tick(s));
        _mp.PlaybackSession.PlaybackStateChanged += (s, _) => _dq.TryEnqueue(() =>
        {
            IsPlaying = s.PlaybackState == MediaPlaybackState.Playing;
            PlayGlyph = IsPlaying ? "" : "";
        });
        _mp.MediaEnded += (_, _) => _dq.TryEnqueue(() => { Progress = 100; IsPlaying = false; PlayGlyph = ""; });
        _mp.MediaFailed += (_, e) => _dq.TryEnqueue(() => { Title = "erro ao tocar: " + e.ErrorMessage; IsPlaying = false; });
    }

    public void Play(string file, string? title = null)
    {
        if (!File.Exists(file)) { Title = "arquivo nao existe: " + System.IO.Path.GetFileName(file); HasTrack = true; return; }
        Path = file;
        Title = title ?? System.IO.Path.GetFileNameWithoutExtension(file);
        Progress = 0;
        TimeText = "";
        HasTrack = true;
        _mp.Source = MediaSource.CreateFromUri(new Uri(file));
        _mp.Play();
    }

    public void Toggle()
    {
        if (!HasTrack) return;
        if (_mp.PlaybackSession.PlaybackState == MediaPlaybackState.Playing) _mp.Pause();
        else
        {
            if (_mp.PlaybackSession.PlaybackState == MediaPlaybackState.None && Path.Length > 0)
                _mp.Source = MediaSource.CreateFromUri(new Uri(Path));
            _mp.Play();
        }
    }

    public void Stop()
    {
        _mp.Pause();
        _mp.Source = null;
        HasTrack = false;
        IsPlaying = false;
        Progress = 0;
        TimeText = "";
        Title = "";
        PlayGlyph = "";
    }

    /// <summary>0..1 da duracao.</summary>
    public void Seek(double fraction)
    {
        var d = _mp.PlaybackSession.NaturalDuration;
        if (d.TotalMilliseconds <= 0) return;
        _mp.PlaybackSession.Position = TimeSpan.FromMilliseconds(d.TotalMilliseconds * Math.Clamp(fraction, 0, 1));
    }

    private void Tick(MediaPlaybackSession s)
    {
        var d = s.NaturalDuration;
        if (d.TotalMilliseconds <= 0) return;
        Progress = 100.0 * s.Position.TotalMilliseconds / d.TotalMilliseconds;
        TimeText = $"{s.Position:m\\:ss} / {d:m\\:ss}";
    }

    public void Dispose() => _mp.Dispose();
}
