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

// TODO:
// Make it OO
// Use state machine
// Use INotifyPropertyChanged 

namespace OpenAL_Music_Player
{
  /// <summary>
  /// Interaction logic for MainWindow.xaml
  /// </summary>
  public partial class MainWindow : Window
  {
    #region Fields
    public IList<string> AllPlaybackDevices;
    public string DefaultPlaybackDevice;
    #endregion Fields

    #region Variables
    // Files in the directory
    static string[] filePaths;

    // Device selection initialization
    string last_selected_device = "";
    string config_file = "oalminerva.ini";

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

    #endregion
    public class playlistItemsList
    {
      public string Number { get; set; }
      public string Title { get; set; }
      public string Album { get; set; }
      public string FileName { get; set; }
    }

    public MainWindow()
    {
      InitializeComponent();

      // vorbis support
      CodecFactory.Instance.Register("ogg-vorbis", new CodecFactoryEntry(s => new NVorbisSource(s).ToWaveSource(), ".ogg"));

      playlistItems.ItemsSource = items;

      // Load last selected device from file, if not found use default
      if (System.IO.File.Exists(config_file))
      {
        last_selected_device = System.IO.File.ReadAllText(config_file);
        DebugTrace("The device that will be used is " + last_selected_device);
      }
      else
      {
        last_selected_device = null;
        DebugTrace("Using default device");
      }

      // Starting audio thread
      openal_thread = new Thread(() => OpenALThread());
      openal_thread.Start();

      // Starting CPU usage timer
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

      for (int i = 0; i < filePaths.Length; ++i)
      {
        TagLib.File file = TagLib.File.Create(filePaths[i]);
        items.Add(new playlistItemsList() { Number = (i + 1).ToString(), Title = file.Tag.Title, Album = file.Tag.Album,
                                            FileName = (Path.GetFileName(filePaths[i])) });
      }

      List<string> mList = new List<string>();

      foreach (string element in filePaths)
        mList.Add(element);

      oalPlayer.MusicList = mList;
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
          filePaths = Directory.GetFiles(dlgOpen.SelectedPath).Where(file => allowedExtensions.Any(file.ToLower().EndsWith)).ToArray();
          playListGen();
        }
      }
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
      if (filePaths != null)
      {
        if (filePaths.Length > 0)
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
        if (filePaths.Length > 0)
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
        if (filePaths.Length > 0)
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
        volume_text_display.Text = e.NewValue.ToString("0") + "%";
      }
    }

    public void Slider_SpeedChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
      if (oalPlayer != null)
      {
        oalPlayer.Pitch = (float)e.NewValue;
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
      if (oalPlayer != null)
      {
        TimeSpan current_time = TimeSpan.FromSeconds(oalPlayer.TrackCurrentTime);
        TimeSpan total_time = TimeSpan.FromSeconds(oalPlayer.TrackTotalTime);

        if (oalPlayer.Status != OpenALPlayer.PlayerState.Stopped)
        {
          if (oalPlayer.TrackTotalTime > 0)
          {
            audio_position_slider.Value = oalPlayer.TrackCurrentTime / oalPlayer.TrackTotalTime;
          }          
          current_music_text_display.Text = oalPlayer.CurrentMusic.ToString();
          position_text_display.Text = ((int)current_time.TotalMinutes).ToString() + ":" + current_time.Seconds.ToString("00") +
                                       " / " +
                                       ((int)total_time.TotalMinutes).ToString() + ":" + total_time.Seconds.ToString("00");
        }
        else
        {
          audio_position_slider.Value = 0;
          current_music_text_display.Text = "-";
          position_text_display.Text = "0:00 / 0:00";
        }

        if (oalPlayer.XRamTotal > 0)
        {
          xram_text_display.Text = (oalPlayer.XRamFree / (1024.0 * 1024)).ToString("0.00") + "MB / " + oalPlayer.XRamTotal.ToString("0.00") + " MB";
        }
      }
    }

    private void UpdateDeviceList()
    {
      if (last_selected_device != null && AllPlaybackDevices.Contains(last_selected_device))
      {
        DeviceChoice.Items.Add(last_selected_device);

        foreach (string s in AllPlaybackDevices)
        {
          if (s != last_selected_device)
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
      System.IO.File.WriteAllText(config_file, (string)DeviceChoice.SelectedValue);
      if (oalPlayer != null)
      {
        oalPlayer.Dispose();
      }
      oalPlayer = new OpenALPlayer(filePaths, (string)DeviceChoice.SelectedValue);
    }

    private void Window_Closing(object sender, EventArgs e)
    {
      if (oalPlayer != null)
        oalPlayer.Dispose();
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
        oalPlayer.UpdateRate = (int)e.NewValue;

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

    private void DebugTrace(string message)
    {
#if DEBUG
      Trace.WriteLine(message);
#endif
    }
  }
}