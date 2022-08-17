using System.Collections.Concurrent;
using System.Diagnostics;
using AudioEngine.Helpers;
using CSCore;
using CSCore.Codecs;
using CSCore.Streams.SampleConverter;
using OpenTK.Audio.OpenAL;
using OpenTK.Audio.OpenAL.Extensions.Creative.EnumerateAll;
using OpenTK.Audio.OpenAL.Extensions.EXT.Float32;

namespace AudioEngine
{
    public class AudioPlayer : IDisposable
  {
    private ALDevice device;
    private ALContext context;
    private int source;
    private XRamExtension xram;
    private bool hasXram = false;
    private bool supportsOffset = false;
    private bool disposedValue = false;

    private readonly SemaphoreSlim playSemaphore = new SemaphoreSlim(1, 1);
    private ConcurrentQueue<CancellationTokenSource> concurrentBag = new();

    public bool IsValid => !disposedValue;

    public AudioPlayer(string? device = null)
    {
      if (device == null)
      {
        device = ALC.GetString(ALDevice.Null, AlcGetString.DefaultDeviceSpecifier);
      }

      this.device = ALC.OpenDevice(device);
      context = ALC.CreateContext(this.device, Array.Empty<int>());
      ALC.MakeContextCurrent(context);
      source = AL.GenSource();
      xram = new XRamExtension();
      hasXram = xram.GetRamSize > 0;
      supportsOffset = AL.IsExtensionPresent("AL_EXT_OFFSET");
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
      }).ConfigureAwait(false);
    }

    public ALSourceState State => AL.GetSourceState(source);

    public void Stop()
    {
      while (concurrentBag.TryDequeue(out var item))
      {
        if (!item.IsCancellationRequested)
        {
          item.Cancel();
        }
      }
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
      set => AL.Source(source, ALSourceb.Looping, value);
    }

    public async Task<PlaybackResult> Play(string filePath, Func<double, double, CancellationToken, Task> timeUpdateCallback, double position)
    {
      Stop();
      using var playReleaseToken = await playSemaphore.LockAsync().ConfigureAwait(false);
      using var tokenSource = NewCancellationToken();
      var localToken = tokenSource.Token;
      var result = await Task.Run(async () =>
      {
        using var audioFile = GetAudioFile(filePath);
        var audioFileLength = audioFile.GetLength();
        var totalTime = audioFileLength.TotalMilliseconds / 1000.0;
        audioFile.SetPosition(audioFileLength * position);

        var streamingBufferTime = TimeSpan.FromMilliseconds(1000);
        var streamingBufferSize = (int)audioFile.GetRawElements(streamingBufferTime);

        var streamingBufferQueueSize = 5; // get from gui
        if (IsXFi)
        {
          var capacity = GetFreeXRam / streamingBufferSize;
          if (streamingBufferQueueSize > capacity)
          {
            streamingBufferQueueSize = capacity;
          }
        }

        if (streamingBufferQueueSize < 3)
        {
          streamingBufferQueueSize = 3;
        }

        var currentTime = audioFile.GetPosition().TotalMilliseconds / 1000;
        var interval = streamingBufferTime / 4;
        var soundData = new byte[streamingBufferSize];
        var initialized = false;
        using var bufferPool = new BufferPool(hasXram);
        while (true)
        {
          if (localToken.IsCancellationRequested)
          {
            FinishUp(bufferPool);
            return PlaybackResult.Stopped;
          }

          var state = AL.GetSourceState(source);
          if (state == ALSourceState.Stopped || (state == ALSourceState.Initial && initialized == true))
          {
            FinishUp(bufferPool);
            return PlaybackResult.Finished;
          }

          if (state == ALSourceState.Paused)
          {
            await SafeDelay(interval, localToken).ConfigureAwait(false);
            continue;
          }

          // queue new buffer if required
          QueueBuffer(audioFile, soundData, streamingBufferQueueSize, bufferPool);
          
          // clear played buffers and requeue them
          var processedBuffers = UnqueueBuffers(source, bufferPool);
          var processedSize = processedBuffers
          .Select(bufferId => {
            AL.GetBuffer(bufferId, ALGetBufferi.Size, out var size);
            return size;
          }).Sum();
          currentTime += audioFile.GetMilliseconds(processedSize) / 1000.0;
          foreach (var _ in processedBuffers)
          {
            QueueBuffer(audioFile, soundData, streamingBufferQueueSize, bufferPool);
          }

          if (state != ALSourceState.Playing && state != ALSourceState.Paused)
          {
            AL.SourcePlay(source);
            CheckALError("playing");
            initialized = true;
          }

          if (currentTime >= totalTime)
          {
            FinishUp(bufferPool);
            return PlaybackResult.Finished;
          }

          var offset = 0.0f;
          if (supportsOffset)
          {
            AL.GetSource(source, ALSourcef.SecOffset, out offset);
            CheckALError("checking offset");
          }

          try
          {
            await timeUpdateCallback(currentTime + offset, totalTime, localToken).ConfigureAwait(false);
          }
          catch (TaskCanceledException)
          {
            Trace.WriteLine($"Unable to update time while playing");
          }

          await SafeDelay(interval, localToken).ConfigureAwait(false);
        }
      }).ConfigureAwait(false);

      try
      {
        await timeUpdateCallback(0, 0, localToken).ConfigureAwait(false);
      }
      catch (TaskCanceledException)
      {
        Trace.WriteLine($"Unable to update time while stopped");
      }

      tokenSource.Cancel();
      return result;
    }

    private async Task SafeDelay(TimeSpan interval, CancellationToken token)
    {
      try
      {
        await Task.Delay(interval, token).ConfigureAwait(false);
      }
      catch (TaskCanceledException e)
      {
        Trace.WriteLine($"Task cancelled: {e.Message}");
      }
    }

    private void FinishUp(BufferPool bufferPool)
    {
      AL.SourceStop(source);
      CheckALError("stopping source when finishing up");
      var _ = UnqueueBuffers(source, bufferPool);
      while (AL.SourceUnqueueBuffer(source) > 0)
      {
        Trace.WriteLine("Extrenous buffer found");
      }
      AL.SourceRewind(source);
      CheckALError("rewinding source");
    }

    private CancellationTokenSource NewCancellationToken()
    {
      var cancellationTokenSource = new CancellationTokenSource();
      concurrentBag.Enqueue(cancellationTokenSource);
      return cancellationTokenSource;
    }

    private void QueueBuffer(IWaveSource audioFile, byte[] soundData, int streamingBufferQueueSize, BufferPool bufferPool)
    {
      if (audioFile.Position < audioFile.Length)
      {
        AL.GetSource(source, ALGetSourcei.BuffersQueued, out int buffersQueued);
        CheckALError("checking buffers queued");

        if (buffersQueued < streamingBufferQueueSize && audioFile.Position < audioFile.Length)
        {
          var size = audioFile.Length - audioFile.Position < soundData.Length
            ? (int)(audioFile.Length - audioFile.Position)
            : soundData.Length;

          audioFile.Read(soundData, 0, size);

          var soundBuffer = bufferPool.Rent();
          var format = GetSoundFormat(audioFile.WaveFormat.Channels, audioFile.WaveFormat.BitsPerSample);
          AL.BufferData(soundBuffer.Id, format, ref soundData[0], size, audioFile.WaveFormat.SampleRate);
          CheckALError("buffering data");

          AL.SourceQueueBuffer(source, soundBuffer.Id);
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

    private IEnumerable<int> UnqueueBuffers(int source, BufferPool bufferPool)
    {
      AL.GetSource(source, ALGetSourcei.BuffersProcessed, out int buffersProcessed);
      CheckALError("checking buffers processed");

      if (buffersProcessed > 0)
      {
        var dequeuedBuffers = AL.SourceUnqueueBuffers(source, buffersProcessed);
        CheckALError("dequeuing buffers");
        foreach (var dequeuedBuffer in dequeuedBuffers)
        {
          bufferPool.Return(dequeuedBuffer);
        }

        return dequeuedBuffers;
      }

      return new List<int>();
    }

    private IWaveSource GetAudioFile(string filePath)
    {
      IWaveSource? audioFile = null;
      try
      {
        audioFile = CodecFactory.Instance.GetCodec(filePath);
        if (audioFile.WaveFormat.WaveFormatTag != AudioEncoding.IeeeFloat
          && audioFile.WaveFormat.WaveFormatTag != AudioEncoding.Pcm
          && audioFile.WaveFormat.WaveFormatTag != AudioEncoding.Extensible)
        {
          audioFile = new LocalSampleToPcm16(audioFile.ToSampleSource());
        }

        if (audioFile.WaveFormat.WaveFormatTag == AudioEncoding.IeeeFloat)
        {
          if (!EXTFloat32.IsExtensionPresent())
          {
            if (IsXFi)
            {
              audioFile = new LocalSampleToPcm32(audioFile.ToSampleSource());
            }
            else
            {
              audioFile = new LocalSampleToPcm16(audioFile.ToSampleSource());
            }
          }
        }
        else if (audioFile.WaveFormat.BitsPerSample == 32)
        {
          if (!IsXFi)
          {
            if (EXTFloat32.IsExtensionPresent())
            {
              audioFile = new SampleToIeeeFloat32(audioFile.ToSampleSource());
            }
            else
            {
              audioFile = new LocalSampleToPcm16(audioFile.ToSampleSource());
            }
          }
        }
        else if (audioFile.WaveFormat.BitsPerSample == 24)
        {
          if (IsXFi)
          {
            audioFile = new LocalSampleToPcm32(audioFile.ToSampleSource());
          }
          else if (EXTFloat32.IsExtensionPresent())
          {
            audioFile = new SampleToIeeeFloat32(audioFile.ToSampleSource());
          }
          else
          {
            audioFile = new LocalSampleToPcm16(audioFile.ToSampleSource());
          }
        }

        return audioFile;
      }
      catch
      {
        audioFile?.Dispose();
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
                // Undocumented enum
                return (ALFormat)AL.GetEnumValue("AL_FORMAT_STEREO32");
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
        if (disposing)
        {
          playSemaphore.Wait();
          playSemaphore.Dispose();
        }

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
