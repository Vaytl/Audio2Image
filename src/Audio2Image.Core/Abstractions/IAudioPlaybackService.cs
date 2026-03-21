namespace Audio2Image.Core.Abstractions;

/// <summary>
/// Abstraction for audio playback.
/// Uses bool for State instead of NAudio.Wave.PlaybackState to avoid NAudio dependency in consumers.
/// </summary>
public interface IAudioPlaybackService : IDisposable
{
    event Action? PlaybackStarted;
    event Action? PlaybackStopped;

    /// <summary>True if currently playing audio.</summary>
    bool IsPlaying { get; }

    /// <summary>Current playback position.</summary>
    TimeSpan Position { get; set; }

    /// <summary>Total duration of loaded file.</summary>
    TimeSpan Duration { get; }

    /// <summary>Volume 0.0 to 1.0.</summary>
    float Volume { get; set; }

    /// <summary>Path of the currently loaded audio file.</summary>
    string? CurrentFile { get; }

    /// <summary>Sample rate of the loaded audio file.</summary>
    int SampleRate { get; }

    void Load(string filePath);
    void Play();
    void Pause();
    void Stop();
    void Seek(TimeSpan position);
    void TogglePlayPause();
    void PlayRange(TimeSpan startTime, TimeSpan endTime);
    void PlayWithBandpass(float lowFreqHz, float highFreqHz);

    /// <summary>Play a time range in a loop (repeating).</summary>
    void PlayLoop(TimeSpan startTime, TimeSpan endTime);

    /// <summary>Stop looping (if active).</summary>
    void StopLoop();

    /// <summary>True if currently playing in loop mode.</summary>
    bool IsLooping { get; }
}
