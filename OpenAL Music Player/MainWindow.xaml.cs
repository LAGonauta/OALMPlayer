using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Linq;

using CSCore;
using CSCore.Codecs;
using System.Reflection;
using System.Threading.Tasks;
using System.Threading;
using OpenALMusicPlayer.AudioPlayer;
using System.Windows.Input;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;
using System.Windows.Threading;

// TODO:
// Use INotifyPropertyChanged 

namespace OpenALMusicPlayer
{
  /// <summary>
  /// Interaction logic for MainWindow.xaml
  /// </summary>
  public partial class MainWindow : Window
  {
    #region Fields
    public IEnumerable<string> AllPlaybackDevices;
    #endregion Fields

    #region Variables
    // Files in the directory
    static List<string> filePaths = new();

    // Generate playlist
    public readonly ObservableCollection<PlaylistItemsList> items = new();

    // OpenAL controls
    private MusicPlayer musicPlayer;

    // CPU usage
    private readonly PerformanceCounter theCPUCounter = new PerformanceCounter("Process", "% Processor Time", Process.GetCurrentProcess().ProcessName);

    // Info text
    private readonly Timer infoTextTimer;
    private readonly Timer cpuTimer;

    NotifyIcon ni;

    private bool positionMoving = false;

    #endregion
    public MainWindow()
    {
      InitializeComponent();

      var icon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetEntryAssembly().ManifestModule.Name);

      ni = new NotifyIcon()
      {
        Visible = true,
        Text = Title,
        Icon = icon
      };

      ni.DoubleClick += (sender, e) =>
      {
        this.Show();
        this.Activate();
        this.WindowState = WindowState.Normal;
      };

      // Set GUI
      playlistItems.ItemsSource = items;

      speed_slider.Value = Properties.Settings.Default.Speed;
      volume_slider.Value = Properties.Settings.Default.Volume;
      thread_rate_slider.Value = Properties.Settings.Default.UpdateRate;
      switch ((RepeatType)Properties.Settings.Default.Repeat)
      {
        case RepeatType.All:
          radioRepeatAll.IsChecked = true;
          break;
        case RepeatType.Song:
          radioRepeatSong.IsChecked = true;
          break;
        case RepeatType.No:
          radioRepeatNone.IsChecked = true;
          break;
      }

      // vorbis support
      CodecFactory.Instance.Register("ogg-vorbis", new CodecFactoryEntry(s => new NVorbisSource(s).ToWaveSource(), ".ogg"));

      // Starting CPU usage timer
      cpuTimer = new Timer();
      cpuTimer.Interval = 2000;
      cpuTimer.Tick += new EventHandler(this.UpdateCPUUsage);
      cpuTimer.Start();

      // Starting GUI information update timer
      infoTextTimer = new Timer();
      infoTextTimer.Interval = 1000;
      infoTextTimer.Tick += new EventHandler(this.UpdateInfoText);
      infoTextTimer.Start();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
      var devices = await AudioEngine.AudioPlayer.AvailableDevices();
      UpdateDeviceList(devices);
      if (Properties.Settings.Default.LastPlaylist != null)
      {
        var newPaths = await Task.Run(
          Properties.Settings.Default.LastPlaylist
          .Cast<string>()
          .Where(File.Exists)
          .ToList);
        filePaths = newPaths;
        await GeneratePlaylist(newPaths);
      }
    }

    public async Task GeneratePlaylist(List<string> filePaths)
    {
      var playlistItems = await Task.Run(
        filePaths
        .Select(filePath =>
        {
          try
          {
            var file = TagLib.File.Create(filePath);
            return new PlaylistItemsList()
            {
              Title = file.Tag.Title,
              Performer = file.Tag.FirstPerformer,
              Album = file.Tag.Album,
              DiscNumber = file.Tag.Disc,
              FileName = Path.GetFileName(filePath),
              TrackNumber = file.Tag.Track
            };
          }
          catch (TagLib.UnsupportedFormatException ex)
          {
            Trace.WriteLine($"Format not supported: {ex.Message}");
            return new PlaylistItemsList()
            {
              FileName = Path.GetFileName(filePath)
            };
          }
        })
        .OrderBy(item => item.DiscNumber)
        .ThenBy(item => item.Album)
        .ThenBy(item => item.TrackNumber)
        .ThenBy(item => item.FileName)
        .Select((item, index) => {
          item.Number = (index + 1).ToString();
          return item;
        })
        .ToList);

      items.Clear();
      playlistItems.ForEach(item => items.Add(item));

      if (musicPlayer != null)
      {
        musicPlayer.MusicList = filePaths; 
      }
    }

