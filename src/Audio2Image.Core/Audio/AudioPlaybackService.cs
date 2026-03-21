using NAudio.Wave;
using NAudio.Vorbis;
using Audio2Image.Core.Abstractions;

namespace Audio2Image.Core.Audio;

public class AudioPlaybackService : IAudioPlaybackService
{
    private WaveOutEvent? _waveOut;
    private WaveStream? _waveStream;         // underlying stream (AudioFileReader or VorbisWaveReader)
    private ISampleProvider? _sampleProvider; // sample provider for playback
    private float _volume = 1.0f;
    private string? _currentFile;
    private bool _disposed;
    private CancellationTokenSource? _stopAtCts; // for StopAtAsync cancellation
    private readonly object _lock = new(); // thread safety for waveOut/waveStream access

    public event Action? PlaybackStarted;
    public event Action? PlaybackStopped;

    /// <summary>Current playback state (NAudio-specific).</summary>
    public PlaybackState State { get { lock (_lock) { return _waveOut?.PlaybackState ?? PlaybackState.Stopped; } } }

    /// <summary>True if currently playing audio (interface-friendly).</summary>
    public bool IsPlaying => State == PlaybackState.Playing;

    /// <summary>Current playback position (reads from active reader — bandpass or direct).</summary>
    public TimeSpan Position
    {
        get { lock (_lock) { return (_isBandpassMode ? _bandpassStream?.CurrentTime : _waveStream?.CurrentTime) ?? TimeSpan.Zero; } }
        set
        {
            lock (_lock)
            {
                if (_waveStream != null)
                    _waveStream.CurrentTime = value;
                if (_bandpassStream != null && _isBandpassMode)
                    _bandpassStream.CurrentTime = value;
            }
        }
    }

    /// <summary>Total duration of loaded file.</summary>
    public TimeSpan Duration { get { lock (_lock) { return (_isBandpassMode ? _bandpassStream?.TotalTime : _waveStream?.TotalTime) ?? TimeSpan.Zero; } } }

