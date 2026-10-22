using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

public static class SavWav
{
    private const int HEADER_SIZE = 44;

    /// <summary>
    /// Converts an AudioClip into a raw PCM WAV byte array for network transmission (e.g., OpenAI Whisper STT API).
    /// </summary>
    public static byte[] GetWavBytes(AudioClip clip)
    {
        if (clip == null)
        {
            Debug.LogError("[SavWav] AudioClip is null!");
            return null;
        }

        using (MemoryStream stream = new MemoryStream())
        {
            // Reserve space for the 44-byte WAV header
            byte[] emptyHeader = new byte[HEADER_SIZE];
            stream.Write(emptyHeader, 0, HEADER_SIZE);

            // Convert audio samples from float [-1.0, 1.0] to 16-bit PCM short
            float[] samples = new float[clip.samples * clip.channels];
            clip.GetData(samples, 0);

            Byte[] intData = new Byte[samples.Length * 2];
            int rescaleFactor = 32767; // 16-bit amplitude scaling factor

            for (int i = 0; i < samples.Length; i++)
            {
                short bitValue = (short)(samples[i] * rescaleFactor);
                Byte[] byteArr = BitConverter.GetBytes(bitValue);
                byteArr.CopyTo(intData, i * 2);
            }

            stream.Write(intData, 0, intData.Length);

            // Write final WAV header
            stream.Seek(0, SeekOrigin.Begin);

            Byte[] riff = System.Text.Encoding.UTF8.GetBytes("RIFF");
            stream.Write(riff, 0, 4);

            Byte[] chunkSize = BitConverter.GetBytes(stream.Length - 8);
            stream.Write(chunkSize, 0, 4);

            Byte[] wave = System.Text.Encoding.UTF8.GetBytes("WAVE");
            stream.Write(wave, 0, 4);

            Byte[] fmt = System.Text.Encoding.UTF8.GetBytes("fmt ");
            stream.Write(fmt, 0, 4);

            Byte[] subChunk1 = BitConverter.GetBytes(16); // Subchunk1Size (16 for PCM)
            stream.Write(subChunk1, 0, 4);

            UInt16 two = 2;
            UInt16 one = 1;

            Byte[] audioFormat = BitConverter.GetBytes(one); // 1 = Uncompressed PCM
            stream.Write(audioFormat, 0, 2);

            Byte[] numChannels = BitConverter.GetBytes((ushort)clip.channels);
            stream.Write(numChannels, 0, 2);

            Byte[] sampleRate = BitConverter.GetBytes(clip.frequency);
            stream.Write(sampleRate, 0, 4);

            Byte[] byteRate = BitConverter.GetBytes(clip.frequency * clip.channels * 2);
            stream.Write(byteRate, 0, 4);

            UInt16 blockAlign = (ushort)(clip.channels * 2);
            stream.Write(BitConverter.GetBytes(blockAlign), 0, 2);

            UInt16 bps = 16;
            Byte[] bitsPerSample = BitConverter.GetBytes(bps);
            stream.Write(bitsPerSample, 0, 2);

            Byte[] datastring = System.Text.Encoding.UTF8.GetBytes("data");
            stream.Write(datastring, 0, 4);

            Byte[] subChunk2 = BitConverter.GetBytes(clip.samples * clip.channels * 2);
            stream.Write(subChunk2, 0, 4);

            return stream.ToArray();
        }
    }
}