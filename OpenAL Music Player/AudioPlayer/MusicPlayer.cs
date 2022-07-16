using System;
using System.Collections.Generic;

using System.Threading;
using System.Threading.Tasks;
using OpenALMusicPlayer.AudioEngine;
using OpenTK.Audio.OpenAL;

namespace OpenALMusicPlayer.AudioPlayer
{
  class MusicPlayer : IDisposable
  {

    #region Fields
    private AudioEngine.AudioPlayer audioPlayer;

    // list with all file paths
    List<string> musicList;

    // player state
    PlayerState currentState;
    int currentMusic;
    double trackTotalTime = 0;
    double trackCurrentTime = 0;
    float volumePercentage = 100;
    float volume = 1f;
    float pitch = 1f;

    // Repeat
    private RepeatType repeat_setting = RepeatType.All;
    private bool disposedValue;
    #endregion

    #region Properties
    public RepeatType RepeatSetting
    {
      get
      {
        return repeat_setting;
      }

      set
      {
        repeat_setting = value;
      }
    }

    public List<string> MusicList
    {
      get
      {
        return musicList;
      }

      set
      {
        musicList = value;
      }
    }

    /// <summary>
    /// Starts at 1 to the user, but internally starts at zero.
    /// </summary>
    public int CurrentMusic
    {
      get
      {
        return currentMusic + 1;
      }

      set
      {
        currentMusic = value - 1;
        currentState = PlayerState.ChangingTrack;
        audioPlayer.Stop();
      }
    }

    /// <summary>
    /// Volume, from 0 to 100.
    /// </summary>
    public float Volume
    {
      get
      {
        return volumePercentage;
      }

      set
      {
        volumePercentage = value;
        //volume = 0.0031623f * (float)Math.Exp(volumePercentage / 100 * 5.757f);
        volume = (float)Math.Pow(volumePercentage / 100, 3);
        audioPlayer.Gain = volume;
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
        audioPlayer.Pitch = pitch;
      }
    }

    public PlayerState Status
    {
      get
      {
        return currentState;
      }
    }

    public double TrackCurrentTime
    {
      get
      {
        return trackCurrentTime;
      }
    }

    public double TrackTotalTime
    {
      get
      {
        return trackTotalTime;
      }

    }

    public bool IsXFi
    {
      get
      {
        return audioPlayer.IsXFi;
      }
    }

    public float XRamFree
    {
      get
      {
        return audioPlayer.GetFreeXRam;
      }
    }

    public float XRamTotal
    {
      get
      {
        return audioPlayer.GetSizeXRam;
      }
    }
    #endregion

    #region Constructor
    public MusicPlayer(List<string> filePaths, string device)
    {
      currentState = PlayerState.Stopped;
      currentMusic = 0;
      volumePercentage = 100;
      pitch = 1f;

      audioPlayer = new AudioEngine.AudioPlayer(device);

      if (filePaths != null)
      {
        musicList = filePaths;
      }
    }
    #endregion

    #region Public methods
    public async Task Play()
    {
      if (currentState == PlayerState.Paused)
      {
        audioPlayer.Unpause();
        currentState = PlayerState.Playing;
        return;
      }
      currentState = PlayerState.Playing;

      audioPlayer.Gain = volume;
      audioPlayer.Pitch = pitch;
      var player = audioPlayer;
      while (currentState == PlayerState.Playing)
      {
        if (!player.IsValid)
        {
          return;
        }
        await player.Play(
          musicList[currentMusic],
          (current, total) =>
          {
            trackTotalTime = total;
            trackCurrentTime = current;
          },
          CancellationToken.None);
        if (currentState == PlayerState.Playing)
        {
          NextTrack(false);
        }
        if (currentState == PlayerState.ChangingTrack)
        {
          currentState = PlayerState.Playing;
        }
      }
    }

    public void Stop()
    {
      currentState = PlayerState.Stopped;
      audioPlayer.Stop();
    }

    public void NextTrack(bool force_next = true)
    {
      if (force_next)
      {
        currentMusic = (currentMusic + 1) % musicList.Count;
        currentState = PlayerState.ChangingTrack;
      }
      else
      {
        if (repeat_setting == RepeatType.All)
        {
          currentMusic = (currentMusic + 1) % musicList.Count;
          currentState = PlayerState.ChangingTrack;
        }
        else if (repeat_setting == RepeatType.Song)
        {
          currentState = PlayerState.ChangingTrack;
        }
        else if (repeat_setting == RepeatType.No)
        {
          if (currentMusic + 1 == musicList.Count)
          {
            currentState = PlayerState.Stopped;
          }
          else
          {
            currentMusic = (currentMusic + 1) % musicList.Count;
            currentState = PlayerState.ChangingTrack;
          }
        }
      }
      var player = audioPlayer;
      if (player != null)
      {
        audioPlayer.Stop();
      }
    }

    public void PreviousTrack()
    {
      if (currentMusic == 0)
      {
        currentMusic = musicList.Count - 1;
      }
      else
      {
        currentMusic = currentMusic - 1;
      }
      currentState = PlayerState.ChangingTrack;
      audioPlayer.Stop();
    }

    public void Pause()
    {
      currentState = PlayerState.Paused;
      audioPlayer.Pause();
    }

    public void Unpause()
    {
      currentState = PlayerState.Playing;
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
          if (audioPlayer != null)
          {
            audioPlayer.Dispose();
          }
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
