﻿using System;
using System.IO;

using CSCore;
using NVorbis;

namespace AudioEngine
{
  // From https://github.com/filoe/cscore/blob/master/Samples/NVorbisIntegration/Program.cs
  public sealed class NVorbisSource : ISampleSource
  {
    private readonly Stream _stream;
    private readonly VorbisReader _vorbisReader;

    private readonly WaveFormat _waveFormat;
    private bool _disposed;

    public NVorbisSource(Stream stream)
    {
      if (stream == null)
        throw new ArgumentNullException("stream");
      if (!stream.CanRead)
        throw new ArgumentException("Stream is not readable.", "stream");
      _stream = stream;
      _vorbisReader = new VorbisReader(stream, false);
      _waveFormat = new WaveFormat(_vorbisReader.SampleRate, 32, _vorbisReader.Channels, AudioEncoding.IeeeFloat);
    }

    public bool CanSeek => _stream.CanSeek;

    public WaveFormat WaveFormat => _waveFormat;

    //got fixed through workitem #17, thanks for reporting @rgodart.
    public long Length => CanSeek ? (long)(_vorbisReader.TotalTime.TotalSeconds * _waveFormat.SampleRate * _waveFormat.Channels) : 0;

    //got fixed through workitem #17, thanks for reporting @rgodart.
    public long Position
    {
      get
      {
        return CanSeek ? (long)(_vorbisReader.TimePosition.TotalSeconds * _vorbisReader.SampleRate * _vorbisReader.Channels) : 0;
      }
      set
      {
        if (!CanSeek)
          throw new InvalidOperationException("NVorbisSource is not seekable.");
        if (value < 0 || value > Length)
          throw new ArgumentOutOfRangeException("value");

        _vorbisReader.TimePosition = TimeSpan.FromSeconds((double)value / _vorbisReader.SampleRate / _vorbisReader.Channels);
      }
    }

    public int Read(float[] buffer, int offset, int count)
    {
      return _vorbisReader.ReadSamples(buffer, offset, count);
    }

    public void Dispose()
    {
      if (!_disposed)
        _vorbisReader.Dispose();
      else
        throw new ObjectDisposedException("NVorbisSource");
      _disposed = true;
    }
  }
}