    #region GUI stuff
    private async void Open_Click(object sender, RoutedEventArgs e)
    {
      using var dlgOpen = new FolderBrowserDialog();
      dlgOpen.Description = "Escolha a pasta para tocar";

      var result = dlgOpen.ShowDialog();
      if (result == System.Windows.Forms.DialogResult.OK)
      {
        var allowedExtensions = CodecFactory.Instance.GetSupportedFileExtensions();
        filePaths = Directory.GetFiles(dlgOpen.SelectedPath)
          .Where(file => allowedExtensions.Any(file.ToLowerInvariant().EndsWith))
          .ToList();
        await GeneratePlaylist(filePaths);
      }
    }

    private async void Play_Click(object sender, RoutedEventArgs e)
    {
      if (filePaths != null && filePaths.Count > 0)
      {
        playlistItems.SelectedIndex = musicPlayer.CurrentMusic - 1;
        if (musicPlayer.Status == PlayerState.Paused)
        {
          musicPlayer.Unpause();
          SoundPlayPause.Content = "Pause";
        }
        else
        {
          if (musicPlayer.Status == PlayerState.Playing)
          {
            SoundPlayPause.Content = "Play";
            musicPlayer.Pause();
          }
          else
          {
            SoundPlayPause.Content = "Pause";
            await musicPlayer.Play(0);
          }
        }
      }
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
      if (filePaths != null && filePaths.Count > 0)
      {
        SoundPlayPause.Content = "Pause";
        musicPlayer.NextTrack(false);
        playlistItems.SelectedIndex = musicPlayer.CurrentMusic - 1;
        await musicPlayer.Play(0);
      }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
      SoundPlayPause.Content = "Play";
      musicPlayer.Stop();
    }

    private async void Back_Click(object sender, RoutedEventArgs e)
    {
      if (filePaths != null && filePaths.Count > 0)
      {
        SoundPlayPause.Content = "Pause";
        musicPlayer.PreviousTrack();
        playlistItems.SelectedIndex = musicPlayer.CurrentMusic - 1;
        await musicPlayer.Play(0);
      }
    }

    private void AboutItem_Click(object sender, RoutedEventArgs e)
    {
      var about_window = new AboutWindow { Owner = this };
      about_window.ShowDialog();
    }

    private async void PlaylistItem_MouseDoubleClick(object sender, RoutedEventArgs e)
    {
      if (playlistItems.SelectedIndex != -1)
      {
        SoundPlayPause.Content = "Pause";
        // SelectedIndex is zero indexed
        musicPlayer.CurrentMusic = playlistItems.SelectedIndex + 1;
        await musicPlayer.Play(0);
      }
    }

    private void Slider_VolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
      if (musicPlayer != null)
      {
        musicPlayer.Volume = (float)e.NewValue;
      }

