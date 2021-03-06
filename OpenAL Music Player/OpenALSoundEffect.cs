﻿using System;
using System.Collections.Generic;

using OpenTK;
using OpenTK.Audio;
using OpenTK.Audio.OpenAL;

using SoundBuffer = System.Int32;
using Effect = System.Int32;
using Filter = System.Int32;

using System.IO;

using CSCore;
using CSCore.Codecs;
using CSCore.Streams.SampleConverter;
using System.Timers;
using System.Diagnostics;

namespace OALEngine
{
  public class OpenALSoundEffect : IDisposable
  {
    #region Fields
    // audio device
    OpenALEngine alengine;
    IList<string> devices = AudioContext.AvailableDevices;
    IWaveSource AudioFile;

    // static buffer
    SoundBuffer buffer;

    // streaming support
    const int streamingBufferTime = 1000; // in milliseconds
    int streamingBufferQueueSize = 0;
    int streamingBufferSize;
    Timer streamingTimer;
    Timer streamingCurrentTimeTimer;
    Stopwatch streamingCurrentTimeStopWatch;
    List<SoundBuffer> bufferList = new List<SoundBuffer>();

    // debug streaming
    long bufferedTotal = 0;
    long releasedTotal = 0;

    // sources
    List<OpenALEngine.SourceInstance> sources = new List<OpenALEngine.SourceInstance>();

    // filter
    Filter filter;

    // valid sound effect
    bool isValid = true;

    // streaming support
    bool streamingSoundEffect = false;

    // looping support
    bool looping = false;

    // pitch
    float pitch = 1f;

    // gain
    float gain = 1f;
    float mingain = 0f;

    // time in seconds
    float currentTime = 0;
    float totalTime = 0;

    #endregion

    #region Properties
    public bool IsLooped
    {
      get
      {
        return looping;
      }
      set
      {
        looping = value;

        foreach (OpenALEngine.SourceInstance source in sources)
        {
          if (AL.IsSource(source.id))
            AL.Source(source.id, ALSourceb.Looping, value);
        }
      }
    }

    public bool IsStream
    {
      get
      {
        return streamingSoundEffect;
      }
    }

    public bool IsPlaying
    {
      get
      {
        foreach (OpenALEngine.SourceInstance source in sources)
        {
          if (AL.GetSourceState(source.id) == ALSourceState.Playing)
          {
            return true;
          }
        }
        return false;
      }
    }

    public Filter Filter
    {
      get
      {
        return filter;
      }

      set
      {
        filter = value;
      }
    }

    public float Pitch
    {
      get
      {
        return pitch;
      }

      set
      {
        pitch = value;

        foreach (OpenALEngine.SourceInstance source in sources)
        {
          if (AL.IsSource(source.id))
            AL.Source(source.id, ALSourcef.Pitch, pitch);
        }
      }
    }

    public float MinGain
    {
      get
      {
        return mingain;
      }
      set
      {
        mingain = value;

        foreach (OpenALEngine.SourceInstance source in sources)
        {
          if (AL.IsSource(source.id))
          {
            AL.Source(source.id, ALSourcef.MinGain, mingain);
          }
        }
      }
    }

    public float Gain
    {
      get
      {
        return gain;
      }

      set
      {
        gain = value;
        foreach (OpenALEngine.SourceInstance source in sources)
        {
          if (AL.IsSource(source.id))
          {
            AL.Source(source.id, ALSourcef.Gain, gain);
          }
        }
      }
    }

    /// <summary>
    /// Set or gets effects current running time
    /// </summary>
    public float CurrentTime
    {
      get
      {
        if (streamingSoundEffect)
        {
          //return (float)AudioFile.GetPosition().TotalSeconds - streamingBufferTime / 1000 * streamingBufferQueueSize;
          return currentTime;
        }
        else
        {
          foreach (OpenALEngine.SourceInstance source in sources)
          {
            if (AL.IsSource(source.id))
              AL.GetSource(source.id, ALSourcef.SecOffset, out currentTime);
          }
        }
        return currentTime;
      }

      set
      {
        if (streamingSoundEffect)
        {
          try
          {
            AudioFile.SetPosition(new TimeSpan(0, 0, (int)value));
          }
          catch
          {

          }
        }
        else
        {
          foreach (OpenALEngine.SourceInstance source in sources)
          {
            if (AL.IsSource(source.id))
              AL.Source(source.id, ALSourcef.SecOffset, value);
          }
        }
      }
    }

    public float TotalTime
    {
      get
      {
        return totalTime;
      }
    }

    #endregion

