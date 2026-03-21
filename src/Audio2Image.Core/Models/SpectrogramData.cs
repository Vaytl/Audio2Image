namespace Audio2Image.Core.Models;

public record SpectrogramData(
    float[][] Magnitudes,  // [timeFrame][freqBin] in dB
    int FrequencyBins,
    int TimeFrames,
    int SampleRate,
    int FftSize);
