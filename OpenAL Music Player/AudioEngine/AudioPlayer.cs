using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CSCore;
using CSCore.Codecs;
using CSCore.Streams.SampleConverter;
using OpenALMusicPlayer.Helpers;
using OpenTK.Audio.OpenAL;
using OpenTK.Audio.OpenAL.Extensions.Creative.EnumerateAll;
using OpenTK.Audio.OpenAL.Extensions.EXT.Float32;

namespace OpenALMusicPlayer.AudioEngine
{
  internal class AudioPlayer : IDisposable
  {
    private ALDevice device;
    private ALContext context;
    private int source;
    private Dictionary<int, int> buffers = new();
    private XRamExtension xram;
    private bool hasXram = false;
    private bool disposedValue;

    private readonly SemaphoreSlim playSemaphore = new SemaphoreSlim(1, 1);
    private readonly object lockObj = new();
    private SimpleCancellationToken simpleCancellationToken = new();

    public double CurrentTime { get; private set; }
    public double TotalTime { get; private set; }
    public bool IsValid => !disposedValue;

    public AudioPlayer(string device = null)
    {
      if (device == null)
      {
        device = ALC.GetString(ALDevice.Null, AlcGetString.DefaultDeviceSpecifier);
      }

      this.device = ALC.OpenDevice(device);
      context = ALC.CreateContext(this.device, (int[])null);
      ALC.MakeContextCurrent(context);
      source = AL.GenSource();
      xram = new XRamExtension();
      hasXram = xram.GetRamSize > 0;
    }

    public static async Task<IEnumerable<string>> AvailableDevices()
    {
      return await Task.Run(() =>
      {
        if (EnumerateAll.IsExtensionPresent())
        {
          return EnumerateAll.GetStringList(GetEnumerateAllContextStringList.AllDevicesSpecifier);
        }
        return ALC.GetStringList(GetEnumerationStringList.DeviceSpecifier);
      });
    }

    public ALSourceState State
    {
      get
      {
        return AL.GetSourceState(source);
      }
    }

    public void Stop()
    {
      lock (lockObj)
      {
        simpleCancellationToken.Cancel();
      }
    }

    private void StopInternal()
    {
      AL.SourceStop(source);
      var _ = UnqueueBuffers(source);
      CurrentTime = 0.0f;
    }

    public void Pause()
    {
      AL.SourcePause(source);
    }

    public void Unpause()
    {
      AL.SourcePlay(source);
    }

    public bool Looping
    {
      get
      {
        AL.GetSource(source, ALSourceb.Looping, out bool looping);
        return looping;
      }
      set
      {
        AL.Source(source, ALSourceb.Looping, value);
      }
    }

    public async Task Play(string filePath, Action<double, double> timeUpdateCallback)
    {
      await Task.Run(async () =>
      {
        var localToken = NewCancellationToken();
        using var playReleaseToken = await playSemaphore.LockAsync();
        using var audioFile = GetAudioFile(filePath);
        TotalTime = audioFile.GetLength().TotalMilliseconds / 1000;

        const int streamingBufferTime = 1000; // in milliseconds
        var streamingBufferSize = (int)audioFile.GetRawElements(streamingBufferTime);

        var streamingBufferQueueSize = 100; // get from gui
        if (IsXFi)
        {
          if (streamingBufferQueueSize > GetFreeXRam / streamingBufferSize)
          {
            streamingBufferQueueSize = GetFreeXRam / streamingBufferSize;
          }
        }

        if (streamingBufferQueueSize < 3)
        {
          streamingBufferQueueSize = 3;
        }

        var interval = streamingBufferTime / streamingBufferQueueSize;
        CurrentTime = 0;
        while (true)
        {
          if (localToken.IsCancellationRequested)
          {
            StopInternal();
            if (!IsXFi)
            {
              AL.SourceRewind(source);
            }
            return;
          }

          var state = AL.GetSourceState(source);
          if (state == ALSourceState.Stopped)
          {
            AL.SourceRewind(source);
            return;
          }

          // queue new buffers
          if (state == ALSourceState.Initial)
          {
            QueueBuffer(audioFile, streamingBufferSize, streamingBufferQueueSize);
            QueueBuffer(audioFile, streamingBufferSize, streamingBufferQueueSize);
            QueueBuffer(audioFile, streamingBufferSize, streamingBufferQueueSize);
            QueueBuffer(audioFile, streamingBufferSize, streamingBufferQueueSize);
            QueueBuffer(audioFile, streamingBufferSize, streamingBufferQueueSize);
          }
          QueueBuffer(audioFile, streamingBufferSize, streamingBufferQueueSize);

          if (state != ALSourceState.Playing && state != ALSourceState.Paused)
          {
            AL.SourcePlay(source);
          }

          // clear played buffers
          var processedBuffers = UnqueueBuffers(source);
          CurrentTime += audioFile.GetMilliseconds(processedBuffers.Select(b => b.bufferSize).Sum()) / 1000;
          timeUpdateCallback(CurrentTime, TotalTime);

          await Task.Delay(interval);
        }
      });
    }

    private SimpleCancellationToken NewCancellationToken()
    {
      lock (lockObj)
      {
        simpleCancellationToken.Cancel();
        var newToken = new SimpleCancellationToken();
        simpleCancellationToken = newToken;
        return newToken;
      }
    }