    #region Constructors
    /// <summary>
    /// Generates the SoundEffect, should also check file type. Not sure if we need to pass the AudioContext...
    /// </summary>
    /// <param name="filePath"></param>
    /// <param name="ac"></param>
    public OpenALSoundEffect(string filePath, ref OpenALEngine alengine, bool streaming = false)
    {
      if (alengine != null && filePath != null)
      {
        this.alengine = alengine;
        streamingSoundEffect = streaming;

        alengine.CheckALError("not found while reseting error state");

        if (!streamingSoundEffect)
        {
          this.buffer = ALContentLoad(filePath);
        }
        else
        {
          ALContentBufferStream(filePath);

          // activate queue timer
          streamingTimer = new Timer();
          streamingTimer.Interval = streamingBufferTime / streamingBufferQueueSize;
          streamingTimer.Elapsed += new ElapsedEventHandler(this.CheckQueue);

          // enable currentTime timer
          streamingCurrentTimeTimer = new Timer();
          streamingCurrentTimeTimer.Interval = 500;
          streamingCurrentTimeTimer.Elapsed += new ElapsedEventHandler(this.UpdateCurrentTime);
          streamingCurrentTimeTimer.Start();

          // stopwatch
          streamingCurrentTimeStopWatch = new Stopwatch();
        }
        this.filter = alengine.GenFilter();
      }
    }
    #endregion

    #region Destructors
    protected virtual void Dispose(bool disposing)
    {
      if (disposing)
      {
        if (isValid)
        {
          isValid = false;
          if (streamingSoundEffect)
          {
            if (streamingTimer != null)
            {
              streamingTimer.Stop();
              streamingTimer = null;
            }

            if (streamingCurrentTimeTimer != null)
            {
              streamingCurrentTimeTimer.Stop();
              streamingCurrentTimeTimer = null;
            }

            this.Stop();
            if (sources.Count > 0)
            {
              Trace.WriteLine("Number of sources = " + sources.Count);
              for (int i = sources.Count - 1; i >= 0; --i)
              {
                if (AL.IsSource(sources[i].id))
                {
                  // unqueue any buffers
                  int buffersProcessed = 0;
                  AL.GetSource(sources[i].id, ALGetSourcei.BuffersProcessed, out buffersProcessed);
                  alengine.CheckALError("getting processed buffers");

                  if (buffersProcessed > 0)
                  {
                    SoundBuffer[] dequeuedBuffers = AL.SourceUnqueueBuffers(sources[i].id, buffersProcessed);

                    foreach (SoundBuffer dequeuedBuffer in dequeuedBuffers)
                    {
                      AL.DeleteBuffer(dequeuedBuffer);
                      alengine.CheckALError("deleting buffer");
                      ++releasedTotal;
                    }

                  }

                  // unbind all buffers from source
                  AL.BindBufferToSource(sources[i].id, 0);

                  // clear source
                  AL.DeleteSource(sources[i].id);
                  alengine.CheckALError("error deleting source");

                  sources.RemoveAt(i);
                }
              }

              if (bufferedTotal != releasedTotal)
              {
                Trace.WriteLine("Buffered = " + bufferedTotal);
                Trace.WriteLine("Released = " + releasedTotal);
                Trace.WriteLine("Count = " + bufferList.Count);

                foreach (SoundBuffer buffer in bufferList)
                {
                  if (AL.IsBuffer(buffer))
                  {
                    AL.DeleteBuffer(buffer);
                  }
                }
              }

              bufferList.Clear();
              Trace.WriteLine("Disposed.");
            }
          }
          else
          {
            this.Stop();
            alengine.Check();
            alengine.DeleteBuffer(buffer);
          }
        }
      }
    }

    public void Dispose()
    {
      this.Dispose(true);
      GC.SuppressFinalize(this);
    }

    ~OpenALSoundEffect()
    {
      this.Dispose(false);
    }
    #endregion

    #region Public methods
    public void Stop()
    {
      for (int i = sources.Count - 1; i >= 0; --i)
      {
        if (AL.GetSourceState(sources[i].id) != ALSourceState.Stopped)
        {
          AL.SourceStop(sources[i].id);
        }
      }
      currentTime = 0;
      this.Check();
    }

    public void Pause()
    {
      foreach (OpenALEngine.SourceInstance source in sources)
      {
        if (AL.GetSourceState(source.id) != ALSourceState.Paused)
        {
          AL.SourcePause(source.id);
        }
      }

      if (streamingSoundEffect)
      {
        streamingCurrentTimeStopWatch.Stop();
        streamingTimer.Stop();
      }
    }

