namespace Audio2Image.Core.Models;

public record AudioData(float[] Samples, int SampleRate, TimeSpan Duration);
