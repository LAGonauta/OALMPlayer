using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenALMusicPlayer.AudioEngine;
using OpenALMusicPlayer.GUI.Model;

namespace OpenALMusicPlayer.AudioPlayer
{
  class MusicPlayer : IDisposable
  {

    #region Fields
    private readonly ITrackNumber trackNumber;
    private readonly Func<double, double, CancellationToken, Task> trackPositionUpdateCallback;

    private AudioEngine.AudioPlayer audioPlayer;
    private int currentMusic = 0;
    private double volumePercentage = 100;
    private double volume = 1;
    private float pitch = 1f;
    private bool disposedValue;
    #endregion

    #region Properties
    public RepeatType RepeatSetting { get; set; } = RepeatType.All;

    public List<string> MusicList { get; set; }

    /// <summary>
    /// Starts at 1 to the user, but internally starts at zero.
    /// </summary>
    public int CurrentMusicIndex
    {
      get => currentMusic + 1;
      set => currentMusic = value - 1;
    }

    /// <summary>
    /// Volume, from 0 to 100.
    /// </summary>
    public double Volume
    {
      get => volumePercentage;
      set
      {
        volumePercentage = value;
        //volume = 0.0031623 * Math.Exp(volumePercentage / 100 * 5.757);
        volume = Math.Pow(volumePercentage / 100, 3);
        audioPlayer.Gain = (float)volume;
      }
    }

    public float Pitch
    {
      get => pitch;
      set => audioPlayer.Pitch = pitch = value;
    }

    public PlayerState Status { get; private set; } = PlayerState.Stopped;

    public double TrackCurrentTime { get; private set; } = 0;

    public double TrackTotalTime { get; private set; } = 0;

    public bool IsXFi => audioPlayer.IsXFi;

    public float XRamFree => audioPlayer.GetFreeXRam;

    public float XRamTotal => audioPlayer.GetSizeXRam;
    #endregion

    #region Constructor
    public MusicPlayer(string device, List<string> filePaths, ITrackNumber trackNumber, Func<double, double, CancellationToken, Task> trackPositionUpdateCallback)
    {
      audioPlayer = new AudioEngine.AudioPlayer(device);
      MusicList = filePaths;
      this.trackNumber = trackNumber;
      this.trackPositionUpdateCallback = trackPositionUpdateCallback;
    }
    #endregion

    #region Public methods
    public async Task Play(double position)
    {
      if (Status == PlayerState.Paused)
      {
        audioPlayer.Unpause();
        Status = PlayerState.Playing;
        return;
      }
      audioPlayer.Gain = (float)volume;
      audioPlayer.Pitch = pitch;

      Status = PlayerState.Playing;
      var player = audioPlayer;
      do
      {
        if (!player.IsValid)
        {
          return;
        }
        trackNumber.Set(CurrentMusicIndex);
        var result = await player.Play(
          MusicList[currentMusic],
          async (current, total, token) =>
          {
            TrackTotalTime = total;
            TrackCurrentTime = current;
            await trackPositionUpdateCallback(current, total, token).ConfigureAwait(false);
          }, position).ConfigureAwait(false);

        if (result == PlaybackResult.Finished)
        {
          NextTrack(true);
          position = 0;
          if (Status == PlayerState.Stopped)
          {
            trackNumber.Set(0);
          }
        }
        else if (result == PlaybackResult.Stopped)
        {
          return;
        }
      } while (Status == PlayerState.Playing);
    }

    public void Stop()
    {
      Status = PlayerState.Stopped;
      audioPlayer.Stop();
    }

    public void NextTrack(bool finished)
    {
      if (finished)
      {
        if (RepeatSetting == RepeatType.All)
        {
          CurrentMusicIndex = (CurrentMusicIndex % MusicList.Count) + 1;
        }
        else if (RepeatSetting == RepeatType.Song)
        {
          // Do nothing, it will repeat itself
        }
        else if (RepeatSetting == RepeatType.No)
        {
          if (CurrentMusicIndex == MusicList.Count)
          {
            Status = PlayerState.Stopped;
          }
          else
          {
            CurrentMusicIndex = (CurrentMusicIndex % MusicList.Count) + 1;
          }
        }
      }
      else
      {
        CurrentMusicIndex = (CurrentMusicIndex % MusicList.Count) + 1;
        audioPlayer.Stop();
      }
    }

    public void PreviousTrack()
    {
      if (CurrentMusicIndex == 1)
      {
        CurrentMusicIndex = MusicList.Count;
      }
      else
      {
        CurrentMusicIndex = CurrentMusicIndex - 1;
      }
      audioPlayer.Stop();
    }

    public void Pause()
    {
      Status = PlayerState.Paused;
      audioPlayer.Pause();
    }

    public void Unpause()
    {
      Status = PlayerState.Playing;
      audioPlayer.Unpause();
    }
    #endregion

    #region Private methods

    protected virtual void Dispose(bool disposing)
    {
      if (!disposedValue)
      {
        if (disposing)
        {
          audioPlayer?.Dispose();
        }

        disposedValue = true;
      }
    }

    public void Dispose()
    {
      // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
      Dispose(disposing: true);
      GC.SuppressFinalize(this);
    }
    #endregion
  }
}