    public void Unpause()
    {
      foreach (OpenALEngine.SourceInstance source in sources)
      {
        if (AL.GetSourceState(source.id) == ALSourceState.Paused)
        {
          AL.SourcePlay(source.id);
        }
      }

      if (streamingSoundEffect)
      {
        streamingCurrentTimeStopWatch.Start();
        streamingTimer.Start();
      }

      // For some reason the source is muted when unpausing on OpenAL Soft
      this.Gain = this.Gain;
    }

    public OpenALEngine.SourceInstance Play()
    {
      OpenALEngine.SourceInstance source;

      if (streamingSoundEffect)
      {
        // lets do it here for now... or for ever.
        source = new OpenALEngine.SourceInstance();
        source.id = AL.GenSource();
        source.priority = OpenALEngine.SoundPriority.BestEffort;

        if (IsLooped)
        {
          AL.Source(source.id, ALSourceb.Looping, true);
          source.looping = true;
        }
        else
        {
          source.looping = false;
        }

        sources.Add(source);
        StartStreaming();
        currentTime = 0;
        AL.SourcePlay(source.id);

        if (!streamingCurrentTimeStopWatch.IsRunning)
          streamingCurrentTimeStopWatch.Start();
        else
          streamingCurrentTimeStopWatch.Restart();

        streamingTimer.Start();
      }
      else
      {
        source = alengine.PlaySound(looping, buffer);
        sources.Add(source);
      }

      return source;
    }

    public OpenALEngine.SourceInstance Play(float volume)
    {
      OpenALEngine.SourceInstance source;

      if (streamingSoundEffect)
      {
        // lets do it here for now... or for ever.
        source = new OpenALEngine.SourceInstance();
        source.id = AL.GenSource();
        source.priority = OpenALEngine.SoundPriority.BestEffort;

        if (IsLooped)
        {
          AL.Source(source.id, ALSourceb.Looping, true);
          source.looping = true;
        }
        else
        {
          source.looping = false;
        }

        sources.Add(source);
        StartStreaming();
        currentTime = 0;
        this.Gain = volume;
        AL.SourcePlay(source.id);

        if (!streamingCurrentTimeStopWatch.IsRunning)
          streamingCurrentTimeStopWatch.Start();
        else
          streamingCurrentTimeStopWatch.Restart();

        streamingTimer.Start();
      }
      else
      {
        source = alengine.PlaySound(looping, buffer, volume);
        sources.Add(source);
      }

      return source;
    }

    ///                         ///
    /// Two-dimension functions ///
    ///                         ///

    public OpenALEngine.SourceInstance Play(float volume, Vector2 position)
    {
      var source = alengine.PlaySound(looping, buffer, volume, position);
      sources.Add(source);
      return source;
    }

    public OpenALEngine.SourceInstance Play(float volume, int xPosition, int yPosition)
    {
      var source = alengine.PlaySound(looping, buffer, volume, xPosition, yPosition);
      sources.Add(source);
      return source;
    }

    public OpenALEngine.SourceInstance Play(float volume, Vector2 position, Vector2 velocity)
    {
      var source = alengine.PlaySound(looping, buffer, volume, position, velocity);
      sources.Add(source);
      return source;
    }

    public OpenALEngine.SourceInstance Play(float volume, int xPosition, int yPosition, int xVelocity, int yVelocity)
    {
      var source = alengine.PlaySound(looping, buffer, volume, xPosition, yPosition, xVelocity, yVelocity);
      sources.Add(source);
      return source;
    }

    //public void SetPosition(OpenALEngine.SourceInstance source, Vector2 position)
    //{

    //}

    //public void SetPosition(OpenALEngine.SourceInstance source, Vector2 position)
    //{

    //}

    //public void SetVelocity(OpenALEngine.SourceInstance source, Vector2 position)
    //{

    //}

    //public void SetPosition(OpenALEngine.SourceInstance source, Vector2 position)
    //{

    //}

    ///                           ///
    /// Three-dimension functions ///
    ///                           ///

    public OpenALEngine.SourceInstance Play(float volume, Vector3 position)
    {
      var source = alengine.PlaySound(looping, buffer, volume, ref position);
      sources.Add(source);
      return source;
    }

    public OpenALEngine.SourceInstance Play(float volume, int xPosition, int yPosition, int zPosition)
    {
      var source = alengine.PlaySound(looping, buffer, volume, xPosition, yPosition, zPosition);
      sources.Add(source);
      return source;
    }

