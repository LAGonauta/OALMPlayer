﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenALMusicPlayer.AudioPlayer
{
  class MusicPlayer : IDisposable
  {

    #region Fields
    private readonly Action trackUpdateCallback;

    private AudioEngine.AudioPlayer audioPlayer;
    private PlayerState currentState = PlayerState.Stopped;
    private int currentMusic = 0;
    private double trackTotalTime = 0;
    private double trackCurrentTime = 0;
    private float volumePercentage = 100;
    private float volume = 1f;
    private float pitch = 1f;
    private bool disposedValue;
    #endregion

    #region Properties
    public RepeatType RepeatSetting { get; set; } = RepeatType.All;

    public List<string> MusicList { get; set; }

    /// <summary>
    /// Starts at 1 to the user, but internally starts at zero.
    /// </summary>
    public int CurrentMusic
    {
      get => currentMusic + 1;
      set
      {
        currentMusic = value - 1;
        currentState = PlayerState.ChangingTrack;
        audioPlayer.Stop();
        trackUpdateCallback();
      }
    }

    /// <summary>
    /// Volume, from 0 to 100.
    /// </summary>
    public float Volume
    {
      get => volumePercentage;
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
      get => pitch;
      set => audioPlayer.Pitch = pitch = value;
    }

    public PlayerState Status => currentState;

    public double TrackCurrentTime => trackCurrentTime;

    public double TrackTotalTime => trackTotalTime;

    public bool IsXFi => audioPlayer.IsXFi;

    public float XRamFree => audioPlayer.GetFreeXRam;

    public float XRamTotal => audioPlayer.GetSizeXRam;
    #endregion

    #region Constructor
    public MusicPlayer(string device, List<string> filePaths, Action trackUpdateCallback)
    {
      audioPlayer = new AudioEngine.AudioPlayer(device);
      MusicList = filePaths;
      this.trackUpdateCallback = trackUpdateCallback;
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
          MusicList[currentMusic],
          (current, total) =>
          {
            trackTotalTime = total;
            trackCurrentTime = current;
          });
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
        currentMusic = (currentMusic + 1) % MusicList.Count;
        currentState = PlayerState.ChangingTrack;
      }
      else
      {
        if (RepeatSetting == RepeatType.All)
        {
          currentMusic = (currentMusic + 1) % MusicList.Count;
          currentState = PlayerState.ChangingTrack;
        }
        else if (RepeatSetting == RepeatType.Song)
        {
          currentState = PlayerState.ChangingTrack;
        }
        else if (RepeatSetting == RepeatType.No)
        {
          if (currentMusic + 1 == MusicList.Count)
          {
            currentState = PlayerState.Stopped;
          }
          else
          {
            currentMusic = (currentMusic + 1) % MusicList.Count;
            currentState = PlayerState.ChangingTrack;
          }
        }
      }
      CurrentMusic = currentMusic + 1;
      var player = audioPlayer;
      player?.Stop();
    }

    public void PreviousTrack()
    {
      if (currentMusic == 0)
      {
        currentMusic = MusicList.Count - 1;
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