    private void QueueBuffer(IWaveSource audioFile, int streamingBufferSize, int streamingBufferQueueSize)
    {
      if (audioFile.Position < audioFile.Length)
      {
        AL.GetSource(source, ALGetSourcei.BuffersQueued, out int buffersQueued);
        CheckALError("checking buffers queued buffer");

        if (buffersQueued < streamingBufferQueueSize && audioFile.Position < audioFile.Length)
        {
          var size = audioFile.Length - audioFile.Position < streamingBufferSize
            ? (int)(audioFile.Length - audioFile.Position)
            : streamingBufferSize;

          var sound_data = new byte[size];
          audioFile.Read(sound_data, 0, sound_data.Length);

          var soundBuffer = AL.GenBuffer();
          var format = GetSoundFormat(audioFile.WaveFormat.Channels, audioFile.WaveFormat.BitsPerSample);
          AL.BufferData(soundBuffer, format, ref sound_data[0], sound_data.Length, audioFile.WaveFormat.SampleRate);
          CheckALError("buffering data");
          buffers.Add(soundBuffer, size);

          AL.SourceQueueBuffer(source, soundBuffer);
          CheckALError("queueing buffer");
        }
      }
    }

    public float Gain
    {
      get
      {
        AL.GetSource(source, ALSourcef.Gain, out var gain);
        return gain;
      }
      set
      {
        AL.Source(source, ALSourcef.Gain, value);
      }
    }

    public float Pitch
    {
      get
      {
        AL.GetSource(source, ALSourcef.Pitch, out var pitch);
        return pitch;
      }
      set
      {
        AL.Source(source, ALSourcef.Pitch, value);
      }
    }

    private IEnumerable<(int bufferId, int bufferSize)> UnqueueBuffers(int source)
    {
      AL.GetSource(source, ALGetSourcei.BuffersProcessed, out int buffersProcessed);
      CheckALError("checking buffers processed");

      if (buffersProcessed > 0)
      {
        var dequeuedBuffers = AL.SourceUnqueueBuffers(source, buffersProcessed)
          .Select(buffer => (buffer, buffers[buffer]))
          .ToList();
        foreach (var (dequeuedBuffer, _) in dequeuedBuffers)
        {
          AL.DeleteBuffer(dequeuedBuffer);
          CheckALError("deleting buffer");
          buffers.Remove(dequeuedBuffer);
        }

        return dequeuedBuffers;
      }

      return new List<(int, int)>();
    }

    private IWaveSource GetAudioFile(string filePath)
    {
      IWaveSource audioFile = null;
      try
      {
        audioFile = CodecFactory.Instance.GetCodec(filePath);
        if (IsXFi && audioFile.WaveFormat.WaveFormatTag == AudioEncoding.IeeeFloat)
        {
          var sampleSource = audioFile.ToSampleSource();
          if (audioFile.WaveFormat.BitsPerSample == 32)
          {
            audioFile = new SampleToPcm32(sampleSource);
          }
          else if (audioFile.WaveFormat.BitsPerSample == 16)
          {
            audioFile = new SampleToPcm16(sampleSource);
          }
          else
          {
            audioFile = new SampleToPcm8(sampleSource);
          }
        }

        return audioFile;
      }
      catch
      {
        if (audioFile != null)
        {
          audioFile.Dispose();
        }
        throw;
      }
    }

    public bool IsXFi
    {
      get
      {
        var renderer = AL.Get(ALGetString.Renderer);
        return renderer != null && renderer.Contains("X-Fi");
      }
    }

    public int GetFreeXRam => hasXram ? xram.GetRamFree : 0;

    public int GetSizeXRam => hasXram ? xram.GetRamSize : 0;

    private ALError CheckALError(string message)
    {
      var error = AL.GetError();

      if (error != ALError.NoError)
      {
        Trace.WriteLine($"Error {message}: {error}");
      }

      return error;
    }

    private ALFormat GetSoundFormat(int channels, int bits)
    {
      switch (channels)
      {
        case 1:
          {
            if (bits == 8)
            {
              return ALFormat.Mono8;
            }
            else if (bits == 16)
            {
              return ALFormat.Mono16;
            }
            else
            {
              if (IsXFi)
              {
                throw new NotSupportedException("The specified sound format is not supported.");
                //return 0x1203;
              }
              else
              {
                if (EXTFloat32.IsExtensionPresent())
                {
                  return ALFormat.MonoFloat32Ext;
                }
                else
                {
                  throw new NotSupportedException("The specified sound format is not supported.");
                }
              }
            }
          }
        case 2:
          {
            if (bits == 8)
            {
              return ALFormat.Stereo8;
            }
            else if (bits == 16)
            {
              return ALFormat.Stereo16;
            }
            else
            {
              if (IsXFi)
              {
                // Undocumented value
                return (ALFormat)0x1203;
              }
              else
              {
                if (EXTFloat32.IsExtensionPresent())
                {
                  return ALFormat.StereoFloat32Ext;
                }
                else
                {
                  throw new NotSupportedException("The specified sound format is not supported.");
                }
              }
            }
          }
        case 6:
          {
            if (bits == 8)
            {
              return ALFormat.Multi51Chn8Ext;
            }
            else if (bits == 16)
            {
              return ALFormat.Multi51Chn16Ext;
            }
            else
            {
              return ALFormat.Multi51Chn32Ext;
            }
          }
        default:
          throw new NotSupportedException("The specified sound format is not supported.");
      }
    }

    protected virtual void Dispose(bool disposing)
    {
      if (!disposedValue)
      {
        Stop();
        playSemaphore.Wait();

        if (disposing)
        {
          playSemaphore.Dispose();
        }

        foreach (var buffer in buffers)
        {
          AL.DeleteBuffer(buffer.Value);
          CheckALError("deleting buffer on dispose");
        }
        buffers.Clear();
        AL.DeleteSource(source);
        ALC.MakeContextCurrent(ALContext.Null);
        ALC.DestroyContext(context);
        ALC.CloseDevice(device);

        disposedValue = true;
      }
    }

    ~AudioPlayer()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
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