    public OpenALEngine.SourceInstance Play(float volume, Vector3 position, Vector3 velocity)
    {
      var source = alengine.PlaySound(looping, buffer, volume, ref position, ref velocity);
      sources.Add(source);
      return source;
    }

    public OpenALEngine.SourceInstance Play(float volume, int xPosition, int yPosition, int zPosition, int xVelocity, int yVelocity, int zVelocity)
    {
      var source = alengine.PlaySound(looping, buffer, volume, xPosition, yPosition, zPosition, xVelocity, yVelocity, zVelocity);
      sources.Add(source);
      return source;
    }

    public void SetPosition(ref Vector3 position)
    {
      foreach (OpenALEngine.SourceInstance source in sources)
      {
        if (AL.IsSource(source.id))
          AL.Source(source.id, ALSource3f.Position, ref position);
      }
    }

    public void SetVelocity(ref Vector3 velocity)
    {
      foreach (OpenALEngine.SourceInstance source in sources)
      {
        if (AL.IsSource(source.id))
          AL.Source(source.id, ALSource3f.Velocity, ref velocity);
      }
    }

    ///             ///
    /// EFX support ///
    ///             ///

    //public void Filter

    public void Check()
    {
      for (int i = sources.Count - 1; i >= 0; --i)
      {
        if (!AL.IsSource(sources[i].id))
        {
          sources.RemoveAt(i);
        }
      }
    }
    #endregion

