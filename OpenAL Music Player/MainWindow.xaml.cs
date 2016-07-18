using NAudio.Wave;
using NAudio.Flac;
using NAudio.WindowsMediaFormat;
using NAudio.Vorbis;
using OpenTK;
using OpenTK.Audio;
using OpenTK.Audio.OpenAL;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Timers;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Controls;
using System.Linq;

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
        static string[] filePaths;// = Directory.GetFiles(@System.IO.Path.Combine("Music"));

        // Generate playlist
        public ObservableCollection<playlistItemsList> items = new ObservableCollection<playlistItemsList>();

        public bool xram_available = false;

        // File control
        public static int file_number = 0;

        // Multithreaded delegated callback, so our gui is not stuck while playing sound
        public delegate void UpdateTextCallback(string message);
        public delegate void UpdateDeviceListCallBack();
        public delegate void UpdateinfoTextCallback(string message);
        public delegate void UpdateCPUUsageTextCallback(string message);

        // Device selection initialization
        string last_selected_device = "";
        string config_file = "oalminerva.ini";
        bool first_selection = true;

        // Multithreaded controls
        public static bool playbackthread_enabled = true;
        public static bool is_playing = false;

        // CPU usage
        private static PerformanceCounter theCPUCounter = new PerformanceCounter("Process", "% Processor Time", Process.GetCurrentProcess().ProcessName);
        private static int CPU_logic_processors = Environment.ProcessorCount;

        // OpenGL
        double pos_x = 0;
        double pos_y = 0;
        double pos_z = 0;
        double rotation_degree = 0;

        // Screen update rate timer
        public static System.Timers.Timer glinvalidate_timer = new System.Timers.Timer();

        // Multisource positioning OAL -> OGL communication variables
        Vector3 red_position = new Vector3(0, 0, 0);
        Vector3 blue_position = new Vector3(0, 0, 0);
        Vector3 green_position = new Vector3(0, 0, 0);
        Vector3 pink_position = new Vector3(0, 0, 0);

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

            // Set playlist
            //ObservableCollection<playlistItemsList> items = new ObservableCollection<playlistItemsList>();
            playlistItems.ItemsSource = items;

            //for (int i = 0; i < filePaths.Length; i++)
            //{
            //    items.Add(new playlistItemsList() { Title = (i + 1 + ". " + filePaths[i]) });
            //}

            //for (int i = items.Count - 1; i >= 0; i--)
            //{
            //    items.RemoveAt(i);
            //}

            // Load last selected device from file, if not found use default
            if (File.Exists(config_file))
            {
                last_selected_device = File.ReadAllText(config_file);
                Trace.WriteLine("The device that will be used is " + last_selected_device);
            }
            else
            {
                last_selected_device = null;
                Trace.WriteLine("Using default device");
            }

            // Starting test thread
            Thread openal_thread = new Thread(new ThreadStart(OpenALThread));
            openal_thread.Start();
                
            // Starting CPU usage thread
            new Thread(new ThreadStart(UpdateCpuUsagePercent)).Start();
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
        }

        #region OpenAL Stuff
        public static bool IsXFi = false;
        public static bool pause_change = false;
        public static bool paused = false;
        public static bool oalthread_enabled = true;
        public static bool start_playback = false;
        public static bool stop_playback = false;
        public static bool goto_next = false;
        public static bool goto_next_from_playlist = false;
        public static bool goback = false;
        public static bool effects_enabled = true;
        public static float volume = 1;
        public static double double_volume = 1;
        public static float playback_speed = 1f;
        public static int int_playback_speed = 10;
        public static bool mudando_velocidade = false;
        public static bool mudando_volume = false;

        // Create timer to use when playing music
        public static System.Timers.Timer music_timer = new System.Timers.Timer();
        public static int music_current_time;

        public void OpenALThread()
        {
            OpenTK.Audio.OpenAL.ALError oal_error;
            string information_text;

            oal_error = AL.GetError();
            if (oal_error != ALError.NoError)
            {
                Trace.WriteLine("Error starting oal error (yeah)" + oal_error);
            }

            // Set timer properties
            music_timer.Elapsed += new ElapsedEventHandler(SetMusicCurrentTime);
            music_timer.Interval = 1000;

            AllPlaybackDevices = AudioContext.AvailableDevices;
            DeviceChoice.Dispatcher.Invoke(new UpdateDeviceListCallBack(this.UpdateDeviceList));

            // Setting up OpenAL stuff
            Trace.WriteLine("Setting up OpenAL playback");
            //AudioContext ac = new AudioContext(last_selected_device, 0, 0, false, true, AudioContext.MaxAuxiliarySends.Four);
            AudioContext ac = new AudioContext(last_selected_device, 48000);
            XRamExtension XRam = new XRamExtension();

            if (AL.Get(ALGetString.Renderer) == "SB X-Fi Audio [0001]")
            {
                IsXFi = true;
            }

            Trace.WriteLine("Renderer: " + AL.Get(ALGetString.Renderer));

            // EFX
            var EFX = new EffectsExtension();
            int effect = EFX.GenEffect();
            int filter = EFX.GenFilter();
            int slot = EFX.GenAuxiliaryEffectSlot();

            oal_error = AL.GetError();
            if (oal_error != ALError.NoError)
            {
                Trace.WriteLine("Error generating effects: " + oal_error);
            }

            if (IsXFi)
            {
                // Is XRam available?
                if (XRam.GetRamSize > 0) { xram_available = true; }

                oal_error = AL.GetError();
                if (oal_error != ALError.NoError)
                {
                    Trace.WriteLine("XRam not available: " + oal_error);
                }
            }

            // Setting up buffers
            Trace.WriteLine("Setting up buffers");
            int buffer = 0;
            int source = 0;

            if (IsXFi)
            {
                // Generating effects
                EFX.BindEffect(effect, EfxEffectType.PitchShifter);

                // Generating filter
                EFX.Filter(filter, EfxFilteri.FilterType, (int)EfxFilterType.Lowpass);

                oal_error = AL.GetError();
                if (oal_error != ALError.NoError)
                {
                    Trace.WriteLine("Failed when generating effects: " + oal_error);
                }
            }

            // Default speed
            int[] pitch_correction = PitchCorrection(playback_speed);

            #region Setting up OAL Variables
            // Listener position and direction. Remember: right-handed axis
            Vector3 ListenerDirection = new Vector3(0f, 0f, -1f);
            Vector3 ListenerDirectionUP = new Vector3(0f, 1f, 0f);
            Vector3 ListenerPosition = new Vector3(0f, 0f, 0f);
            Vector3 SourceDirection = new Vector3(0f, 0f, 1f);
            Vector3 SourcePosition = new Vector3(0f, 0f, -1f); // Default to 1m
            Vector3 SourceVelocity = new Vector3(0f, 0f, 0f); // Default to 0m/s
            Vector3 ListenerVelocity = new Vector3(0f, 0f, 0f); // Default to 0m/s
            int degrees = 0;

            // For multisource test
            Vector3 RedVelocity = new Vector3(0f, 0f, 0f);
            Vector3 BlueVelocity = new Vector3(0f, 0f, 0f);
            Vector3 GreenVelocity = new Vector3(0f, 0f, 0f);
            Vector3 PinkVelocity = new Vector3(0f, 0f, 0f);

            // Position.
            double position_z = 0;
            double position_x = 0;
            double position_y = 0;

            // Direction
            double direction_x;
            //double direction_y;
            double direction_z;

            // Parametric
            double t_parametric = 0;

            #endregion

            // Goto's are used here, sorry. I use them to cleanup OpenAL when the user closes the window and I don't want to do anything else (like play a source or print a line). Do you have a better solution? Share it, please :)

            #region Playback
            while (playbackthread_enabled)
            {
                Thread.Sleep(250);
                while (is_playing)
                {
                    #region Buffer
                    // Generating sources
                    Trace.WriteLine("Generating sources");
                    source = AL.GenSource(); // One source

                    oal_error = AL.GetError();
                    if (oal_error != ALError.NoError)
                    {
                        Trace.WriteLine("Failed to generate source: " + oal_error);
                    }

                    // Setting up buffers
                    Trace.WriteLine("Setting up buffers");
                    buffer = AL.GenBuffer(); // One buffer, the second is a dummy buffer to release memory
                    oal_error = AL.GetError();
                    if (oal_error != ALError.NoError)
                    {
                        Trace.WriteLine("Failed to generate buffer: " + oal_error);
                    }

                    TimeSpan total_time = ExtensionMethods.ToTimeSpan(0); // Some files never send stop commands for some reason, let's do it manually. (only needed on driver 2.40+)
                    Trace.WriteLine("Carregando...");

                    int channels, bits_per_sample, sample_rate;
                    byte[] sound_data; // Creating sound data array

                    FileStream AudioFile;
                    TimeSpan total_time_temp;

                    AudioFile = File.OpenRead(filePaths[file_number]);
                    if (filePaths[file_number].EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
                    {
                        sound_data = LoadMP3(AudioFile, out channels, out bits_per_sample, out sample_rate, out total_time_temp);
                        //sound_data = LoadMP3Path(filePaths[file_number], out channels, out bits_per_sample, out sample_rate, out total_time_temp);
                    }
                    else if (filePaths[file_number].EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                    {
                        sound_data = LoadWave(AudioFile, out channels, out bits_per_sample, out sample_rate, out total_time_temp);
                    }
                    else if (filePaths[file_number].EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
                    {
                        sound_data = LoadVorbis(AudioFile, out channels, out bits_per_sample, out sample_rate, out total_time_temp);
                    }
                    else if (filePaths[file_number].EndsWith(".wma", StringComparison.OrdinalIgnoreCase))
                    {
                        sound_data = LoadWMA(filePaths[file_number], out channels, out bits_per_sample, out sample_rate, out total_time_temp);
                    }
                    else if (filePaths[file_number].EndsWith(".flac", StringComparison.OrdinalIgnoreCase))
                    {
                        sound_data = LoadFLAC(AudioFile, out channels, out bits_per_sample, out sample_rate, out total_time_temp);
                    }
                    else
                    {
                        Trace.WriteLine("No file to load.");
                        break;
                    }

                    total_time = total_time_temp;
                    ALFormat soundformat = GetSoundFormat(channels, bits_per_sample);
                    AL.BufferData(buffer, soundformat, sound_data, sound_data.Length, sample_rate);

                    oal_error = AL.GetError();
                    if (oal_error != ALError.NoError)
                    {
                        Trace.WriteLine("Buffering error: " + oal_error);
                    }

                    sound_data = null;
                    AudioFile.Dispose();
                    #endregion

                    Trace.WriteLine("Setting source: ");

                    AL.Source(source, ALSourcei.Buffer, buffer);

                    oal_error = AL.GetError();
                    if (oal_error != ALError.NoError)
                    {
                        Trace.WriteLine("Source binding error: " + oal_error);
                    }

                    if (IsXFi)
                    {
                        // Binding effects
                        EFX.BindSourceToAuxiliarySlot(source, slot, 0, 0);
                        Trace.WriteLine("Binding effect");
                    }

                    // Correcting gain to match last played file
                    AL.Source(source, ALSourcef.Gain, volume);

                    // Correcting effect and filter to match last played file
                    if (int_playback_speed == 10)
                    {
                        if (IsXFi)
                        {
                            // Disabling filter
                            EFX.Filter(filter, EfxFilterf.LowpassGain, 1f);
                            EFX.BindFilterToSource(source, filter);

                            // Disabling effect
                            EFX.Effect(effect, EfxEffecti.PitchShifterCoarseTune, pitch_correction[0]);
                            EFX.Effect(effect, EfxEffecti.PitchShifterFineTune, pitch_correction[1]);
                            EFX.AuxiliaryEffectSlot(slot, EfxAuxiliaryi.EffectslotEffect, (int)EfxEffectType.Null);
                        }

                        // Changing pitch
                        AL.Source(source, ALSourcef.Pitch, playback_speed);

                        // Correcting playback timer
                        music_timer.Interval = 1000 * (double)10 / int_playback_speed;
                    }
                    else
                    {
                        if (IsXFi)
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

                        // Correcting playback timer
                        music_timer.Interval = (double)1000 * 10 / int_playback_speed;
                    }

                    music_timer.Start();
                    AL.SourcePlay(source);

                    oal_error = AL.GetError();
                    if (oal_error != ALError.NoError)
                    {
                        Trace.WriteLine("Unable to play source: " + oal_error);
                        break;
                    }

                    decimal total_time_seconds = ExtensionMethods.ToDecimal(total_time);

                    #region Playback
                    while (AL.GetSourceState(source) == ALSourceState.Playing || AL.GetSourceState(source) == ALSourceState.Paused) // We want to wait until application exit
                    {
                        Thread.Sleep(250);

                        if (pause_change)
                        {
                            if (paused)
                            {
                                AL.SourcePause(source);
                                music_timer.Stop();
                                pause_change = false;
                            }
                            else
                            {
                                AL.SourcePlay(source);
                                music_timer.Start();
                                pause_change = false;
                            }
                        }

                        if (mudando_velocidade)
                        {
                            pitch_correction = PitchCorrection(playback_speed);

                            if (int_playback_speed == 10)
                            {
                                if (IsXFi)
                                {
                                    // Disabling filter
                                    EFX.Filter(filter, EfxFilterf.LowpassGain, 1f);
                                    EFX.BindFilterToSource(source, filter);

                                    // Disabling effect
                                    EFX.Effect(effect, EfxEffecti.PitchShifterCoarseTune, pitch_correction[0]);
                                    EFX.Effect(effect, EfxEffecti.PitchShifterFineTune, pitch_correction[1]);
                                    EFX.AuxiliaryEffectSlot(slot, EfxAuxiliaryi.EffectslotEffect, (int)EfxEffectType.Null);
                                }

                                // Changing pitch
                                AL.Source(source, ALSourcef.Pitch, playback_speed);

                                // Correcting playback timer
                                music_timer.Interval = (double)1000 * 10 / int_playback_speed;
                            }
                            else
                            {
                                if (IsXFi)
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

                                // Correcting playback timer
                                music_timer.Interval = (double)1000 * 10 / int_playback_speed;
                            }

                            mudando_velocidade = false;
                        }

                        // Change volume
                        if (mudando_volume)
                        {
                            AL.Source(source, ALSourcef.Gain, volume);
                            mudando_volume = false;
                        }

                        if (stop_playback)
                        {
                            //file_number = 0; // It is better to this this at the end
                            is_playing = false;
                            stop_playback = false;
                            break;
                        }
                        else if (goto_next)
                        {
                            //file_number++; // It is better to this this at the end
                            goto_next = false;
                            break;
                        }
                        else if (goback)
                        {
                            if (file_number > 0)
                            {
                                // Just break here, we need to set this at the end before the file number add operation
                                //file_number = file_number - 1;
                                //goback = false; 
                                break;
                            }
                            else
                            {
                                goback = false;
                            }

                            //if (file_number == 0)
                            //{
                            //    goback = false;
                            //}
                        }

                        // Needed on newer drivers. Doesn't work with speed shift.
                        //if (music_current_time > total_time_seconds)
                        //{
                        //    break;
                        //}

                        information_text = ("Música atual: " + (file_number + 1)) + Environment.NewLine + ("Posição: " + music_current_time + "s/" + total_time_seconds + "s") + Environment.NewLine + ("Volume: " + (int)(double_volume * 100) + "%") + Environment.NewLine + ("Velocidade: " + int_playback_speed * 10 + "%");

                        if (xram_available)
                        {
                            information_text = information_text + Environment.NewLine + ("XRam livre: " + (XRam.GetRamFree / (1024.0 * 1024)).ToString("0.00") + "MB");
                        }

                        infoText.Dispatcher.Invoke(new UpdateinfoTextCallback(this.UpdateinfoText), new object[] { information_text });
                    }
                    #endregion

                    // Stopping Timer
                    music_timer.Stop();
                    music_current_time = 0;

                    Trace.WriteLine("Stopping source");

                    AL.SourceStop(source);

                    oal_error = AL.GetError();
                    if (oal_error != ALError.NoError)
                    {
                        Trace.WriteLine("Unable to stop source: " + oal_error);
                        break;
                    }

                    // Deleting source and buffer
                    AL.DeleteSource(source);

                    oal_error = AL.GetError();
                    if (oal_error != ALError.NoError)
                    {
                        Trace.WriteLine("Unable to delete source: " + oal_error);
                    }

                    AL.DeleteBuffer(buffer);

                    oal_error = AL.GetError();
                    if (oal_error != ALError.NoError)
                    {
                        Trace.WriteLine("Unable to delete buffer: " + oal_error);
                    }

                    if (file_number == (filePaths.Length - 1) && !goback && !goto_next_from_playlist)
                    {
                        // Restart playback, or stop playback.
                        file_number = 0; // Restarting here
                    }
                    else
                    {
                        if (!is_playing)
                        {
                            file_number = 0;
                        }
                        else if (goback)
                        {
                            file_number = file_number - 1;
                            goback = false;
                        }
                        else
                        {
                            if (!goto_next_from_playlist)
                            {
                                file_number++; 
                            }
                            else
                            {
                                goto_next_from_playlist = false;
                            }
                        }
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

            Trace.WriteLine("Disposing context");

            return;
        }
        #endregion

        // Math functions
        public Vector3 LineCoeficients(Vector3 end_coordenate, Vector3 start_coordenate) //3D vector
        {
            Vector3 path_line_vector;

            path_line_vector.X = end_coordenate.X - start_coordenate.Y;
            path_line_vector.Y = end_coordenate.Y - start_coordenate.Y;
            path_line_vector.Z = end_coordenate.Z - start_coordenate.Z;

            return path_line_vector;
        }

        public Vector3 GetVelocityComponents(Vector3 start_position, Vector3 end_position, float current_speed)
        {
            float dx, dy, dz, d;
            Vector3 velocity_vector;

            // Decomposing movement
            dx = end_position.X - start_position.X;
            dy = end_position.Y - start_position.Y;
            dz = end_position.Z - start_position.Z;

            // Total movement
            d = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);

            // Velocity vectors

            if (d > 0)
            {
                velocity_vector.X = dx / d * current_speed;
                velocity_vector.Y = dy / d * current_speed;
                velocity_vector.Z = dz / d * current_speed;
            }
            else
            {
                velocity_vector.X = 0;
                velocity_vector.Y = 0;
                velocity_vector.Z = 0;
            }

            return velocity_vector;
        }

        // Code from Creative's Effects Extension guide
        // Real Pan vector is the pan calculated here times the magnitude (real_pan = pan * mag)
        public Vector3 GetEAXReverbPan(Vector3 aperture_first_coordinate, Vector3 aperture_second_coordinate, Vector3 list_orientation, Vector3 list_position) // First and second coordinate from the reverb or reflection aperture, and the listener orientation and position
        {
            Vector3 PanVector, list_to_aperture, ApertureMidPoint;
            double angle;

            ApertureMidPoint = new Vector3((aperture_first_coordinate.X + aperture_second_coordinate.X) / 2, (aperture_first_coordinate.Y + aperture_second_coordinate.Y) / 2, (aperture_first_coordinate.Z + aperture_second_coordinate.Z) / 2);

            // First we need to translate the aperture coordinates to the "user-relative" position
            list_orientation.Normalize();
            angle = Math.Acos(list_orientation.Z);

            if (list_orientation.X < 0)
            {
                angle = -angle;
            }

            list_to_aperture = LineCoeficients(ApertureMidPoint, list_position); // Listener to Aperture midpoint vector

            // Calculate pan vector
            PanVector.X = (list_to_aperture.X * (float)Math.Cos(-angle)) + (list_to_aperture.Z * (float)Math.Sin(-angle));
            PanVector.Y = 0; // TO DO: Not really, the sound may be coming from the ceiling ou the ground... How to fix this?
            PanVector.Z = (list_to_aperture.X * -(float)Math.Sin(-angle)) + (list_to_aperture.Z * (float)Math.Cos(-angle));

            // Normalize it
            PanVector.Normalize();

            return PanVector; // This seems to be right-handed coordenates
        }

        public float GetEAXReverbMag(Vector3 aperture_first_coordinate, Vector3 aperture_second_coordinate, Vector3 list_position)
        {
            Vector3 first_vector, second_vector;
            double angle;
            float magnitude;

            // Calculate vectors to aperture extremities
            first_vector = LineCoeficients(aperture_first_coordinate, list_position);
            second_vector = LineCoeficients(aperture_second_coordinate, list_position);

            // Normalize them
            first_vector.Normalize();
            second_vector.Normalize();

            // Calculate angle in rad
            angle = Math.Acos((first_vector.X * second_vector.X) + (first_vector.Y * second_vector.Y) + (first_vector.Z * second_vector.Z));

            magnitude = (float)((2 * Math.Sin(angle / 2)) / angle);

            return magnitude;
        }

        #region GUI stuff
        // CPU usage
        private void UpdateCpuUsagePercent()
        {
            while (playbackthread_enabled)
            {
                c = 0;
                while (c < 20 && playbackthread_enabled) // Check if is still enabled, or if the application was closed
                {
                    Thread.Sleep(100); // We want to sleep a total of 2000ms
                    c++;
                }

                float total_cpu_usage = theCPUCounter.NextValue();
                CPUUsagePercent.Dispatcher.Invoke(new UpdateCPUUsageTextCallback(this.UpdateCPUUsageText), new object[] { (total_cpu_usage / CPU_logic_processors).ToString("0.0") + "%" });
            }
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            //var dialog = new System.Windows.Forms.FolderBrowserDialog();
            //System.Windows.Forms.DialogResult result = dialog.ShowDialog();

            using (FolderBrowserDialog dlgOpen = new FolderBrowserDialog())
            {
                //dlgOpen. = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
                dlgOpen.Description = "Escolha a pasta para tocar";

                System.Windows.Forms.DialogResult result = dlgOpen.ShowDialog();
                
                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    if (is_playing)
                    {
                        stop_playback = true;
                    }
                    Thread.Sleep(250); // So we are sure that notthing bad happens...
                    var allowedExtensions = new[] { ".mp3", ".wav", ".wma", ".ogg", ".flac" };
                    //filePaths = Directory.GetFiles(dlgOpen.SelectedPath)
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
                    if (paused && is_playing)
                    {
                        pause_change = true;
                        paused = false;
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
                } 
            }
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            goto_next = true;
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            stop_playback = true;
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            goback = true;
        }

        private void AboutItem_Click(object sender, RoutedEventArgs e)
        {
            //var about_window = new AboutWindow { Owner = this };
            //about_window.ShowDialog();
        }

        private void playlistItem_MouseDoubleClick(object sender, RoutedEventArgs e)
        {
            Trace.WriteLine("Double click! " + playlistItems.SelectedIndex);
            if (file_number != playlistItems.SelectedIndex)
            {
                if (playlistItems.SelectedIndex == 0)
                {
                    file_number = 0;
                }
                else
                {
                    file_number = playlistItems.SelectedIndex;
                    goto_next = true;
                    goto_next_from_playlist = true;
                }

                if (!is_playing)
                {
                    is_playing = true;
                }

                if (paused)
                {
                    SoundPlayPause.Content = "Play";
                }
                else
                {
                    SoundPlayPause.Content = "Pause";
                }
            }
    }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            double_volume = e.NewValue;
            volume = 0.0031623f * (float)Math.Exp(double_volume * 5.757);
            mudando_volume = true;
        }

        private void UpdateCPUUsageText(string message)
        {
            CPUUsagePercent.Text = message;
        }

        private void UpdateinfoText(string message)
        {
            infoText.Text = (message);
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

        public static void SetMusicCurrentTime(object source, ElapsedEventArgs e)
        {
            music_current_time++;
        }

        private void Window_Closing(object sender, EventArgs e)
        {
            stop_playback = true;
            playbackthread_enabled = false;
            is_playing = false;
        }

        #endregion

        // Loads a wave/riff audio file using NAudio.
        public static byte[] LoadMediaFoundation(string filepath, out int channels, out int bits, out int rate, out System.TimeSpan totaltime)
        {
            if (filepath == null)
                throw new ArgumentNullException("filepath");

            using (MediaFoundationReader reader = new MediaFoundationReader(filepath))
            {
                int num_channels = reader.WaveFormat.Channels;
                int sample_rate = reader.WaveFormat.SampleRate;
                int bits_per_sample = reader.WaveFormat.BitsPerSample;
                totaltime = reader.TotalTime;

                channels = num_channels;
                bits = bits_per_sample;
                rate = sample_rate;

                // Byte array
                byte[] buffer = new byte[reader.Length];
                reader.Read(buffer, 0, buffer.Length);
                reader.Dispose();

                return buffer;
            }
        }

        public static byte[] LoadMP3Path(string filepath, out int channels, out int bits, out int rate, out System.TimeSpan totaltime)
        {
            if (filepath == null)
                throw new ArgumentNullException("filepath");

            using (Mp3FileReader reader = new Mp3FileReader(filepath))
            {

                int num_channels = reader.Mp3WaveFormat.Channels;
                int sample_rate = reader.Mp3WaveFormat.SampleRate;
                int bits_per_sample = reader.Mp3WaveFormat.BitsPerSample;
                totaltime = reader.TotalTime;

                channels = num_channels;
                bits = bits_per_sample;
                rate = sample_rate;

                byte[] buffer = new byte[reader.Length];
                int read = reader.Read(buffer, 0, buffer.Length);

                return buffer;
            }
        }

        public static byte[] LoadWave(Stream stream, out int channels, out int bits, out int rate, out System.TimeSpan totaltime)
        {
            if (stream == null)
                throw new ArgumentNullException("stream");

            using (WaveFileReader reader = new WaveFileReader(stream))
            {
                int num_channels = reader.WaveFormat.Channels;
                int sample_rate = reader.WaveFormat.SampleRate;
                int bits_per_sample = reader.WaveFormat.BitsPerSample;
                totaltime = reader.TotalTime;

                channels = num_channels;
                bits = bits_per_sample;
                rate = sample_rate;

                // Byte array
                byte[] buffer = new byte[reader.Length];
                reader.Read(buffer, 0, buffer.Length);

                return buffer;
            }
        }

        public static short[] LoadWaveShort(Stream stream, out int channels, out int bits, out int rate, out System.TimeSpan totaltime)
        {
            if (stream == null)
                throw new ArgumentNullException("stream");

            using (WaveFileReader reader = new WaveFileReader(stream))
            {
                int num_channels = reader.WaveFormat.Channels;
                int sample_rate = reader.WaveFormat.SampleRate;
                int bits_per_sample = reader.WaveFormat.BitsPerSample;
                totaltime = reader.TotalTime;

                channels = num_channels;
                bits = bits_per_sample;
                rate = sample_rate;

                if (bits != 16)
                {
                    Trace.WriteLine("Only works with 16 bit audio, this on is " + bits + "-bit");

                }

                // Byte array
                byte[] buffer = new byte[reader.Length];
                int read = reader.Read(buffer, 0, buffer.Length);

                // Convert to short
                short[] sampleBuffer = new short[read / 2];
                Buffer.BlockCopy(buffer, 0, sampleBuffer, 0, read);

                return sampleBuffer;
            }
        }

        public static byte[] LoadMP3(Stream stream, out int channels, out int bits, out int rate, out System.TimeSpan totaltime)
        {
            if (stream == null)
                throw new ArgumentNullException("stream");

            using (Mp3FileReader reader = new Mp3FileReader(stream))
            {

                int num_channels = reader.Mp3WaveFormat.Channels;
                int sample_rate = reader.Mp3WaveFormat.SampleRate;
                int bits_per_sample = reader.Mp3WaveFormat.BitsPerSample;
                totaltime = reader.TotalTime;

                channels = num_channels;
                bits = bits_per_sample;
                rate = sample_rate;

                byte[] buffer = new byte[reader.Length];
                reader.Read(buffer, 0, buffer.Length);
                reader.Dispose();

                return buffer;
            }
        }

        public static byte[] LoadFLAC(Stream stream, out int channels, out int bits, out int rate, out System.TimeSpan totaltime)
        {
            if (stream == null)
                throw new ArgumentNullException("stream");

            using (FlacReader reader = new FlacReader(stream))
            {
                int num_channels = reader.WaveFormat.Channels;
                int sample_rate = reader.WaveFormat.SampleRate;
                int bits_per_sample = reader.WaveFormat.BitsPerSample;
                totaltime = reader.TotalTime;

                channels = num_channels;
                bits = bits_per_sample;
                rate = sample_rate;

                byte[] buffer = new byte[reader.Length];
                reader.Read(buffer, 0, buffer.Length);
                reader.Dispose();

                return buffer;
            }
        }

        public static byte[] LoadWMA(string filepath, out int channels, out int bits, out int rate, out System.TimeSpan totaltime)
        {
            if (filepath == null)
                throw new ArgumentNullException("filepath");

            using (WMAFileReader reader = new WMAFileReader(filepath))
            {
                int num_channels = reader.WaveFormat.Channels;
                int sample_rate = reader.WaveFormat.SampleRate;
                int bits_per_sample = reader.WaveFormat.BitsPerSample;
                totaltime = reader.TotalTime;

                channels = num_channels;
                bits = bits_per_sample;
                rate = sample_rate;

                byte[] buffer = new byte[reader.Length];
                reader.Read(buffer, 0, buffer.Length);

                return buffer;
            }
        }

        public static byte[] LoadVorbis(Stream stream, out int channels, out int bits, out int rate, out System.TimeSpan totaltime)
        {
            if (stream == null)
                throw new ArgumentNullException("stream");

            using (VorbisWaveReader reader = new VorbisWaveReader(stream))
            {
                int num_channels = reader.WaveFormat.Channels;
                int sample_rate = reader.WaveFormat.SampleRate;
                int bits_per_sample = reader.WaveFormat.BitsPerSample;
                totaltime = reader.TotalTime;

                channels = num_channels;
                bits = bits_per_sample;
                rate = sample_rate;

                byte[] buffer = new byte[reader.Length];
                reader.Read(buffer, 0, buffer.Length);

                var waveBuffer = new WaveBuffer(buffer);

                // Convert to 16-bit
                int read = waveBuffer.FloatBuffer.Length;
                short[] sampleBufferShort = new short[waveBuffer.FloatBuffer.Length / 4];

                for (uint i = 0; i < read / 4; ++i)
                {
                    if (waveBuffer.FloatBuffer[i] >= 1.0)
                        sampleBufferShort[i] = 32767;
                    else if (waveBuffer.FloatBuffer[i] <= 1.0)
                        sampleBufferShort[i] = -32768;
                    else
                        sampleBufferShort[i] = (short)Math.Floor(waveBuffer.FloatBuffer[i] * (1 << 16));
                }

                for (uint i = 0, z = 0; i < read / 4; ++i, z += 2)
                {
                    buffer[z] = (byte)sampleBufferShort[i];
                    buffer[z + 1] = (byte)(sampleBufferShort[i] >> 8);
                }

                return buffer;
            }
        }

        public static short[] LoadMP3Short(Stream stream, out int channels, out int bits, out int rate, out System.TimeSpan totaltime)
        {
            if (stream == null)
                throw new ArgumentNullException("stream");

            using (Mp3FileReader reader = new Mp3FileReader(stream))
            {
                int num_channels = reader.Mp3WaveFormat.Channels;
                int sample_rate = reader.Mp3WaveFormat.SampleRate;
                int bits_per_sample = reader.Mp3WaveFormat.BitsPerSample;
                totaltime = reader.TotalTime;

                channels = num_channels;
                bits = bits_per_sample;
                rate = sample_rate;

                if (bits != 16)
                {
                    Trace.WriteLine("Only works with 16 bit audio, this on is " + bits + "-bit");

                }

                // Byte array
                byte[] buffer = new byte[reader.Length];
                int read = reader.Read(buffer, 0, buffer.Length);

                // Convert to short
                short[] sampleBuffer = new short[read / 2];
                Buffer.BlockCopy(buffer, 0, sampleBuffer, 0, read);

                return sampleBuffer;
            }
        }

        public static ALFormat GetSoundFormat(int channels, int bits)
        {
            switch (channels)
            {
                case 1: return bits == 8 ? ALFormat.Mono8 : ALFormat.Mono16;
                case 2: return bits == 8 ? ALFormat.Stereo8 : ALFormat.Stereo16;
                default: throw new NotSupportedException("The specified sound format is not supported.");
            }
        }

        public static void mudar_velocidade(bool aumentou)
        {
            if (aumentou)
            {
                if (int_playback_speed < 20)
                {
                    int_playback_speed = int_playback_speed + 1;
                    playback_speed = int_playback_speed / 10f;
                }
            }
            else
            {
                if (int_playback_speed > 5)
                {
                    int_playback_speed = int_playback_speed - 1;
                    playback_speed = int_playback_speed / 10f;
                }
            }
            mudando_velocidade = true;
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