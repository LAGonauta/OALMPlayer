using System;
using System.Collections.Generic;

using System.Threading;
using OpenALMusicPlayer.AudioEngine;
using OpenTK.Audio.OpenAL;

namespace OpenALMusicPlayer.AudioPlayer
{
  class MusicPlayer : IDisposable
  {

    #region Fields
    private AudioEngine.AudioPlayer audioPlayer;

    // timer
    Timer timer;
    uint timerPeriod;

    // list with all file paths
    List<string> musicList;

    // player state
    PlayerState currentState;
    PlayerState lastState;
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

    /// <summary>
    /// Time in ms between each update of the player
    /// </summary>
    public uint UpdateRate
    {
      get
      {
        return timerPeriod;
      }

      set
      {
        timerPeriod = value;
        timer.Change(0, timerPeriod);
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
        Now();
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
        if (audioPlayer != null)
        {
          audioPlayer.Gain = volume;
        }
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
        if (audioPlayer != null)
        {
          audioPlayer.Pitch = pitch;
        }
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
      timerPeriod = 150;
      currentMusic = 0;
      volumePercentage = 100;
      pitch = 1f;

      audioPlayer = new AudioEngine.AudioPlayer(device);
      timer = new Timer(new TimerCallback(DetectChanges), null, timerPeriod, timerPeriod);

      if (filePaths != null)
      {
        musicList = filePaths;
      }
      StartPlayer();
    }
    #endregion

    #region Public methods
    public void Update()
    {

    }

    public void Play()
    {
      currentState = PlayerState.StartPlayback;
      Now();
    }

    public void Stop()
    {
      currentState = PlayerState.StopPlayback;
      Now();
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
            currentState = PlayerState.StopPlayback;
          }
          else
          {
            currentMusic = (currentMusic + 1) % musicList.Count;
            currentState = PlayerState.ChangingTrack;
          }
        }
      }

      Now();
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
      Now();
    }

    public void Pause()
    {
      currentState = PlayerState.Pausing;
      Now();
    }

    public void Unpause()
    {
      currentState = PlayerState.Unpausing;
      Now();
    }
    #endregion

    #region Private methods
    private void StartPlayer()
    {
      Now();
    }

    /// <summary>
    /// Force the timer to restart.
    /// </summary>
    private void Now()
    {
      timer.Change(0, timerPeriod);
    }

    private void TimerDisable()
    {
      timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private void TimerEnable()
    {
      timer.Change(timerPeriod, timerPeriod);
    }

    private void DetectChanges(object obj)
    {
      TimerDisable();
      switch (currentState)
      {
        case PlayerState.Stopped:
          // waiting user input
          lastState = PlayerState.Stopped;
          break;

        case PlayerState.StartPlayback:
          if (currentState == PlayerState.Paused)
          {
            currentState = PlayerState.Unpausing;
          }
          else
          {
            currentState = PlayerState.ChangingTrack;
          }
          lastState = PlayerState.StartPlayback;
          break;

        case PlayerState.Playing:
          if (audioPlayer.State != ALSourceState.Playing)
          {
            NextTrack(false);
          }

          trackCurrentTime = audioPlayer.CurrentTime;

          lastState = PlayerState.Playing;
          break;

        case PlayerState.Paused:
          // waiting user input
          lastState = PlayerState.Paused;
          break;

        case PlayerState.Pausing:
          audioPlayer.Pause();
          currentState = PlayerState.Paused;
          lastState = PlayerState.Pausing;
          break;

        case PlayerState.Unpausing:
          audioPlayer.Unpause();
          currentState = PlayerState.Playing;
          lastState = PlayerState.Unpausing;
          break;

        case PlayerState.ChangingTrack:
          trackTotalTime = audioPlayer.TotalTime;
          audioPlayer.Gain = volume;
          audioPlayer.Play(musicList[currentMusic], CancellationToken.None);
          audioPlayer.Pitch = pitch;

          currentState = PlayerState.Playing;
          lastState = PlayerState.ChangingTrack;
          break;

        case PlayerState.StopPlayback:
          audioPlayer.Stop();
          currentState = PlayerState.Stopped;
          lastState = PlayerState.StopPlayback;
          break;
      }
      TimerEnable();
    }

    protected virtual void Dispose(bool disposing)
    {
      if (!disposedValue)
      {
        if (disposing)
        {
          if (timer != null)
          {
            timer.Change(Timeout.Infinite, Timeout.Infinite);
            timer.Dispose();
            timer = null;
          }

          if (audioPlayer != null)
          {
            audioPlayer.Dispose();
            audioPlayer = null;
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
