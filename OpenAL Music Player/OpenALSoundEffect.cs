using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using OpenTK;
using OpenTK.Audio;
using OpenTK.Audio.OpenAL;

using SoundBuffer = System.Int32;
using Effect = System.Int32;
using Filter = System.Int32;

using System.IO;
using System.Diagnostics;

using CSCore;
using CSCore.Codecs;
using NVorbis;

namespace OALEngine
{
    public class OpenALSoundEffect : IDisposable
    {
        #region Fields
        // audio device
        OpenALEngine alengine;
        IList<string> devices = AudioContext.AvailableDevices;

        // buffer
        SoundBuffer buffer;

        // sources
        List<OpenALEngine.SourceInstance> sources = new List<OpenALEngine.SourceInstance>();

        // filter
        Filter filter;

        // valid sound effect
        bool isValid = true;

        // looping support
        bool looping = false;

        // pitch
        float pitch = 1f;

        // gain
        float gain = 1f;
        float mingain = 0f;

        // time in seconds
        float currentTime = 0;
        float totalTime = 0;

        #endregion

        #region Properties
        public bool IsLooped
        {
            get
            {
                return looping;
            }
            set
            {
                looping = value;

                foreach (OpenALEngine.SourceInstance source in sources)
                {
                    if (AL.IsSource(source.id))
                        AL.Source(source.id, ALSourceb.Looping, value);
                }
            }
        }

        public bool IsPlaying
        {
            get
            {
                bool playing = false;
                foreach (OpenALEngine.SourceInstance source in sources)
                {
                    if (AL.GetSourceState(source.id) == ALSourceState.Playing)
                        playing = true;
                }
                return playing;
            }
        }

        public Filter Filter
        {
            get
            {
                return filter;
            }

            set
            {
                filter = value;
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

                foreach (OpenALEngine.SourceInstance source in sources)
                {
                    if (AL.IsSource(source.id))
                        AL.Source(source.id, ALSourcef.Pitch, pitch);
                }
            }


        }

        public float MinGain
        {
            get
            {
                return mingain;
            }
            set
            {
                mingain = value;

                foreach (OpenALEngine.SourceInstance source in sources)
                {
                    if (AL.IsSource(source.id))
                        AL.Source(source.id, ALSourcef.MinGain, mingain);
                }
            }
        }

        public float Gain
        {
            get
            {
                return gain;
            }

            set
            {
                gain = value;
                foreach (OpenALEngine.SourceInstance source in sources)
                {
                    if (AL.IsSource(source.id))
                        AL.Source(source.id, ALSourcef.Gain, gain);
                }
            }
        }

        public float CurrentTime
        {
            get
            {
                foreach (OpenALEngine.SourceInstance source in sources)
                {
                    if (AL.IsSource(source.id))
                        AL.GetSource(source.id, ALSourcef.SecOffset, out currentTime);
                }
                return currentTime;
            }
        }

        public float TotalTime
        {
            get
            {
                return totalTime;
            }
        }

        #endregion

        #region Constructors
        /// <summary>
        /// Generates the SoundEffect, should also check file type. Not sure if we need to pass the AudioContext...
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="ac"></param>
        public OpenALSoundEffect(string filePath, ref OpenALEngine alengine)
        {
            if (alengine != null && filePath != null)
            {
                this.alengine = alengine;
                this.buffer = ALContentLoad(filePath);
                this.filter = alengine.GenFilter();
            }

            // vorbis support
            CodecFactory.Instance.Register("ogg-vorbis", new CodecFactoryEntry(s => new NVorbisSource(s).ToWaveSource(), ".ogg"));
        }
        #endregion

        #region Destructors
        public void Dispose()
        {
            if (isValid)
            {
                this.Stop();
                alengine.Check();
                alengine.DeleteBuffer(buffer);
                isValid = false;
            }
        }

        ~OpenALSoundEffect()
        {
            this.Dispose();
        }
        #endregion

