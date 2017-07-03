using CSCore;
using CSCore.Codecs;
using CSCore.Streams.SampleConverter;
using OpenTK.Audio;
using OpenTK.Audio.OpenAL;
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

    // Generate playlist
    public ObservableCollection<playlistItemsList> items = new ObservableCollection<playlistItemsList>();

    public bool xram_available = false;

    // change this to use new system
    bool useObjectOrientedMethod = true;

    // File control
    public static int file_number = 0;

    // Multithreaded delegated callback, so our gui is not stuck while playing sound
    public delegate void UpdateTextCallback(string message);
    public delegate void UpdateDeviceListCallBack();
    public delegate void UpdateinfoTextCallback(string message);

    // Device selection initialization
    string last_selected_device = "";
    string config_file = "oalminerva.ini";
    bool first_selection = true;

    // Multithreaded controls
    public static bool playbackthread_enabled = true;
    public static bool is_playing = false;
    public static int update_time_ms = 150;
    OpenALPlayer oalPlayer;
    Thread openal_thread;

    // CPU usage
    PerformanceCounter theCPUCounter = new PerformanceCounter("Process", "% Processor Time", Process.GetCurrentProcess().ProcessName);
    int CPU_logic_processors = Environment.ProcessorCount;
    float total_cpu_usage;

    // Info text
    string message;
    System.Windows.Forms.Timer InfoText;

    // Variable used when verifying if user closed the window (to clean up) 
    public byte p = 0;
    public byte c = 0;

    #endregion
    public class playlistItemsList
    {
      public string Title { get; set; }
    }

    public MainWindow()
    {
      InitializeComponent();

      // vorbis support
      CodecFactory.Instance.Register("ogg-vorbis", new CodecFactoryEntry(s => new NVorbisSource(s).ToWaveSource(), ".ogg"));

      playlistItems.ItemsSource = items;

      // Load last selected device from file, if not found use default
      if (File.Exists(config_file))
      {
        last_selected_device = File.ReadAllText(config_file);
        DebugTrace("The device that will be used is " + last_selected_device);
      }
      else
      {
        last_selected_device = null;
        DebugTrace("Using default device");
      }

      // Starting audio thread
      if (useObjectOrientedMethod)
      {
        openal_thread = new Thread(new ThreadStart(OpenALThread2));
      }
      else
      {
        openal_thread = new Thread(new ThreadStart(OpenALThread));
      }
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
      for (int i = items.Count - 1; i >= 0; i--)
      {
        items.RemoveAt(i);
      }

      for (int i = 0; i < filePaths.Length; i++)
      {
        items.Add(new playlistItemsList() { Title = (i + 1 + ". " + Path.GetFileName(filePaths[i])) });
      }

      if (useObjectOrientedMethod)
      {
        List<string> mList = new List<string>();

        foreach (string element in filePaths)
          mList.Add(element);

        oalPlayer.MusicList = mList;
      }

    }

    #region OpenAL Stuff
    public static bool IsXFi = false;
    public static bool float_support = false;
    public static bool pause_change = false;
    public static bool paused = false;
    public static bool oalthread_enabled = true;
    public static bool start_playback = false;
    public static bool stop_playback = false;
    public static bool change_file = false;
    public static float volume = 1;
    public static double double_volume = 1;
    public static float playback_speed = 1f;
    public static bool mudando_velocidade = false;
    public static bool mudando_volume = false;
    public static bool effects_enabled = false;
    public static bool pitch_shift_enabled = false;

    public void OpenALThread2()
    {
      AllPlaybackDevices = AudioContext.AvailableDevices;
      DeviceChoice.Dispatcher.Invoke(new UpdateDeviceListCallBack(this.UpdateDeviceList));

      oalPlayer = new OpenALPlayer(filePaths, last_selected_device);
    }

    public void OpenALThread()
    {
      //Register the new codec.
      CodecFactory.Instance.Register("ogg-vorbis", new CodecFactoryEntry(s => new NVorbisSource(s).ToWaveSource(), ".ogg"));

      OpenTK.Audio.OpenAL.ALError oal_error;
      string information_text;
      float music_current_time = 0;

      oal_error = AL.GetError();
      if (oal_error != ALError.NoError)
      {
        DebugTrace("Error starting oal error (yeah)" + oal_error);
      }

      AllPlaybackDevices = AudioContext.AvailableDevices;
      DeviceChoice.Dispatcher.Invoke(new UpdateDeviceListCallBack(this.UpdateDeviceList));

      // Setting up OpenAL stuff
      DebugTrace("Setting up OpenAL playback");

      AudioContext ac = new AudioContext(last_selected_device, 48000, 0, false, true, AudioContext.MaxAuxiliarySends.One);
      XRamExtension XRam = new XRamExtension();

      if (AL.Get(ALGetString.Renderer).IndexOf("X-Fi") != -1)
        IsXFi = true;

      if (AL.IsExtensionPresent("AL_EXT_float32"))
        float_support = true;

      DebugTrace("Renderer: " + AL.Get(ALGetString.Renderer));

      // EFX
      var EFX = new EffectsExtension();
      int effect = EFX.GenEffect();
      int filter = EFX.GenFilter();
      int slot = EFX.GenAuxiliaryEffectSlot();

      oal_error = AL.GetError();
      if (oal_error != ALError.NoError)
      {
        DebugTrace("Error generating effects: " + oal_error);
      }

      if (IsXFi)
      {
        // Is XRam available?
        if (XRam.GetRamSize > 0) { xram_available = true; }

        oal_error = AL.GetError();
        if (oal_error != ALError.NoError)
        {
          DebugTrace("XRam not available: " + oal_error);
        }
      }

      // Setting up buffers
      DebugTrace("Setting up buffers");
      int buffer = 0;
      int source = 0;

      // Need to change to last used effect, or null effect.
      if (IsXFi)
      {
        // Generating effects
        EFX.BindEffect(effect, EfxEffectType.PitchShifter);

        // Generating filter
        EFX.Filter(filter, EfxFilteri.FilterType, (int)EfxFilterType.Lowpass);

        oal_error = AL.GetError();
        if (oal_error != ALError.NoError)
        {
          DebugTrace("Failed when generating effects: " + oal_error);
        }
      }

      // Default speed
      int[] pitch_correction = PitchCorrection(playback_speed);

      #region Playback
      while (playbackthread_enabled)
      {
        Thread.Sleep(250);
        while (is_playing)
        {
          #region Buffer
          // Generating sources
          DebugTrace("Generating source");
          source = AL.GenSource();

          oal_error = AL.GetError();
          if (oal_error != ALError.NoError)
          {
            DebugTrace("Failed to generate source: " + oal_error);
          }

          // Setting up buffers
          DebugTrace("Setting up buffer");
          buffer = AL.GenBuffer();
          oal_error = AL.GetError();
          if (oal_error != ALError.NoError)
          {
            DebugTrace("Failed to generate buffer: " + oal_error);
          }

          TimeSpan total_time = ExtensionMethods.ToTimeSpan(0); // Some files never send stop commands for some reason, let's do it manually. (only needed on driver 2.40+)
          DebugTrace("Carregando...");

          IWaveSource AudioFile;

          try
          {
            AudioFile = CodecFactory.Instance.GetCodec(filePaths[file_number]);
          }
          catch (Exception)
          {
            DebugTrace("No file to load.");
            break;
          }

          if (IsXFi && AudioFile.WaveFormat.WaveFormatTag == AudioEncoding.IeeeFloat)
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

          total_time = new TimeSpan(0, 0, (int)(AudioFile.Length * sizeof(byte) / AudioFile.WaveFormat.BytesPerSecond));

          ALFormat sound_format;
          try
          {
            sound_format = GetSoundFormat(AudioFile.WaveFormat.Channels, AudioFile.WaveFormat.BitsPerSample, float_support);
          }
          catch
          {
            DebugTrace("Invalid file format.");
            break;
          }

          byte[] sound_data = new byte[AudioFile.Length];
          try
          {
            AudioFile.Read(sound_data, 0, sound_data.Length);
          }
          catch
          {
            DebugTrace("Unable to read file.");
            break;
          }

          AudioFile.Dispose();

          AL.BufferData(buffer, sound_format, sound_data, sound_data.Length, AudioFile.WaveFormat.SampleRate);
          sound_data = null;

          oal_error = AL.GetError();
          if (oal_error != ALError.NoError)
          {
            DebugTrace("Buffering error: " + oal_error);
          }
          #endregion

          DebugTrace("Setting source: ");

          AL.Source(source, ALSourcei.Buffer, buffer);

          oal_error = AL.GetError();
          if (oal_error != ALError.NoError)
          {
            DebugTrace("Source binding error: " + oal_error);
          }

          if (IsXFi)
          {
            // Binding effects
            EFX.BindSourceToAuxiliarySlot(source, slot, 0, 0);
            DebugTrace("Binding effect");
          }

          // Correcting gain to match last played file
          AL.Source(source, ALSourcef.Gain, volume);

          // Correcting effect and filter to match last played file
          if (Math.Truncate(playback_speed * 100) == 100)
          {
            if (IsXFi)
            {
              // Reset effect
              EFX.Effect(effect, EfxEffecti.PitchShifterCoarseTune, 0);
              EFX.Effect(effect, EfxEffecti.PitchShifterFineTune, 0);

              if (!pitch_shift_enabled)
              {
                // Disable filter
                EFX.Filter(filter, EfxFilterf.LowpassGain, 1f);
                EFX.BindFilterToSource(source, filter);

                // Disable effect
                EFX.AuxiliaryEffectSlot(slot, EfxAuxiliaryi.EffectslotEffect, (int)EfxEffectType.Null);
              }
            }

            // Changing pitch
            AL.Source(source, ALSourcef.Pitch, 1f);
          }
          else
          {
            if (IsXFi && pitch_shift_enabled)
            {
              // Changing effect
              EFX.Effect(effect, EfxEffecti.PitchShifterCoarseTune, pitch_correction[0]);
              EFX.Effect(effect, EfxEffecti.PitchShifterFineTune, pitch_correction[1]);
              EFX.AuxiliaryEffectSlot(slot, EfxAuxiliaryi.EffectslotEffect, effect);

              // Changing filter
              EFX.Filter(filter, EfxFilterf.LowpassGain, 0f); // To disable direct sound and leave only the effect
              EFX.BindFilterToSource(source, filter);
            }

            // Change source speed
            AL.Source(source, ALSourcef.Pitch, playback_speed);
          }

          AL.SourcePlay(source);

          oal_error = AL.GetError();
          if (oal_error != ALError.NoError)
          {
            DebugTrace("Unable to play source: " + oal_error);
            break;
          }

          decimal total_time_seconds = ExtensionMethods.ToDecimal(total_time);

          #region Playback
          while (AL.GetSourceState(source) == ALSourceState.Playing || AL.GetSourceState(source) == ALSourceState.Paused) // We want to wait until application exit
          {
            Thread.Sleep(update_time_ms);

            if (pause_change)
            {
              if (paused)
              {
                AL.SourcePause(source);
                pause_change = false;
              }
              else
              {
                AL.SourcePlay(source);
                pause_change = false;
              }
            }

            if (mudando_velocidade)
            {
              if (Math.Truncate(playback_speed * 100) == 100)
              {
                if (IsXFi)
                {
                  // Reset effect
                  EFX.Effect(effect, EfxEffecti.PitchShifterCoarseTune, 0);
                  EFX.Effect(effect, EfxEffecti.PitchShifterFineTune, 0);

                  if (!pitch_shift_enabled)
                  {
                    // Disable filter
                    EFX.Filter(filter, EfxFilterf.LowpassGain, 1f);
                    EFX.BindFilterToSource(source, filter);

                    // Disable effect
                    EFX.AuxiliaryEffectSlot(slot, EfxAuxiliaryi.EffectslotEffect, (int)EfxEffectType.Null);
                  }
                }

                // Changing pitch
                AL.Source(source, ALSourcef.Pitch, 1f);
              }
              else
              {
                if (IsXFi)
                {
                  if (pitch_shift_enabled)
                  {
                    // Changing effect
                    pitch_correction = PitchCorrection(playback_speed);
                    EFX.Effect(effect, EfxEffecti.PitchShifterCoarseTune, pitch_correction[0]);
                    EFX.Effect(effect, EfxEffecti.PitchShifterFineTune, pitch_correction[1]);
                    EFX.AuxiliaryEffectSlot(slot, EfxAuxiliaryi.EffectslotEffect, effect);

                    // Changing filter
                    EFX.Filter(filter, EfxFilterf.LowpassGain, 0f); // To disable direct sound and leave only the effect
                    EFX.BindFilterToSource(source, filter);
                  }
                  else
                  {
                    // Disable filter
                    EFX.Filter(filter, EfxFilterf.LowpassGain, 1f);
                    EFX.BindFilterToSource(source, filter);

                    // Disable effect
                    EFX.AuxiliaryEffectSlot(slot, EfxAuxiliaryi.EffectslotEffect, (int)EfxEffectType.Null);
                  }
                }

                // Change source speed
                AL.Source(source, ALSourcef.Pitch, playback_speed);
              }

              mudando_velocidade = false;
            }

            // Change volume
            if (mudando_volume)
            {
              AL.Source(source, ALSourcef.Gain, volume);
              mudando_volume = false;
            }

            if (change_file)
            {
              break;
            }

            AL.GetSource(source, ALSourcef.SecOffset, out music_current_time);
            // Needed on newer X-Fi drivers. I could also change the source to streaming, but with this we can be sure.
            if ((int)music_current_time > total_time_seconds && IsXFi)
            {
              break;
            }

            information_text = ("Música atual: " + (file_number + 1)) + Environment.NewLine +
                ("Posição: " + (int)music_current_time + "s/" + total_time_seconds + "s") + Environment.NewLine +
                ("Volume: " + (int)(double_volume) + "%") + Environment.NewLine +
                ("Velocidade: " + (int)(playback_speed * 100) + "%");

            if (xram_available)
            {
              information_text = information_text + Environment.NewLine + ("XRam livre: " + (XRam.GetRamFree / (1024.0 * 1024)).ToString("0.00") + "MB");
            }

            infoText.Dispatcher.Invoke(new UpdateinfoTextCallback(this.UpdateinfoText), new object[] { information_text });
          }
          #endregion

          music_current_time = 0;

          DebugTrace("Stopping source");

          AL.SourceStop(source);

          oal_error = AL.GetError();
          if (oal_error != ALError.NoError)
          {
            DebugTrace("Unable to stop source: " + oal_error);
            break;
          }

          // Deleting source and buffer
          AL.DeleteSource(source);

          oal_error = AL.GetError();
          if (oal_error != ALError.NoError)
          {
            DebugTrace("Unable to delete source: " + oal_error);
          }

          AL.DeleteBuffer(buffer);

          oal_error = AL.GetError();
          if (oal_error != ALError.NoError)
          {
            DebugTrace("Unable to delete buffer: " + oal_error);
          }

          if (file_number == (filePaths.Length - 1) && !change_file)
          {
            // Restart playback.
            file_number = 0;
          }
          else if (change_file)
          {
            change_file = false;
            break;
          }
          else
          {
            file_number++;
          }
        }
      }
      #endregion

      EFX.DeleteAuxiliaryEffectSlot(slot);
      EFX.DeleteEffect(effect);
      EFX.DeleteFilter(filter);
      AL.DeleteSource(source);
      AL.DeleteBuffer(buffer);
      ac.Dispose(); // Cleaning context

      DebugTrace("Disposing context");

      return;
    }
    #endregion

    #region GUI stuff
    private void Open_Click(object sender, RoutedEventArgs e)
    {
      using (FolderBrowserDialog dlgOpen = new FolderBrowserDialog())
      {
        dlgOpen.Description = "Escolha a pasta para tocar";

        System.Windows.Forms.DialogResult result = dlgOpen.ShowDialog();

        if (result == System.Windows.Forms.DialogResult.OK)
        {
          if (is_playing)
          {
            stop_playback = true;
          }
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
          if (useObjectOrientedMethod)
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
          else
          {
            if (paused && is_playing)
            {
              paused = false;
              pause_change = true;
            }
            else if (!is_playing)
            {
              is_playing = true;
            }
            else
            {
              paused = true;
              pause_change = true;
            }

            if (paused)
            {
              SoundPlayPause.Content = "Play";
            }
            else
            {
              SoundPlayPause.Content = "Pause";
            }

            playlistItems.SelectedIndex = file_number;
          }
        }
      }
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
      if (filePaths != null)
      {
        if (filePaths.Length > 0)
        {
          if (useObjectOrientedMethod)
          {
            SoundPlayPause.Content = "Pause";
            oalPlayer.NextTrack();
            playlistItems.SelectedIndex = oalPlayer.CurrentMusic - 1;
          }
          else
          {
            file_number++;

            if (file_number == filePaths.Length) // When it reaches the end of the list.
              file_number = 0;

            playlistItems.SelectedIndex = file_number;

            change_file = true;
          }
        }
      }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
      if (useObjectOrientedMethod)
      {
        SoundPlayPause.Content = "Play";
        oalPlayer.Stop();
      }
      else
      {
        file_number = 0;
        is_playing = false;
        stop_playback = true;
        change_file = true;
      }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
      if (filePaths != null)
      {
        if (filePaths.Length > 0)
        {
          if (useObjectOrientedMethod)
          {
            SoundPlayPause.Content = "Pause";
            oalPlayer.PreviousTrack();
            playlistItems.SelectedIndex = oalPlayer.CurrentMusic - 1;
          }
          else
          {
            if (file_number > 0)
            {
              file_number--;
            }
            else
            {
              file_number = filePaths.Length - 1;
            }

            playlistItems.SelectedIndex = file_number;
            change_file = true;
          }
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
      if (useObjectOrientedMethod)
      {
        if (playlistItems.SelectedIndex != -1)
        {
          SoundPlayPause.Content = "Pause";
          // is SelectedIndex zero indexed? Yes.
          oalPlayer.CurrentMusic = playlistItems.SelectedIndex + 1;
        }
      }
      else
      {
        if (file_number != playlistItems.SelectedIndex)
        {
          file_number = playlistItems.SelectedIndex;
          change_file = true;

          if (!is_playing)
          {
            is_playing = true;
          }

          if (paused)
          {
            SoundPlayPause.Content = "Pause";
            paused = false;
            pause_change = true;
          }
        }
        else
        {
          if (!is_playing)
          {
            change_file = true;
            is_playing = true;
          }
        }
      }
    }

    private void Slider_VolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
      if (useObjectOrientedMethod)
      {
        if (oalPlayer != null)
          oalPlayer.Volume = (float)e.NewValue;
      }
      else
      {
        double_volume = e.NewValue; // Linear volume scale
        volume = 0.0031623f * (float)Math.Exp(double_volume / 100 * 5.757f); // Exp volume scale
        mudando_volume = true;
      }
    }

    public void Slider_SpeedChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
      if (useObjectOrientedMethod)
      {
        if (oalPlayer != null)
          oalPlayer.Pitch = (float)e.NewValue / 100;
      }
      else
      {
        playback_speed = (float)e.NewValue / 100;
        mudando_velocidade = true;
      }
    }

    private void UpdateCPUUsage(object source, EventArgs e)
    {
      total_cpu_usage = theCPUCounter.NextValue();
      CPUUsagePercent.Text = (total_cpu_usage / CPU_logic_processors).ToString("0.0") + "%";
    }

    private void UpdateinfoText(string message)
    {
      infoText.Text = (message);
    }

    private void UpdateInfoText(object source, EventArgs e)
    {
      if (oalPlayer != null)
      {
        message = ("Música atual: " + (oalPlayer.CurrentMusic)) + Environment.NewLine +
            ("Posição: " + (int)oalPlayer.TrackCurrentTime + "s/" + (int)oalPlayer.TrackTotalTime + "s") + Environment.NewLine +
            ("Volume: " + (int)(oalPlayer.Volume) + "%") + Environment.NewLine +
            ("Velocidade: " + (int)(oalPlayer.Pitch * 100) + "%");

        if (oalPlayer.XRamTotal > 0)
        {
          message = message + Environment.NewLine + ("XRam livre: " + (oalPlayer.XRamFree / (1024.0 * 1024)).ToString("0.00") + "MB");
        }

        infoText.Text = (message);
      }
    }

    private void UpdateDeviceList()
    {
      if (last_selected_device != null)
      {
        DeviceChoice.Items.Add(last_selected_device + " (Current)");
      }

      {
        foreach (string s in AllPlaybackDevices)
          DeviceChoice.Items.Add(s);
      }
    }

    private void DeviceChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      if (first_selection)
      {
        first_selection = false;
      }
      else
      {
        if (File.Exists(config_file))
        {
          if (DeviceChoice.SelectedIndex != 1)
          {
            File.WriteAllText(config_file, (string)DeviceChoice.SelectedValue);
            System.Windows.MessageBox.Show("Please restart to load the new device", "Restart required");
          }
          else
          {
            System.Windows.MessageBox.Show("Please select other device", "Invalid option");
          }
        }
        else
        {
          File.WriteAllText(config_file, (string)DeviceChoice.SelectedValue);
          System.Windows.MessageBox.Show("Please restart to load the new device", "Restart required");
        }
      }
    }

    private void Window_Closing(object sender, EventArgs e)
    {
      if (useObjectOrientedMethod)
      {
        if (oalPlayer != null)
          oalPlayer.Dispose();
      }
      else
      {
        stop_playback = true;
        playbackthread_enabled = false;
        is_playing = false;
        change_file = true;
      }
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

    public static ALFormat GetSoundFormat(int channels, int bits, bool float_support)
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
              if (float_support)
              {
                return ALFormat.MonoFloat32Ext;
              }
              else
              {
                throw new NotSupportedException("The specified sound format is not supported.");
                //return 0x1203;
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
              if (float_support)
              {
                return ALFormat.StereoFloat32Ext;
              }
              else
              {
                // Undocumented value
                return (ALFormat)0x1203;
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
      if (useObjectOrientedMethod)
      {
        DebugTrace("EFX not implemented yet");
      }
      else
      {
        if (pitchShiftCheckbox.IsChecked.Value)
        {
          pitch_shift_enabled = true;
          mudando_velocidade = true;
        }
        else
        {
          pitch_shift_enabled = false;
          mudando_velocidade = true;
        }
      }
    }

    private int SetEffect(EffectsExtension EFX, int source, int slot, int effect, bool filtered, int filter)
    {
      // Filtering
      if (filtered)
      {
        EFX.Filter(filter, EfxFilterf.LowpassGain, 0f); // Allow only the effect to play
        EFX.BindFilterToSource(source, filter);
      }
      else
      {
        EFX.Filter(filter, EfxFilterf.LowpassGain, 1f);
        EFX.BindFilterToSource(source, filter);
      }

      EFX.AuxiliaryEffectSlot(slot, EfxAuxiliaryi.EffectslotEffect, effect);

      return 0;
    }

    private int RemoveEffect(EffectsExtension EFX, int source, int slot, int effect, int filter)
    {
      EFX.Filter(filter, EfxFilterf.LowpassGain, 1f);
      EFX.BindFilterToSource(source, filter);

      EFX.Effect(effect, EfxEffecti.PitchShifterCoarseTune, 0);
      EFX.Effect(effect, EfxEffecti.PitchShifterFineTune, 0);
      EFX.AuxiliaryEffectSlot(slot, EfxAuxiliaryi.EffectslotEffect, (int)EfxEffectType.Null);

      return 0;
    }

    private void ThreadTimeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
      if (useObjectOrientedMethod)
      {
        if (oalPlayer != null)
          oalPlayer.UpdateRate = (int)e.NewValue;

        if (InfoText != null)
          InfoText.Interval = (int)e.NewValue;
      }
      else
      {
        update_time_ms = (int)e.NewValue;
      }
    }

    private void DebugTrace(string message)
    {
#if DEBUG
            Trace.WriteLine(message); 
#endif
    }
  }

  // From http://www.codeproject.com/Questions/147596/TimeSpan-to-integer
  public static class ExtensionMethods
  {
    public static decimal ToDecimal(this TimeSpan span)
    {
      decimal spanSecs = (span.Hours * 3600) + (span.Minutes * 60) + span.Seconds;
      decimal result = spanSecs;
      return result;
    }

    public static TimeSpan ToTimeSpan(this decimal value)
    {
      int days = Convert.ToInt32(Math.Ceiling(value));
      value -= days;
      int time = Convert.ToInt32(value * 86400M);
      TimeSpan result = new TimeSpan(1, 0, 0, time, 0);
      return result;
    }
  }
}