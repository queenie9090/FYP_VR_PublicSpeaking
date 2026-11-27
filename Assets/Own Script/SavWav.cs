using UnityEngine;

public static class SavWav
{
    private const int HeaderSize = 44;

    public static byte[] GetWavBytes(AudioClip clip)
    {
        return GetWavBytes(clip, clip != null ? clip.samples : 0);
    }

    // frameCount is in sample frames, not individual interleaved channel samples.
    public static byte[] GetWavBytes(AudioClip clip, int frameCount)
    {
        return GetWavBytes(clip, 0, frameCount);
    }

    // AudioClip.GetData wraps when the requested range crosses the clip boundary.
    public static byte[] GetWavBytes(AudioClip clip, int startFrame, int frameCount)
    {
        if (clip == null)
        {
            Debug.LogError("[SavWav] AudioClip is null!");
            return null;
        }

        float[] samples = ReadSamples(clip, startFrame, frameCount);
        return EncodePcm16(samples, clip.channels, clip.frequency);
    }

    // Unity's AudioClip API must stay on the main thread.
    internal static float[] ReadSamples(AudioClip clip, int startFrame, int frameCount)
    {
        float[] samples = new float[checked(frameCount * clip.channels)];
        if (!VoiceMicTrace.ReadClip(clip, samples, startFrame))
            throw new System.InvalidOperationException("Unable to read recorded microphone audio.");
        return samples;
    }

    // This method only accesses managed data and can run on a worker thread.
    internal static byte[] EncodePcm16(float[] samples, int channels, int frequency)
    {
        int dataSize = checked(samples.Length * 2);
        byte[] wav = new byte[checked(HeaderSize + dataSize)];
        wav[0] = (byte)'R'; wav[1] = (byte)'I'; wav[2] = (byte)'F'; wav[3] = (byte)'F';
        Write32(wav, 4, wav.Length - 8);
        wav[8] = (byte)'W'; wav[9] = (byte)'A'; wav[10] = (byte)'V'; wav[11] = (byte)'E';
        wav[12] = (byte)'f'; wav[13] = (byte)'m'; wav[14] = (byte)'t'; wav[15] = (byte)' ';
        Write32(wav, 16, 16);
        Write16(wav, 20, 1);
        Write16(wav, 22, channels);
        Write32(wav, 24, frequency);
        Write32(wav, 28, frequency * channels * 2);
        Write16(wav, 32, channels * 2);
        Write16(wav, 34, 16);
        wav[36] = (byte)'d'; wav[37] = (byte)'a'; wav[38] = (byte)'t'; wav[39] = (byte)'a';
        Write32(wav, 40, dataSize);

        for (int i = 0; i < samples.Length; i++)
        {
            short value = (short)(samples[i] * 32767);
            Write16(wav, HeaderSize + i * 2, value);
        }
        return wav;
    }

    private static void Write16(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
    }

    private static void Write32(byte[] buffer, int offset, int value)
    {
        Write16(buffer, offset, value);
        Write16(buffer, offset + 2, value >> 16);
    }
}
