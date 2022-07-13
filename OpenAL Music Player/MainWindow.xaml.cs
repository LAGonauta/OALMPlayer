using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Controls;
using System.Linq;

using CSCore;
using CSCore.Codecs;
using TagLib;
using OpenTK.Audio;
using OpenTK.Audio.OpenAL;
using System.Reflection;

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
    public IList<string> AllPlaybackDevices;
    #endregion Fields

    #region Variables
    // Files in the directory
    static List<string> filePaths = new List<string>();

    // Generate playlist
    public ObservableCollection<playlistItemsList> items = new ObservableCollection<playlistItemsList>();

    // Multithreaded delegated callback, so our gui is not stuck while playing sound
    public delegate void UpdateDeviceListCallBack();

    // OpenAL controls
    OpenALPlayer oalPlayer;
    Thread openal_thread;

    // CPU usage
    PerformanceCounter theCPUCounter = new PerformanceCounter("Process", "% Processor Time", Process.GetCurrentProcess().ProcessName);
    int CPU_logic_processors = Environment.ProcessorCount;
    float total_cpu_usage;

    // Info text
    System.Windows.Forms.Timer InfoText;

    NotifyIcon ni;

    #endregion
    public class playlistItemsList
    {
      public string Number { get; set; }
      public string Title { get; set; }
      public string Performer { get; set; }
      public string Album { get; set; }
      public string FileName { get; set; }
    }

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

      ni.DoubleClick +=
        delegate (object sender, EventArgs e)
        {
          this.Show();
          this.WindowState = WindowState.Normal;
        };

      // Set GUI
      if (Properties.Settings.Default.LastPlaylist != null)
      {
        foreach (var path in Properties.Settings.Default.LastPlaylist)
        {
          if (System.IO.File.Exists(path))
          {
            filePaths.Add(path);
          }
        }
        playListGen(); 
      }

      speed_slider.Value = Properties.Settings.Default.Speed;
      volume_slider.Value = Properties.Settings.Default.Volume;
      thread_rate_slider.Value = Properties.Settings.Default.UpdateRate;
      switch ((OpenALPlayer.repeatType)Properties.Settings.Default.Repeat)
      {
        case OpenALPlayer.repeatType.All:
          radioRepeatAll.IsChecked = true;
          break;
        case OpenALPlayer.repeatType.Song:
          radioRepeatSong.IsChecked = true;
          break;
        case OpenALPlayer.repeatType.No:
          radioRepeatNone.IsChecked = true;
          break;
      }

      // vorbis support
      CodecFactory.Instance.Register("ogg-vorbis", new CodecFactoryEntry(s => new NVorbisSource(s).ToWaveSource(), ".ogg"));

      playlistItems.ItemsSource = items;

      // Starting audio thread
      openal_thread = new Thread(() => OpenALThread());
      openal_thread.Start();

      // Starting CPU usage timer
      total_cpu_usage = theCPUCounter.NextValue();
      this.UpdateCPUUsage(null, null);
      var CPUTimer = new System.Windows.Forms.Timer();
      CPUTimer.Interval = 2000;
      CPUTimer.Tick += new EventHandler(this.UpdateCPUUsage);
      CPUTimer.Start();

      // Starting GUI information update timer
      this.UpdateCPUUsage(null, null);
      InfoText = new System.Windows.Forms.Timer();
      InfoText.Interval = 150;
      InfoText.Tick += new EventHandler(this.UpdateInfoText);
      InfoText.Start();
    }

    public void playListGen()
    {
      for (int i = items.Count - 1; i >= 0; --i)
      {
        items.RemoveAt(i);
      }

      for (int i = 0; i < filePaths.Count; ++i)
      {
        TagLib.File file = TagLib.File.Create(filePaths[i]);
        items.Add(new playlistItemsList() { Number = (i + 1).ToString(), Title = file.Tag.Title, Performer = file.Tag.FirstPerformer,
                                            Album = file.Tag.Album, FileName = (Path.GetFileName(filePaths[i])) });
      }

      if (oalPlayer != null)
      {
        oalPlayer.MusicList = filePaths; 
      }
    }

    public void OpenALThread()
    {
      AllPlaybackDevices = AudioContext.AvailableDevices;
      DeviceChoice.Dispatcher.Invoke(new UpdateDeviceListCallBack(this.UpdateDeviceList));

      // The player is generated on the selection changed handler
      //oalPlayer = new OpenALPlayer(filePaths, last_selected_device);
    }

    #region GUI stuff
    private void Open_Click(object sender, RoutedEventArgs e)
    {
      using (FolderBrowserDialog dlgOpen = new FolderBrowserDialog())
      {
        dlgOpen.Description = "Escolha a pasta para tocar";

        System.Windows.Forms.DialogResult result = dlgOpen.ShowDialog();

        if (result == System.Windows.Forms.DialogResult.OK)
        {
          Thread.Sleep(250); // So we are sure that notthing bad happens...
          var allowedExtensions = new[] { ".mp3", ".wav", ".wma", ".ogg", ".flac", ".mp4", ".m4a", ".ac3" };
          filePaths.Clear();
          foreach (var path in Directory.GetFiles(dlgOpen.SelectedPath).Where(file => allowedExtensions.Any(file.ToLower().EndsWith)))
          {
            filePaths.Add(path);
          }
          playListGen();
        }
      }
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
      if (filePaths != null)
      {
        if (filePaths.Count > 0)
        {
          if (oalPlayer.Status == OpenALPlayer.PlayerState.Paused)
          {
            oalPlayer.Unpause();
            SoundPlayPause.Content = "Pause";
          }
          else
          {
            if (oalPlayer.Status == OpenALPlayer.PlayerState.Playing)
            {
              oalPlayer.Pause();
              SoundPlayPause.Content = "Play";
            }
            else
            {
              oalPlayer.Play();
              SoundPlayPause.Content = "Pause";
            }
          }

          playlistItems.SelectedIndex = oalPlayer.CurrentMusic - 1;
        }
      }
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
      if (filePaths != null)
      {
        if (filePaths.Count > 0)
        {
          SoundPlayPause.Content = "Pause";
          oalPlayer.NextTrack();
          playlistItems.SelectedIndex = oalPlayer.CurrentMusic - 1;
        }
      }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
      SoundPlayPause.Content = "Play";
      oalPlayer.Stop();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
      if (filePaths != null)
      {
        if (filePaths.Count > 0)
        {
          SoundPlayPause.Content = "Pause";
          oalPlayer.PreviousTrack();
          playlistItems.SelectedIndex = oalPlayer.CurrentMusic - 1;
        }
      }
    }

    private void AboutItem_Click(object sender, RoutedEventArgs e)
    {
      var about_window = new AboutWindow { Owner = this };
      about_window.ShowDialog();
    }

    private void playlistItem_MouseDoubleClick(object sender, RoutedEventArgs e)
    {
      if (playlistItems.SelectedIndex != -1)
      {
        SoundPlayPause.Content = "Pause";
        // is SelectedIndex zero indexed? Yes.
        oalPlayer.CurrentMusic = playlistItems.SelectedIndex + 1;
      }
    }

    private void Slider_VolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
      if (oalPlayer != null)
      {
        oalPlayer.Volume = (float)e.NewValue;
      }

      if (volume_text_display != null)
      {
        volume_text_display.Text = e.NewValue.ToString("0") + "%";
      }      
    }

    public void Slider_SpeedChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
      if (oalPlayer != null)
      {
        oalPlayer.Pitch = (float)e.NewValue;
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
      if (oalPlayer != null)
      {
        TimeSpan current_time = TimeSpan.FromSeconds(oalPlayer.TrackCurrentTime);
        TimeSpan total_time = TimeSpan.FromSeconds(oalPlayer.TrackTotalTime);

        if (oalPlayer.Status != OpenALPlayer.PlayerState.Stopped)
        {
          if (oalPlayer.TrackTotalTime > 0)
          {
            if (audio_position_slider.Value != oalPlayer.TrackCurrentTime / oalPlayer.TrackTotalTime)
            {
              audio_position_slider.Value = oalPlayer.TrackCurrentTime / oalPlayer.TrackTotalTime;
            }
          }

          if (current_music_text_display.Text != oalPlayer.CurrentMusic.ToString())
          {
            current_music_text_display.Text = oalPlayer.CurrentMusic.ToString();
          }

          string pos_text = ((int)current_time.TotalMinutes).ToString() + ":" + current_time.Seconds.ToString("00") +
                            " / " +
                            ((int)total_time.TotalMinutes).ToString() + ":" + total_time.Seconds.ToString("00");

          if (position_text_display.Text != pos_text)
          {
            position_text_display.Text = pos_text;
          }

          if (playlistItems.SelectedIndex != oalPlayer.CurrentMusic - 1)
          {
            if (!playlistItems.IsMouseOver)
            {
              playlistItems.SelectedIndex = oalPlayer.CurrentMusic - 1;
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

        if (oalPlayer.XRamTotal > 0)
        {
          if (xram_text_display.Text != (oalPlayer.XRamFree / (1024.0 * 1024)).ToString("0.00") + "MB")
          {
            xram_text_display.Text = (oalPlayer.XRamFree / (1024.0 * 1024)).ToString("0.00") + "MB"; 
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

    private void UpdateDeviceList()
    {
      if (AllPlaybackDevices.Contains(Properties.Settings.Default.Device))
      {
        DeviceChoice.Items.Add(Properties.Settings.Default.Device);

        foreach (string s in AllPlaybackDevices)
        {
          if (s != Properties.Settings.Default.Device)
          {
            DeviceChoice.Items.Add(s);
          }
        }
      }
      else
      {
        foreach (string s in AllPlaybackDevices)
        {
          DeviceChoice.Items.Add(s);
        }
      }

      if (DeviceChoice.Items.Count > 0)
      {
        DeviceChoice.SelectedIndex = 0;
      }
    }

    private void DeviceChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      if (oalPlayer != null)
      {
        oalPlayer.Dispose();
      }
      oalPlayer = new OpenALPlayer(filePaths, (string)DeviceChoice.SelectedValue);

      // Load settings after changing player
      if (radioRepeatAll.IsChecked == true)
      {
        oalPlayer.RepeatSetting = OpenALPlayer.repeatType.All;
      }
      else if (radioRepeatSong.IsChecked == true)
      {
        oalPlayer.RepeatSetting = OpenALPlayer.repeatType.Song;
      }
      else if (radioRepeatNone.IsChecked == true)
      {
        oalPlayer.RepeatSetting = OpenALPlayer.repeatType.No;
      }

      oalPlayer.Volume = (float)volume_slider.Value;
      oalPlayer.Pitch = (float)speed_slider.Value;
      oalPlayer.UpdateRate = (uint)thread_rate_slider.Value;
      oalPlayer.MusicList = filePaths;
    }

    private void Window_Closing(object sender, EventArgs e)
    {
      Properties.Settings.Default.Device = (string)DeviceChoice.SelectedValue;
      Properties.Settings.Default.Repeat = (byte)oalPlayer.RepeatSetting;
      Properties.Settings.Default.Volume = volume_slider.Value;
      Properties.Settings.Default.Speed = speed_slider.Value;
      Properties.Settings.Default.UpdateRate = (uint)thread_rate_slider.Value;
      Properties.Settings.Default.LastPlaylist = new System.Collections.Specialized.StringCollection();
      Properties.Settings.Default.LastPlaylist.AddRange(filePaths.ToArray());
      Properties.Settings.Default.Save();

      if (oalPlayer != null)
        oalPlayer.Dispose();

      ni.Visible = false;
    }
    #endregion

    //        var waveBuffer = new WaveBuffer(buffer);

    //        // Convert to 16-bit
    //        int read = waveBuffer.FloatBuffer.Length;
    //        short[] sampleBufferShort = new short[waveBuffer.FloatBuffer.Length / 4];

    //        for (uint i = 0; i < read / 4; ++i)
    //        {
    //            waveBuffer.FloatBuffer[i] = waveBuffer.FloatBuffer[i] * (1 << 15);

    //            if (waveBuffer.FloatBuffer[i] > 32767)
    //                sampleBufferShort[i] = 32767;
    //            else if (waveBuffer.FloatBuffer[i] < -32768)
    //                sampleBufferShort[i] = -32768;
    //            else
    //                sampleBufferShort[i] = (short)(waveBuffer.FloatBuffer[i]);
    //        }

    //        return sampleBufferShort.SelectMany(x => BitConverter.GetBytes(x)).ToArray();

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

    private int SetEffect(EffectsExtension EFX, int source, int slot, int effect, bool filtered, int filter)
    {
      // Dummy
      return 0;
    }

    private int RemoveEffect(EffectsExtension EFX, int source, int slot, int effect, int filter)
    {
      // Dummy
      return 0;
    }

    private void ThreadTimeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
      if (oalPlayer != null)
        oalPlayer.UpdateRate = (uint)e.NewValue;

      if (InfoText != null)
        InfoText.Interval = (int)e.NewValue;
    }

    private void repeatRadioButtons_checked(object sender, RoutedEventArgs e)
    {
      if (oalPlayer != null)
      {
        if (sender == radioRepeatAll)
        {
          oalPlayer.RepeatSetting = OpenALPlayer.repeatType.All;
        }
        else if (sender == radioRepeatSong)
        {
          oalPlayer.RepeatSetting = OpenALPlayer.repeatType.Song;
        }
        else if (sender == radioRepeatNone)
        {
          oalPlayer.RepeatSetting = OpenALPlayer.repeatType.No;
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