        #region Public methods
        public void Stop()
        {
            for (int i = sources.Count - 1; i >= 0; --i)
            {
                if (AL.GetSourceState(sources[i].id) != ALSourceState.Stopped)
                {
                    AL.SourceStop(sources[i].id);
                }
            }
            this.Check();
        }

        public void Pause()
        {
            foreach (OpenALEngine.SourceInstance source in sources)
            {
                if (AL.GetSourceState(source.id) != ALSourceState.Paused)
                {
                    AL.SourcePause(source.id);
                }
            }
        }

        public void Unpause()
        {
            foreach (OpenALEngine.SourceInstance source in sources)
            {
                if (AL.GetSourceState(source.id) == ALSourceState.Paused)
                {
                    AL.SourcePlay(source.id);
                }
            }
        }

        public OpenALEngine.SourceInstance Play()
        {
            var source = alengine.PlaySound(looping, buffer);
            sources.Add(source);
            return source;
        }

        public OpenALEngine.SourceInstance Play(float volume)
        {
            var source = alengine.PlaySound(looping, buffer, volume);
            sources.Add(source);
            return source;
        }

        ///                         ///
        /// Two-dimension functions ///
        ///                         ///

        public OpenALEngine.SourceInstance Play(float volume, Vector2 position)
        {
            var source = alengine.PlaySound(looping, buffer, volume, position);
            sources.Add(source);
            return source;
        }

        public OpenALEngine.SourceInstance Play(float volume, int xPosition, int yPosition)
        {
            var source = alengine.PlaySound(looping, buffer, volume, xPosition, yPosition);
            sources.Add(source);
            return source;
        }

        public OpenALEngine.SourceInstance Play(float volume, Vector2 position, Vector2 velocity)
        {
            var source = alengine.PlaySound(looping, buffer, volume, position, velocity);
            sources.Add(source);
            return source;
        }

        public OpenALEngine.SourceInstance Play(float volume, int xPosition, int yPosition, int xVelocity, int yVelocity)
        {
            var source = alengine.PlaySound(looping, buffer, volume, xPosition, yPosition, xVelocity, yVelocity);
            sources.Add(source);
            return source;
        }

        //public void SetPosition(OpenALEngine.SourceInstance source, Vector2 position)
        //{

        //}

        //public void SetPosition(OpenALEngine.SourceInstance source, Vector2 position)
        //{

        //}

        //public void SetVelocity(OpenALEngine.SourceInstance source, Vector2 position)
        //{

        //}

        //public void SetPosition(OpenALEngine.SourceInstance source, Vector2 position)
        //{

        //}

        ///                           ///
        /// Three-dimension functions ///
        ///                           ///

        public OpenALEngine.SourceInstance Play(float volume, Vector3 position)
        {
            var source = alengine.PlaySound(looping, buffer, volume, ref position);
            sources.Add(source);
            return source;
        }

        public OpenALEngine.SourceInstance Play(float volume, int xPosition, int yPosition, int zPosition)
        {
            var source = alengine.PlaySound(looping, buffer, volume, xPosition, yPosition, zPosition);
            sources.Add(source);
            return source;
        }

        public OpenALEngine.SourceInstance Play(float volume, Vector3 position, Vector3 velocity)
        {
            var source = alengine.PlaySound(looping, buffer, volume, ref position, ref velocity);
            sources.Add(source);
            return source;
        }

        public OpenALEngine.SourceInstance Play(float volume, int xPosition, int yPosition, int zPosition, int xVelocity, int yVelocity, int zVelocity)
        {
            var source = alengine.PlaySound(looping, buffer, volume, xPosition, yPosition, zPosition, xVelocity, yVelocity, zVelocity);
            sources.Add(source);
            return source;
        }

        public void SetPosition(ref Vector3 position)
        {
            foreach (OpenALEngine.SourceInstance source in sources)
            {
                if (AL.IsSource(source.id))
                    AL.Source(source.id, ALSource3f.Position, ref position);
            }
        }

