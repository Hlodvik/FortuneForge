using System;
using System.IO;

public static class GeneratePirateSmallWinSound
{
    private const int SampleRate = 22050;

    public static void Create(string outputPath)
    {
        const double duration = 0.42;
        var sampleCount = (int)(SampleRate * duration);
        var dataLength = sampleCount * sizeof(short);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
        using (var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(new[] { 'R', 'I', 'F', 'F' });
            writer.Write(36 + dataLength);
            writer.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(SampleRate);
            writer.Write(SampleRate * sizeof(short));
            writer.Write((short)sizeof(short));
            writer.Write((short)16);
            writer.Write(new[] { 'd', 'a', 't', 'a' });
            writer.Write(dataLength);

            for (var index = 0; index < sampleCount; index++)
            {
                var time = index / (double)SampleRate;
                var firstTone = Bell(time, 1046.5, 0.64);
                var secondTone = time < 0.105 ? 0 : Bell(time - 0.105, 1568.0, 0.5);
                var sample = Math.Max(-1, Math.Min(1, firstTone + secondTone));
                writer.Write((short)(sample * short.MaxValue));
            }
        }
    }

    private static double Bell(double time, double frequency, double gain)
    {
        var attack = Math.Min(1, time / 0.009);
        var decay = Math.Exp(-time * 9.4);
        var fundamental = Math.Sin(2 * Math.PI * frequency * time);
        var overtone = Math.Sin(2 * Math.PI * frequency * 2.01 * time) * 0.18;
        return (fundamental + overtone) * gain * attack * decay;
    }
}
