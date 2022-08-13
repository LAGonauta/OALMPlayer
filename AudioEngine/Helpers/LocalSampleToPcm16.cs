using System;
using System.Buffers.Binary;
using CSCore;
using CSCore.Streams.SampleConverter;

namespace AudioEngine.Helpers
{
  // From CSCore, but with stackalloc
  // Will be here until CSCore is updated
  internal class LocalSampleToPcm16 : SampleToWaveBase
  {
    public LocalSampleToPcm16(ISampleSource source)
        : base(source, 16, AudioEncoding.Pcm)
    {
      if (source == null)
        throw new ArgumentNullException("source");
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
      Buffer = Buffer.CheckBuffer(count / 2);

      int read = Source.Read(Buffer, 0, count / 2);
      int bufferOffset = offset;

      Span<byte> bytes = stackalloc byte[sizeof(Int16)];
      for (int i = 0; i < read; i++)
      {
        short value = (short)(Buffer[i] * short.MaxValue);

        BinaryPrimitives.WriteInt16LittleEndian(bytes, value);

        buffer[bufferOffset++] = bytes[0];
        buffer[bufferOffset++] = bytes[1];
      }

      return read * 2;
    }
  }
}