        public void SetVelocity(ref Vector3 velocity)
        {
            foreach (OpenALEngine.SourceInstance source in sources)
            {
                if (AL.IsSource(source.id))
                    AL.Source(source.id, ALSource3f.Velocity, ref velocity);
            }
        }

        ///             ///
        /// EFX support ///
        ///             ///

        //public void Filter

        public void Check()
        {
            for (int i = sources.Count - 1; i >= 0; --i)
            {
                if (!AL.IsSource(sources[i].id))
                {
                    sources.RemoveAt(i);
                }
            }
        }
        #endregion

        #region Private methods
        /// <summary>
        /// Get correct OpenAL sound enum from file information
        /// </summary>
        /// <param name="channels">numberr of audio channels</param>
        /// <param name="bits">number of bits</param>
        /// <returns></returns>
        private ALFormat GetSoundFormat(int channels, int bits)
        {
            switch (channels)
            {
                case 1: return bits == 8 ? ALFormat.Mono8 : ALFormat.Mono16;
                case 2: return bits == 8 ? ALFormat.Stereo8 : ALFormat.Stereo16;
                default: throw new NotSupportedException("The specified sound format is not supported.");
            }
        }

        private SoundBuffer ALContentLoad(string filePath)
        {
            var AudioFile = CodecFactory.Instance.GetCodec(filePath);
            byte[] sound_data = new byte[AudioFile.Length];
            AudioFile.Read(sound_data, 0, sound_data.Length);

            SoundBuffer soundBuffer = AL.GenBuffer();
            AL.BufferData(soundBuffer, GetSoundFormat(AudioFile.WaveFormat.Channels, AudioFile.WaveFormat.BitsPerSample), sound_data, sound_data.Length, AudioFile.WaveFormat.SampleRate);
            totalTime = AudioFile.Length * sizeof(byte) / AudioFile.WaveFormat.BytesPerSecond;

            AudioFile.Dispose();
            return soundBuffer;
        }
        #endregion
    }

    // From https://github.com/filoe/cscore/blob/master/Samples/NVorbisIntegration/Program.cs
    public sealed class NVorbisSource : ISampleSource
    {
        private readonly Stream _stream;
        private readonly VorbisReader _vorbisReader;

        private readonly WaveFormat _waveFormat;
        private bool _disposed;

        public NVorbisSource(Stream stream)
        {
            if (stream == null)
                throw new ArgumentNullException("stream");
            if (!stream.CanRead)
                throw new ArgumentException("Stream is not readable.", "stream");
            _stream = stream;
            _vorbisReader = new VorbisReader(stream, false);
            _waveFormat = new WaveFormat(_vorbisReader.SampleRate, 32, _vorbisReader.Channels, AudioEncoding.IeeeFloat);
        }

        public bool CanSeek
        {
            get { return _stream.CanSeek; }
        }

        public WaveFormat WaveFormat
        {
            get { return _waveFormat; }
        }

        //got fixed through workitem #17, thanks for reporting @rgodart.
        public long Length
        {
            get { return CanSeek ? (long)(_vorbisReader.TotalTime.TotalSeconds * _waveFormat.SampleRate * _waveFormat.Channels) : 0; }
        }

        //got fixed through workitem #17, thanks for reporting @rgodart.
        public long Position
        {
            get
            {
                return CanSeek ? (long)(_vorbisReader.DecodedTime.TotalSeconds * _vorbisReader.SampleRate * _vorbisReader.Channels) : 0;
            }
            set
            {
                if (!CanSeek)
                    throw new InvalidOperationException("NVorbisSource is not seekable.");
                if (value < 0 || value > Length)
                    throw new ArgumentOutOfRangeException("value");

                _vorbisReader.DecodedTime = TimeSpan.FromSeconds((double)value / _vorbisReader.SampleRate / _vorbisReader.Channels);
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            return _vorbisReader.ReadSamples(buffer, offset, count);
        }

        public void Dispose()
        {
            if (!_disposed)
                _vorbisReader.Dispose();
            else
                throw new ObjectDisposedException("NVorbisSource");
            _disposed = true;
        }
    }
}