    /// <summary>Volume 0.0 to 1.0.</summary>
    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            lock (_lock)
            {
                // AudioFileReader supports Volume directly
                if (_waveStream is AudioFileReader afr)
                    afr.Volume = _volume;
            }
        }
    }

    /// <summary>Path of the currently loaded audio file.</summary>
    public string? CurrentFile => _currentFile;

    /// <summary>Sample rate of the loaded audio file.</summary>
    public int SampleRate => _waveStream?.WaveFormat.SampleRate ?? 0;

    /// <summary>
    /// Create the appropriate WaveStream + ISampleProvider for a file path.
    /// AudioFileReader for MP3/WAV (has built-in volume), VorbisWaveReader for OGG.
    /// </summary>
    private static (WaveStream stream, ISampleProvider provider) CreateReader(string filePath)
    {
        if (AudioFormats.IsVorbis(filePath))
        {
            var vorbis = new VorbisWaveReader(filePath);
            // VorbisWaveReader implements ISampleProvider directly
            return (vorbis, vorbis);
        }
        else
        {
            var afr = new AudioFileReader(filePath);
            return (afr, afr);
        }
    }

    /// <summary>
    /// Load an audio file for playback.
    /// WaveOut is initialized lazily on first Play to avoid requiring an audio device at load time.
    /// </summary>
    public void Load(string filePath)
    {
        lock (_lock)
        {
            Stop();
            DisposePlayback();

            var (stream, provider) = CreateReader(filePath);
            _waveStream = stream;
            _sampleProvider = provider;
            _currentFile = filePath;

            // Apply current volume to AudioFileReader
            if (_waveStream is AudioFileReader afr)
                afr.Volume = _volume;
        }
    }

    /// <summary>
    /// Start or resume playback (direct, no filter).
    /// </summary>
    public void Play()
    {
        lock (_lock)
        {
            if (_waveStream == null) return;
            EnsureDirectPlayback();
            _waveOut!.Play();
            PlaybackStarted?.Invoke();
        }
    }

    /// <summary>
    /// Play a specific time range [startTime, endTime] without any filter.
    /// </summary>
    public void PlayRange(TimeSpan startTime, TimeSpan endTime)
    {
        lock (_lock)
        {
            if (_waveStream == null) return;

            // Ensure direct playback (no bandpass filter)
            EnsureDirectPlayback();

            _waveStream.CurrentTime = startTime;
            _waveOut!.Play();
            PlaybackStarted?.Invoke();

            // Cancel previous StopAt task and start new one
            _stopAtCts?.Cancel();
            _stopAtCts?.Dispose();
            _stopAtCts = new CancellationTokenSource();
            _ = StopAtAsync(endTime, _stopAtCts.Token);
        }
    }

    /// <summary>
    /// Play the full track (or from current position) with a bandpass filter,
    /// so only frequencies between lowFreqHz and highFreqHz are audible.
    /// </summary>
    public void PlayWithBandpass(float lowFreqHz, float highFreqHz)
    {
        lock (_lock)
        {
            if (_currentFile == null) return;

            Stop();
            DisposeWaveOut();
            _isBandpassMode = true;

            // Create a separate reader for bandpass playback to avoid conflicts
            // with the primary reader used for position tracking
            _bandpassStream?.Dispose();
            var (stream, provider) = CreateReader(_currentFile);
            _bandpassStream = stream;
            var bandpass = new BandpassSampleProvider(provider, lowFreqHz, highFreqHz);
            _waveOut = new WaveOutEvent();
            _waveOut.Init(bandpass);
            _waveOut.PlaybackStopped += OnPlaybackStopped;
            _waveOut.Play();
            PlaybackStarted?.Invoke();
        }
    }

    private bool _isBandpassMode;
    private WaveStream? _bandpassStream;

    // Loop state
    private bool _isLooping;
    private TimeSpan _loopStart;
    private TimeSpan _loopEnd;
    private CancellationTokenSource? _loopCts;

    /// <summary>True if currently playing in loop mode.</summary>
    public bool IsLooping => _isLooping;

    /// <summary>
    /// Play a time range in a loop (repeating until StopLoop is called).
    /// </summary>
    public void PlayLoop(TimeSpan startTime, TimeSpan endTime)
    {
        lock (_lock)
        {
            if (_waveStream == null) return;

            StopLoop();
            EnsureDirectPlayback();

            _isLooping = true;
            _loopStart = startTime;
            _loopEnd = endTime;
            _loopCts = new CancellationTokenSource();

            _waveStream.CurrentTime = startTime;
            _waveOut!.Play();
            PlaybackStarted?.Invoke();

            _ = LoopAsync(_loopCts.Token);
        }
    }

    /// <summary>
    /// Stop looping and pause playback.
    /// </summary>
    public void StopLoop()
    {
        _isLooping = false;
        _loopCts?.Cancel();
        _loopCts?.Dispose();
        _loopCts = null;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        // Capture local references to avoid race conditions if fields are reassigned
        var waveOut = _waveOut;
        var waveStream = _waveStream;
        var loopStart = _loopStart;
        var loopEnd = _loopEnd;

        if (waveOut == null || waveStream == null) return;

        try
        {
            while (!ct.IsCancellationRequested && waveOut.PlaybackState == PlaybackState.Playing)
            {
                if (waveStream.CurrentTime >= loopEnd)
                {
                    waveStream.CurrentTime = loopStart;
                }
                await Task.Delay(5, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Ensure WaveOut is connected directly to the sample provider (no filter).
    /// Reinitializes if currently in bandpass mode or if WaveOut is null.
    /// </summary>
    private void EnsureDirectPlayback()
    {
        lock (_lock)
        {
            if (_waveStream == null || _sampleProvider == null) return;

            if (_waveOut == null || _isBandpassMode)
            {
                Stop();
                DisposeWaveOut();
                DisposeBandpassStream();
                _isBandpassMode = false;

                _waveOut = new WaveOutEvent();
                _waveOut.Init(_sampleProvider);
                _waveOut.PlaybackStopped += OnPlaybackStopped;
            }
        }
    }

    private void DisposeBandpassStream()
    {
        _bandpassStream?.Dispose();
        _bandpassStream = null;
    }

    private void DisposeWaveOut()
    {
        if (_waveOut != null)
        {
            _waveOut.PlaybackStopped -= OnPlaybackStopped;
            _waveOut.Dispose();
            _waveOut = null;
        }
    }

    private async Task StopAtAsync(TimeSpan endTime, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _waveOut?.PlaybackState == PlaybackState.Playing)
            {
                if (_waveStream != null && _waveStream.CurrentTime >= endTime)
                {
                    Pause();
                    break;
                }
                await Task.Delay(10, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Pause playback.
    /// </summary>
    public void Pause()
    {
        lock (_lock)
        {
            _waveOut?.Pause();
        }
    }

    /// <summary>
    /// Stop playback and reset position to start.
    /// </summary>
    public void Stop()
    {
        lock (_lock)
        {
            StopLoop();
            _stopAtCts?.Cancel();
            _stopAtCts?.Dispose();
            _stopAtCts = null;
            if (_waveOut?.PlaybackState != PlaybackState.Stopped)
            {
                _waveOut?.Stop();
            }
            if (_waveStream != null)
                _waveStream.CurrentTime = TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Seek to a specific position.
    /// </summary>
    public void Seek(TimeSpan position)
    {
        lock (_lock)
        {
            if (_waveStream != null)
            {
                _waveStream.CurrentTime = position;
            }
        }
    }

    /// <summary>
    /// Toggle between play and pause.
    /// </summary>
    public void TogglePlayPause()
    {
        lock (_lock)
        {
            if (_waveStream == null) return;
            EnsureDirectPlayback();

            if (_waveOut!.PlaybackState == PlaybackState.Playing)
                Pause();
            else
                Play();
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        PlaybackStopped?.Invoke();
    }

    private void DisposePlayback()
    {
        lock (_lock)
        {
            DisposeWaveOut();
            DisposeBandpassStream();
            _isBandpassMode = false;
            _waveStream?.Dispose();
            _waveStream = null;
            _sampleProvider = null;
            _currentFile = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        DisposePlayback();
    }
}
