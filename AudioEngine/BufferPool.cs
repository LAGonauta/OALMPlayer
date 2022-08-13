using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using OpenTK.Audio.OpenAL;

namespace AudioEngine
{
  internal class BufferPool : IDisposable
  {
    private bool disposedValue;

    private readonly Dictionary<int, AudioBuffer> allBuffers = new();
    private readonly HashSet<AudioBuffer> buffers = new();
    private readonly HashSet<AudioBuffer> rented = new();
    private readonly object lockObj = new();
    private readonly bool useXRam;
    private readonly XRamExtension xRamExtension;

    public BufferPool(bool useXRam = false)
    {
      this.useXRam = useXRam;
      xRamExtension = new XRamExtension();
    }

    public AudioBuffer Rent()
    {
      lock (lockObj)
      {
        var buffer = buffers.FirstOrDefault();
        if (buffer == null)
        {
          buffer = new AudioBuffer { Id = AL.GenBuffer() };
          if (useXRam)
          {
            var bufferId = new[] { buffer.Id };
            if (!xRamExtension.SetBufferMode(1, ref bufferId[0], XRamExtension.XRamStorage.Hardware))
            {
              Trace.WriteLine("Unable to set xram mode");
            }
          }
          buffers.Add(buffer);
          allBuffers.Add(buffer.Id, buffer);
        }

        rented.Add(buffer);
        buffers.Remove(buffer);
        return buffer;
      }
    }

    public void Return(int bufferId)
    {
      lock (lockObj)
      {
        if (allBuffers.TryGetValue(bufferId, out var buffer))
        {
          Return(buffer);
        }
        else
        {
          Trace.WriteLine($"ERROR: Unable to find buffer {bufferId}");
        }
      }
    }

    public void Return(AudioBuffer audioBuffer)
    {
      lock (lockObj)
      {
        if (rented.TryGetValue(audioBuffer, out var buffer))
        {
          rented.Remove(buffer);
          buffers.Add(buffer);
        }
        else
        {
          Trace.WriteLine($"ERROR: Unable to find buffer {audioBuffer}");
        }
      }
    }

    protected virtual void Dispose(bool disposing)
    {
      if (!disposedValue)
      {
        AL.DeleteBuffers(allBuffers.Keys.ToArray());
        allBuffers.Clear();
        buffers.Clear();
        rented.Clear();
        disposedValue = true;
      }
    }

    ~BufferPool()
    {
      Dispose(disposing: false);
    }

    public void Dispose()
    {
      // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
      Dispose(disposing: true);
      GC.SuppressFinalize(this);
    }
  }
}
