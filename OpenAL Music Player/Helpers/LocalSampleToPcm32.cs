using System;
using System.Buffers.Binary;
using CSCore;
using CSCore.Streams.SampleConverter;

namespace OpenALMusicPlayer.Helpers
{
  // From CSCore, but with stackalloc
  // Will be here until CSCore is updated
  internal class LocalSampleToPcm32 : SampleToWaveBase
  {
    public LocalSampleToPcm32(ISampleSource source)
        : base(source, 32, AudioEncoding.Pcm)
    {
      if (source == null)
        throw new ArgumentNullException("source");
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
      Buffer = Buffer.CheckBuffer(count / 4);

      int read = Source.Read(Buffer, 0, count / 4);
      int bufferOffset = offset;

      Span<byte> bytes = stackalloc byte[4];
      for (int i = 0; i < read; i++)
      {
        int value = (int)(Buffer[i] * int.MaxValue);

        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);

        buffer[bufferOffset++] = bytes[0];
        buffer[bufferOffset++] = bytes[1];
        buffer[bufferOffset++] = bytes[2];
        buffer[bufferOffset++] = bytes[3];
      }

      return read * 4;
    }
  }
}
