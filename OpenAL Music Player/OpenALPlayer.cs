using System;
using System.Collections.Generic;

using OALEngine;
using System.Threading;
using OpenTK.Audio;

namespace OpenALMusicPlayer
{
  class OpenALPlayer : IDisposable
  {

    #region Fields
    bool isValid;

    // sound engine
    OpenALEngine alengine;
    bool isXFi = false;

    // timer
    System.Threading.Timer timer;
    uint timerPeriod;

    // list with all file paths
    List<string> musicList;

    // player state
    PlayerState currentState;
    PlayerState lastState;
    int currentMusic;
    float trackTotalTime = 0;
    float trackCurrentTime = 0;
    float volumePercentage = 100;
    float volume = 1f;
    float pitch = 1f;

    // Repeat
    repeatType repeat_setting = repeatType.All;

    // sound "effect"
    OpenALSoundEffect music;

    // EFX effects
    int effect;
    #endregion

    #region Properties
    public repeatType RepeatSetting
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
        this.Now();
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
        if (music != null)
        {
          music.Gain = volume;
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
        if (music != null)
        {
          music.Pitch = pitch;
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

    public float TrackCurrentTime
    {
      get
      {
        return trackCurrentTime;
      }
    }

    public float TrackTotalTime
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
        return isXFi;
      }
    }

    public float XRamFree
    {
      get
      {
        return alengine.GetXRamFree;
      }
    }

    public float XRamTotal
    {
      get
      {
        return alengine.GetXRamSize;
      }
    }
    #endregion

    #region Constructor
    public OpenALPlayer(List<string> filePaths)
    {
      isValid = true;
      currentState = PlayerState.Stopped;
      timerPeriod = 150;
      currentMusic = 0;
      volumePercentage = 100;
      pitch = 1f;

      var devices = AudioContext.AvailableDevices;
      alengine = new OpenALEngine(devices[0], true, AudioContext.MaxAuxiliarySends.One, true);

      timer = new Timer(new TimerCallback(this.DetectChanges), null, timerPeriod, timerPeriod);

      if (filePaths != null)
      {
        musicList = filePaths;
      }

      this.StartPlayer();
    }

    public OpenALPlayer(List<string> filePaths, string device)
    {
      isValid = true;
      currentState = PlayerState.Stopped;
      timerPeriod = 150;
      currentMusic = 0;
      volumePercentage = 100;
      pitch = 1f;

      alengine = new OpenALEngine(device, true, AudioContext.MaxAuxiliarySends.One, true);

      timer = new Timer(new TimerCallback(this.DetectChanges), null, timerPeriod, timerPeriod);

      if (filePaths != null)
      {
        musicList = filePaths;
      }
      this.StartPlayer();
    }
    #endregion

    #region Destructor
    protected virtual void Dispose(bool disposing)
    {
      if (disposing)
      {
        if (isValid)
        {
          isValid = false;

          if (timer != null)
          {
            timer.Change(Timeout.Infinite, Timeout.Infinite);
            timer.Dispose();
            timer = null;
          }

          if (music != null)
          {
            music.Dispose();
            music = null;
          }

          if (alengine != null)
          {
            alengine.Dispose();
            alengine = null;
          }
        }
      }
    }

    public void Dispose()
    {
      this.Dispose(true);
      GC.SuppressFinalize(this);
    }

    ~OpenALPlayer()
    {
      this.Dispose();
    }
    #endregion

    #region Public methods
    public void Update()
    {

    }

    public void Play()
    {
      currentState = PlayerState.StartPlayback;
      this.Now();
    }

    public void Stop()
    {
      currentState = PlayerState.StopPlayback;
      this.Now();
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
        if (repeat_setting == repeatType.All)
        {
          currentMusic = (currentMusic + 1) % musicList.Count;
          currentState = PlayerState.ChangingTrack;
        }
        else if (repeat_setting == repeatType.Song)
        {
          currentState = PlayerState.ChangingTrack;
        }
        else if (repeat_setting == repeatType.No)
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

      this.Now();
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
      this.Now();
    }

    public void Pause()
    {
      currentState = PlayerState.Pausing;
      this.Now();
    }

    public void Unpause()
    {
      currentState = PlayerState.Unpausing;
      this.Now();
    }
    #endregion

    #region Private methods
    private void StartPlayer()
    {
      this.Now();
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
      this.TimerDisable();
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
          if (music != null)
          {
            if (!music.IsPlaying)
            {
              this.NextTrack(false);
            }
          }

          trackCurrentTime = music.CurrentTime;

          lastState = PlayerState.Playing;
          break;

        case PlayerState.Paused:
          // waiting user input
          lastState = PlayerState.Paused;
          break;

        case PlayerState.Pausing:
          if (music != null)
          {
            music.Pause();
          }
          currentState = PlayerState.Paused;
          lastState = PlayerState.Pausing;
          break;

        case PlayerState.Unpausing:
          if (music != null)
          {
            music.Unpause();
          }
          currentState = PlayerState.Playing;
          lastState = PlayerState.Unpausing;
          break;

        case PlayerState.ChangingTrack:
          if (music != null)
            music.Dispose();

          music = new OpenALSoundEffect(musicList[currentMusic], ref alengine, true);
          trackTotalTime = music.TotalTime;
          music.Play(volume);
          music.Pitch = pitch;

          currentState = PlayerState.Playing;
          lastState = PlayerState.ChangingTrack;
          break;

        case PlayerState.StopPlayback:
          if (music != null)
            music.Dispose();

          currentState = PlayerState.Stopped;
          lastState = PlayerState.StopPlayback;
          break;
      }
      this.TimerEnable();
    }
    #endregion

    #region Enumerations
    public enum PlayerState : byte
    {
      Stopped,
      StartPlayback,
      Playing,
      Paused,
      ChangingTrack,
      Pausing,
      Unpausing,
      StopPlayback
    };

    public enum repeatType : byte
    {
      No,
      Song,
      All
    };
    #endregion
  }
}