    #region Private methods
    /// <summary>
    /// Get correct OpenAL sound enum from file information
    /// </summary>
    /// <param name="channels">numberr of audio channels</param>
    /// <param name="bits">number of bits</param>
    /// <returns></returns>
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
              if (alengine.IsXFi)
              {
                throw new NotSupportedException("The specified sound format is not supported.");
                //return 0x1203;
              }
              else
              {
                if (alengine.IeeeFloat32Support)
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
              if (alengine.IsXFi)
              {
                // Undocumented value
                return (ALFormat)0x1203;
              }
              else
              {
                if (alengine.IeeeFloat32Support)
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

    private SoundBuffer ALContentLoad(string filePath)
    {
      AudioFile = CodecFactory.Instance.GetCodec(filePath);

      if (alengine.IsXFi && AudioFile.WaveFormat.WaveFormatTag == AudioEncoding.IeeeFloat)
      {
        var toSample = AudioFile.ToSampleSource();
        if (AudioFile.WaveFormat.BitsPerSample == 32)
        {
          AudioFile = new SampleToPcm32(toSample);
        }
        else if (AudioFile.WaveFormat.BitsPerSample == 16)
        {
          AudioFile = new SampleToPcm16(toSample);
        }
        else
        {
          AudioFile = new SampleToPcm8(toSample);
        }
      }

      byte[] sound_data;
      if (AudioFile.Length > 0)
        sound_data = new byte[AudioFile.Length];
      else
        sound_data = new byte[0];

      AudioFile.Read(sound_data, 0, sound_data.Length);

      SoundBuffer soundBuffer = AL.GenBuffer();
      AL.BufferData(soundBuffer, GetSoundFormat(AudioFile.WaveFormat.Channels, AudioFile.WaveFormat.BitsPerSample), sound_data, sound_data.Length, AudioFile.WaveFormat.SampleRate);
      totalTime = AudioFile.Length * sizeof(byte) / AudioFile.WaveFormat.BytesPerSecond;
      //totalTime = AudioFile.GetMilliseconds(AudioFile.GetRawElements(AudioFile.GetLength()) / 1000;
      //totalTime = (float)AudioFile.GetLength().TotalSeconds;

      AudioFile.Dispose();
      return soundBuffer;
    }

    private void ALContentBufferStream(string filePath)
    {
      AudioFile = CodecFactory.Instance.GetCodec(filePath);

      if (alengine.IsXFi && AudioFile.WaveFormat.WaveFormatTag == AudioEncoding.IeeeFloat)
      {
        var toSample = AudioFile.ToSampleSource();
        if (AudioFile.WaveFormat.BitsPerSample == 32)
          AudioFile = new SampleToPcm32(toSample);
        else if (AudioFile.WaveFormat.BitsPerSample == 16)
          AudioFile = new SampleToPcm16(toSample);
        else
          AudioFile = new SampleToPcm8(toSample);
      }

      //totalTime = AudioFile.Length * sizeof(byte) / AudioFile.WaveFormat.BytesPerSecond;
      totalTime = AudioFile.GetMilliseconds(AudioFile.GetRawElements(AudioFile.GetLength())) / 1000;
      streamingBufferSize = (int)AudioFile.GetRawElements(streamingBufferTime);

      // set queue size
      if (alengine.IsXFi)
      {
        streamingBufferQueueSize = 100; // get from gui
        if (streamingBufferQueueSize > alengine.GetXRamFree / streamingBufferSize)
        {
          streamingBufferQueueSize = alengine.GetXRamFree / streamingBufferSize;
        }         
      }

      if (streamingBufferQueueSize < 3)
        streamingBufferQueueSize = 3;
    }

    /// <summary>
    /// This will check a source queue
    /// </summary>
    private void CheckQueue(object obj, EventArgs e)
    {
      streamingTimer.Stop();
      if (this.isValid)
      {
        for (int i = 0, final = sources.Count; i < final; ++i)
        {
          if (AL.GetSourceType(sources[i].id) == ALSourceType.Streaming)
          {
            int buffersQueued, buffersProcessed;
            int size = 0;

            // queue new buffers
            AL.GetSource(sources[i].id, ALGetSourcei.BuffersQueued, out buffersQueued);
            alengine.CheckALError("checking buffers queued buffer");

            if (buffersQueued < streamingBufferQueueSize && AudioFile.Position < AudioFile.Length)
            {
              if (AudioFile.Length - AudioFile.Position < streamingBufferSize)
                size = (int)(AudioFile.Length - AudioFile.Position);
              else
                size = streamingBufferSize;

              byte[] sound_data;
              sound_data = new byte[size];
              AudioFile.Read(sound_data, 0, sound_data.Length);

              if (this.isValid)
              {
                SoundBuffer soundBuffer = AL.GenBuffer();
                AL.BufferData(soundBuffer, GetSoundFormat(AudioFile.WaveFormat.Channels, AudioFile.WaveFormat.BitsPerSample), sound_data, sound_data.Length, AudioFile.WaveFormat.SampleRate);
                alengine.CheckALError("buffering data");
                bufferList.Add(soundBuffer);

                AL.SourceQueueBuffer(sources[i].id, soundBuffer);
                alengine.CheckALError("queueing buffer");

                ++bufferedTotal;
              }
            }

            // clear played buffers
            AL.GetSource(sources[i].id, ALGetSourcei.BuffersProcessed, out buffersProcessed);
            alengine.CheckALError("checking buffers processed");

            if (buffersProcessed > 0)
            {
              SoundBuffer[] dequeuedBuffers = AL.SourceUnqueueBuffers(sources[i].id, buffersProcessed);

              foreach (SoundBuffer dequeuedBuffer in dequeuedBuffers)
              {
                AL.DeleteBuffer(dequeuedBuffer);
                alengine.CheckALError("deleting buffer");

                // maybe check for errors here?
                ++releasedTotal;
              }
            }
          }
        }
      }
      streamingTimer.Start();
    }

    private void StartStreaming()
    {
      streamingTimer.Stop();
      if (this.isValid && streamingSoundEffect)
      {
        for (int i = 0, final = sources.Count; i < final; ++i)
        {
          if (AL.GetSourceState(sources[i].id) == ALSourceState.Initial)
          {
            // start queuing buffers
            if (AudioFile.Position < AudioFile.Length)
            {
              byte[] sound_data;
              if (AudioFile.Length - AudioFile.Position < streamingBufferSize)
                sound_data = new byte[AudioFile.Length - AudioFile.Position];
              else
                sound_data = new byte[streamingBufferSize];

              AudioFile.Read(sound_data, 0, sound_data.Length);
              
              SoundBuffer soundBuffer = AL.GenBuffer();
              AL.BufferData(soundBuffer, GetSoundFormat(AudioFile.WaveFormat.Channels, AudioFile.WaveFormat.BitsPerSample), sound_data, sound_data.Length, AudioFile.WaveFormat.SampleRate);
              alengine.CheckALError("buffering data (stream start)");
              bufferList.Add(soundBuffer);

              AL.SourceQueueBuffer(sources[i].id, soundBuffer);
              alengine.CheckALError("queueing buffer (stream start)");

              ++bufferedTotal;
            }
          }
        }

        streamingTimer.Start();
      }
    }

    private void UpdateCurrentTime(object obj, EventArgs e)
    {
      streamingCurrentTimeTimer.Stop();
      if (this.IsPlaying)
        this.currentTime += (float)streamingCurrentTimeStopWatch.ElapsedMilliseconds / 1000 * pitch;
      streamingCurrentTimeStopWatch.Restart();
      streamingCurrentTimeTimer.Start();
    }
    #endregion
  }
}
