using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Controls;
using System.Linq;

using CSCore;
using CSCore.Codecs;
using System.Reflection;
using System.Threading.Tasks;
using OpenALMusicPlayer.AudioPlayer;

// TODO:
// Make it OO
// Use state machine
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
    static List<string> filePaths = new List<string>();

    // Generate playlist
    public ObservableCollection<PlaylistItemsList> items = new ObservableCollection<PlaylistItemsList>();

    // Multithreaded delegated callback, so our gui is not stuck while playing sound
    public delegate void UpdateDeviceListCallBack();

    // OpenAL controls
    private MusicPlayer player;

    // CPU usage
    PerformanceCounter theCPUCounter = new PerformanceCounter("Process", "% Processor Time", Process.GetCurrentProcess().ProcessName);
    int CPU_logic_processors = Environment.ProcessorCount;
    float total_cpu_usage;

    // Info text
    System.Windows.Forms.Timer InfoText;

    NotifyIcon ni;

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
      total_cpu_usage = theCPUCounter.NextValue();
      this.UpdateCPUUsage(null, null);
      var CPUTimer = new Timer();
      CPUTimer.Interval = 2000;
      CPUTimer.Tick += new EventHandler(this.UpdateCPUUsage);
      CPUTimer.Start();

      // Starting GUI information update timer
      this.UpdateCPUUsage(null, null);
      InfoText = new Timer();
      InfoText.Interval = 150;
      InfoText.Tick += new EventHandler(this.UpdateInfoText);
      InfoText.Start();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
      var devices = await AudioEngine.AudioPlayer.AvailableDevices();
      UpdateDeviceList(devices);
      if (Properties.Settings.Default.LastPlaylist != null)
      {
        foreach (var path in Properties.Settings.Default.LastPlaylist)
        {
          if (File.Exists(path))
          {
            filePaths.Add(path);
          }
        }
        await GeneratePlaylist();
      }
    }

    public async Task GeneratePlaylist()
    {
      items.Clear();
      var playlistItems = await Task.Run(() =>
      {
        return filePaths
          .Select((path, index) =>
          {
            TagLib.File file = TagLib.File.Create(path);
            return new PlaylistItemsList()
            {
              Number = (index + 1).ToString(),
              Title = file.Tag.Title,
              Performer = file.Tag.FirstPerformer,
              Album = file.Tag.Album,
              FileName = Path.GetFileName(path)
            };
          })
          .ToList();
      });

      playlistItems
          .ForEach(item => items.Add(item));

      if (player != null)
      {
        player.MusicList = filePaths; 
      }
    }

    #region GUI stuff
    private async void Open_Click(object sender, RoutedEventArgs e)
    {
      using (var dlgOpen = new FolderBrowserDialog())
      {
        dlgOpen.Description = "Escolha a pasta para tocar";

        var result = dlgOpen.ShowDialog();

        if (result == System.Windows.Forms.DialogResult.OK)
        {
          var allowedExtensions = new[] { ".mp3", ".wav", ".wma", ".ogg", ".flac", ".mp4", ".m4a", ".ac3" };
          filePaths.Clear();
          foreach (var path in Directory.GetFiles(dlgOpen.SelectedPath).Where(file => allowedExtensions.Any(file.ToLower().EndsWith)))
          {
            filePaths.Add(path);
          }
          await GeneratePlaylist();
        }
      }
    }

    private async void Play_Click(object sender, RoutedEventArgs e)
    {
      if (filePaths != null)
      {
        if (filePaths.Count > 0)
        {
          playlistItems.SelectedIndex = player.CurrentMusic - 1;
          if (player.Status == PlayerState.Paused)
          {
            player.Unpause();
            SoundPlayPause.Content = "Pause";
          }
          else
          {
            if (player.Status == PlayerState.Playing)
            {
              SoundPlayPause.Content = "Play";
              player.Pause();
            }
            else
            {
              SoundPlayPause.Content = "Pause";
              await player.Play();
            }
          }
        }
      }
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
      if (filePaths != null)
      {
        if (filePaths.Count > 0)
        {
          SoundPlayPause.Content = "Pause";
          await player.NextTrack();
          playlistItems.SelectedIndex = player.CurrentMusic - 1;
        }
      }
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
      SoundPlayPause.Content = "Play";
      await player.Stop();
    }

    private async void Back_Click(object sender, RoutedEventArgs e)
    {
      if (filePaths != null)
      {
        if (filePaths.Count > 0)
        {
          SoundPlayPause.Content = "Pause";
          await player.PreviousTrack();
          playlistItems.SelectedIndex = player.CurrentMusic - 1;
        }
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
        // is SelectedIndex zero indexed? Yes.
        var status = player.Status;
        await player.SetCurrentMusic(playlistItems.SelectedIndex + 1);
        if (status == PlayerState.Stopped)
        {
          Play_Click(sender, e);
        }
      }
    }

    private void Slider_VolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
      if (player != null)
      {
        player.Volume = (float)e.NewValue;
      }

      if (volume_text_display != null)
      {
        volume_text_display.Text = e.NewValue.ToString("0") + "%";
      }      
    }

    public void Slider_SpeedChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
      if (player != null)
      {
        player.Pitch = (float)e.NewValue;
      }

      if (speed_text_display != null)
      {
        speed_text_display.Text = (e.NewValue * 100).ToString("0") + "%";
      }
    }

    private void UpdateCPUUsage(object source, EventArgs e)
    {
      total_cpu_usage = theCPUCounter.NextValue();
      CPUUsagePercent.Text = (total_cpu_usage / CPU_logic_processors).ToString("0.0") + "%";
    }

    private void UpdateInfoText(object source, EventArgs e)
    {
      // Updating the values only when they change. Is this faster or slower than just updating them?
      if (player != null)
      {
        TimeSpan current_time = TimeSpan.FromSeconds(player.TrackCurrentTime);
        TimeSpan total_time = TimeSpan.FromSeconds(player.TrackTotalTime);

        if (player.Status != PlayerState.Stopped)
        {
          if (player.TrackTotalTime > 0)
          {
            if (audio_position_slider.Value != player.TrackCurrentTime / player.TrackTotalTime)
            {
              audio_position_slider.Value = player.TrackCurrentTime / player.TrackTotalTime;
            }
          }

          if (current_music_text_display.Text != player.CurrentMusic.ToString())
          {
            current_music_text_display.Text = player.CurrentMusic.ToString();
          }

          var pos_text = $"{(int)current_time.TotalMinutes}:{current_time.Seconds:00} / {(int)total_time.TotalMinutes}:{total_time.Seconds:00}";
          if (position_text_display.Text != pos_text)
          {
            position_text_display.Text = pos_text;
          }

          if (playlistItems.SelectedIndex != player.CurrentMusic - 1)
          {
            if (!playlistItems.IsMouseOver)
            {
              playlistItems.SelectedIndex = player.CurrentMusic - 1;
            } 
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

        if (player.XRamTotal > 0)
        {
          if (xram_text_display.Text != (player.XRamFree / (1024.0 * 1024)).ToString("0.00") + "MB")
          {
            xram_text_display.Text = (player.XRamFree / (1024.0 * 1024)).ToString("0.00") + "MB"; 
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
      if (player != null)
      {
        player.Dispose();
      }
      player = new MusicPlayer(filePaths, (string)DeviceChoice.SelectedValue);

      // Load settings after changing player
      if (radioRepeatAll.IsChecked == true)
      {
        player.RepeatSetting = RepeatType.All;
      }
      else if (radioRepeatSong.IsChecked == true)
      {
        player.RepeatSetting = RepeatType.Song;
      }
      else if (radioRepeatNone.IsChecked == true)
      {
        player.RepeatSetting = RepeatType.No;
      }

      player.Volume = (float)volume_slider.Value;
      player.Pitch = (float)speed_slider.Value;
      //player.UpdateRate = (uint)thread_rate_slider.Value; TODO: remove this slider
      player.MusicList = filePaths;
    }

    private void Window_Closing(object sender, EventArgs e)
    {
      Properties.Settings.Default.Device = (string)DeviceChoice.SelectedValue;
      Properties.Settings.Default.Repeat = (byte)player.RepeatSetting;
      Properties.Settings.Default.Volume = volume_slider.Value;
      Properties.Settings.Default.Speed = speed_slider.Value;
      Properties.Settings.Default.UpdateRate = (uint)thread_rate_slider.Value;
      Properties.Settings.Default.LastPlaylist = new System.Collections.Specialized.StringCollection();
      Properties.Settings.Default.LastPlaylist.AddRange(filePaths.ToArray());
      Properties.Settings.Default.Save();

      if (player != null)
        player.Dispose();

      ni.Visible = false;
    }
    #endregion

    public static int[] PitchCorrection(float rate)
    {
      float pitch_correction_cents_total = 1200 * (float)(Math.Log(1 / rate) / Math.Log(2));
      int pitch_correction_semitones;
      float pitch_correction_cents;

      if (pitch_correction_cents_total > 1250)
      {
        pitch_correction_semitones = 12;
        pitch_correction_cents = 50;
      }
      else if (pitch_correction_cents_total < -1250)
      {
        pitch_correction_semitones = -12;
        pitch_correction_cents = -50;
      }
      else
      {
        pitch_correction_semitones = (int)(pitch_correction_cents_total / 100); // Truncate
        pitch_correction_cents = pitch_correction_cents_total - pitch_correction_semitones * 100;

        if (pitch_correction_cents > 50)
        {
          pitch_correction_semitones = pitch_correction_semitones + 1;
          pitch_correction_cents = pitch_correction_cents - 100;
        }
        else if (pitch_correction_cents < -50)
        {
          pitch_correction_semitones = pitch_correction_semitones - 1;
          pitch_correction_cents = pitch_correction_cents + 100;
        }
      }

      int[] pitch_out = new int[] { pitch_correction_semitones, (int)Math.Round(pitch_correction_cents) };
      return pitch_out;
    }

    private void pitchShiftCheckbox_Checked(object sender, RoutedEventArgs e)
    {
      DebugTrace("EFX not implemented yet");
    }

    private void ThreadTimeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
      if (player != null)
        //player.UpdateRate = (uint)e.NewValue; TODO: remove this slider

      if (InfoText != null)
        InfoText.Interval = (int)e.NewValue;
    }

    private void repeatRadioButtons_checked(object sender, RoutedEventArgs e)
    {
      if (player != null)
      {
        if (sender == radioRepeatAll)
        {
          player.RepeatSetting = RepeatType.All;
        }
        else if (sender == radioRepeatSong)
        {
          player.RepeatSetting = RepeatType.Song;
        }
        else if (sender == radioRepeatNone)
        {
          player.RepeatSetting = RepeatType.No;
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