      if (volume_text_display != null)
      {
        volume_text_display.Text = $"{e.NewValue:0}%";
      }      
    }

    private async void Slider_PositionDrag(object sender, System.Windows.Input.MouseEventArgs e)
    {
      if (e.LeftButton == MouseButtonState.Pressed)
      {
        positionMoving = true;
      }
      else
      {
        if (sender is Slider slider)
        {
          var position = slider.Value;
          positionMoving = false;
          if (musicPlayer.Status == PlayerState.Playing)
          {
            await musicPlayer.Play(position);
          }
        }
        else
        {
          positionMoving = false;
        }
      }
    }

    public void Slider_PitchChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
      if (musicPlayer != null)
      {
        musicPlayer.Pitch = (float)e.NewValue;
      }

      if (speed_text_display != null)
      {
        speed_text_display.Text = $"{e.NewValue * 100:0}%";
      }
    }

    private void UpdateCPUUsage(object _, EventArgs __)
    {
      CPUUsagePercent.Text = $"{theCPUCounter.NextValue() / Environment.ProcessorCount:0.0}%";
    }

    private void UpdateInfoText(object _, EventArgs __)
    {
      // Updating the values only when they change. Is this faster or slower than just updating them?
      if (musicPlayer != null)
      {
        if (musicPlayer.XRamTotal > 0)
        {
          var val = $"{musicPlayer.XRamFree / (1024.0 * 1024):0.00}MB";
          if (xram_text_display.Text != val)
          {
            xram_text_display.Text = val;
          }
        }
        else
        {
          if (xram_text_display.Text != "-")
          {
            xram_text_display.Text = "-";
          }
        }
      }
    }

    private void UpdateDeviceList(IEnumerable<string> devices)
    {
      if (devices.Contains(Properties.Settings.Default.Device))
      {
        DeviceChoice.Items.Add(Properties.Settings.Default.Device);

        foreach (var device in devices)
        {
          if (device != Properties.Settings.Default.Device)
          {
            DeviceChoice.Items.Add(device);
          }
        }
      }
      else
      {
        foreach (string device in devices)
        {
          DeviceChoice.Items.Add(device);
        }
      }

      if (DeviceChoice.Items.Count > 0)
      {
        DeviceChoice.SelectedIndex = 0;
      }
    }

    private void DeviceChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      musicPlayer?.Dispose();

      musicPlayer = new MusicPlayer((string)DeviceChoice.SelectedValue, filePaths, UpdateTrackNumber, UpdateTrackPosition);

      // Load settings after changing player
      if (radioRepeatAll.IsChecked == true)
      {
        musicPlayer.RepeatSetting = RepeatType.All;
      }
      else if (radioRepeatSong.IsChecked == true)
      {
        musicPlayer.RepeatSetting = RepeatType.Song;
      }
      else if (radioRepeatNone.IsChecked == true)
      {
        musicPlayer.RepeatSetting = RepeatType.No;
      }

      musicPlayer.Volume = (float)volume_slider.Value;
      musicPlayer.Pitch = (float)speed_slider.Value;
      //player.UpdateRate = (uint)thread_rate_slider.Value; TODO: remove this slider
      musicPlayer.MusicList = filePaths;
    }
    
    private async Task UpdateTrackNumber(CancellationToken token)
    {
      try
      {
        await Dispatcher.InvokeAsync(() =>
        {
          var currentMusic = musicPlayer.CurrentMusic.ToString();
          var currentText = current_music_text_display.Text;
          if (currentText != currentMusic)
          {
            current_music_text_display.Text = currentMusic;
          }
        }, DispatcherPriority.DataBind, token);
      }
      catch (TaskCanceledException)
      {
        Trace.WriteLine("Task cancelled updating track number");
      }
    }

    private async Task UpdateTrackPosition(double currentTime, double totalTime, CancellationToken token)
    {
      try
      {
        await Dispatcher.InvokeAsync(() =>
        {
          var ct = TimeSpan.FromSeconds(currentTime);
          var tt = TimeSpan.FromSeconds(totalTime);

          if (musicPlayer.Status != PlayerState.Stopped)
          {
            if (!positionMoving && musicPlayer.TrackTotalTime > 0)
            {
              var ratio = musicPlayer.TrackCurrentTime / musicPlayer.TrackTotalTime;
              if (audio_position_slider.Value != ratio)
              {
                audio_position_slider.Value = ratio;
              }
            }

            var pos_text = $"{(int)ct.TotalMinutes}:{ct.Seconds:00} / {(int)tt.TotalMinutes}:{tt.Seconds:00}";
            if (position_text_display.Text != pos_text)
            {
              position_text_display.Text = pos_text;
            }

            if (playlistItems.SelectedIndex != musicPlayer.CurrentMusic - 1 && !playlistItems.IsMouseOver)
            {
              playlistItems.SelectedIndex = musicPlayer.CurrentMusic - 1;
            }
          }
          else
          {
            if (audio_position_slider.Value != 0)
            {
              audio_position_slider.Value = 0;
            }

            if (current_music_text_display.Text != "-")
            {
              current_music_text_display.Text = "-";
            }

            if (position_text_display.Text != "0:00 / 0:00")
            {
              position_text_display.Text = "0:00 / 0:00";
            }
          }
        }, DispatcherPriority.DataBind, token);
      }
      catch (TaskCanceledException)
      {
        Trace.WriteLine("Task cancelled updating track position");
      }
    }

    private void Window_Closing(object sender, EventArgs e)
    {
      Properties.Settings.Default.Device = (string)DeviceChoice.SelectedValue;
      Properties.Settings.Default.Repeat = (byte)musicPlayer.RepeatSetting;
      Properties.Settings.Default.Volume = volume_slider.Value;
      Properties.Settings.Default.Speed = speed_slider.Value;
      Properties.Settings.Default.UpdateRate = (uint)thread_rate_slider.Value;
      Properties.Settings.Default.LastPlaylist = new();
      Properties.Settings.Default.LastPlaylist.AddRange(filePaths.ToArray());
      Properties.Settings.Default.Save();

      musicPlayer?.Dispose();
      cpuTimer.Stop();
      cpuTimer.Dispose();
      infoTextTimer.Stop();
      infoTextTimer.Dispose();

      ni.Visible = false;
    }
    #endregion

    public static (int semitones, int cents) PitchCorrection(float rate)
    {
      float pitchCorrectionCentsTotal = 1200 * (float)(Math.Log(1 / rate) / Math.Log(2));
      int pitchCorrectionSemitones;
      float pitchCorretionCents;

      if (pitchCorrectionCentsTotal > 1250)
      {
        pitchCorrectionSemitones = 12;
        pitchCorretionCents = 50;
      }
      else if (pitchCorrectionCentsTotal < -1250)
      {
        pitchCorrectionSemitones = -12;
        pitchCorretionCents = -50;
      }
      else
      {
        pitchCorrectionSemitones = (int)(pitchCorrectionCentsTotal / 100); // Truncate
        pitchCorretionCents = pitchCorrectionCentsTotal - pitchCorrectionSemitones * 100;

        if (pitchCorretionCents > 50)
        {
          pitchCorrectionSemitones = pitchCorrectionSemitones + 1;
          pitchCorretionCents = pitchCorretionCents - 100;
        }
        else if (pitchCorretionCents < -50)
        {
          pitchCorrectionSemitones = pitchCorrectionSemitones - 1;
          pitchCorretionCents = pitchCorretionCents + 100;
        }
      }

      return (pitchCorrectionSemitones, (int)Math.Round(pitchCorretionCents));
    }

    private void pitchShiftCheckbox_Checked(object sender, RoutedEventArgs e)
    {
      DebugTrace("EFX not implemented yet");
    }

    private void ThreadTimeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
    }

    private void repeatRadioButtons_checked(object sender, RoutedEventArgs e)
    {
      if (musicPlayer != null)
      {
        if (sender == radioRepeatAll)
        {
          musicPlayer.RepeatSetting = RepeatType.All;
        }
        else if (sender == radioRepeatSong)
        {
          musicPlayer.RepeatSetting = RepeatType.Song;
        }
        else if (sender == radioRepeatNone)
        {
          musicPlayer.RepeatSetting = RepeatType.No;
        }
      }
    }

    protected override void OnStateChanged(EventArgs e)
    {
      if (WindowState == WindowState.Minimized)
      {
        this.Hide();
      }

      base.OnStateChanged(e);
    }

    private void DebugTrace(string message)
    {
#if DEBUG
      Trace.WriteLine(message);
#endif
    }
  }
}
