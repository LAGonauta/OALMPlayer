using System;
using System.Collections.Generic;

using OpenTK;
using OpenTK.Audio;
using OpenTK.Audio.OpenAL;

using SoundBuffer = System.Int32;
using SoundSource = System.Int32;
using EffectSlot = System.Int32;
using Effect = System.Int32;
using Filter = System.Int32;

using System.IO;

namespace OALEngine
{
    /// <summary>
    /// Engine to easily create and play sources using OpenAL, C# and OpenTK.
    /// </summary>
    public class OpenALEngine : IDisposable
    {

        #region Fields
        // audio device
        AudioContext ac;
        XRamExtension XRam;
        bool xRamActive = false;

        // should effect slots and effects be managed by the engine? They are very limited...
        EffectsExtension efx;
        List<EffectSlot> slots;
        List<EffectInstance> effects = new List<EffectInstance>();
        bool efxActive = false;
        IList<string> devices = AudioContext.AvailableDevices;

        // error checking
        ALError error;

        // source generation error handling
        bool sourceReachedLimit = false;
        int maxNumberOfSources = 256;
        PriorityNumbers priorityNumber;

        // sources list
        List<SourceInstance> sources = new List<SourceInstance>();

        // finalizer
        bool isValid = true;

        #endregion

        #region Properties
        public int Sources
        {
            get { return sources.Count; }
        }

        public int GetXRamFree
        {
            get
            {
                if (xRamActive)
                {
                    return XRam.GetRamFree;
                }
                else
                {
                    return 0;
                }
            }
        }

        public int GetXRamSize
        {
            get
            {
                if (xRamActive)
                {
                    return XRam.GetRamSize;
                }
                else
                {
                    return 0;
                }
            }
        }

        public int GetEffectSlotsNumber
        {
            get
            {
                return slots.Count;
            }
        }

        public string Renderer
        {
            get
            {
                return AL.Get(ALGetString.Renderer);
            }
        }

        public bool IsXFi
        {
            get
            {
                if (AL.Get(ALGetString.Renderer).IndexOf("X-Fi") != -1)
                    return true;
                else
                    return false;
            }
        }

        public bool IeeeFloat32Support
        {
            get
            {
                if (AL.IsExtensionPresent("AL_EXT_float32"))
                    return true;
                else
                    return false;
            }
        }

        #endregion

        #region Constructors
        public OpenALEngine()
        {
            var devices = AudioContext.AvailableDevices;
            // force first device, which probably isn't generic software, and disable EFX.
            ac = new AudioContext(devices[0], 0, 0, false, false);
            XRam = new XRamExtension();
        }

        public OpenALEngine(string device)
        {
            ac = new AudioContext(device, 0, 0, false, false);
        }

        public OpenALEngine(string device, bool efx, AudioContext.MaxAuxiliarySends sends)
        {
            ac = new AudioContext(device, 0, 0, false, efx, sends);

            if (efx)
            {
                this.efx = new EffectsExtension();

                if (sends != AudioContext.MaxAuxiliarySends.UseDriverDefault)
                {
                    slots = new List<EffectSlot>((int)sends);
                }
                else
                {
                    slots = new List<EffectSlot>(4);
                }

                efxActive = true;
            }

        }

        public OpenALEngine(string device, bool efx, AudioContext.MaxAuxiliarySends sends, bool xram)
        {
            ac = new AudioContext(device, 0, 0, false, efx, sends);

            if (efx)
            {
                this.efx = new EffectsExtension();

                if (sends != AudioContext.MaxAuxiliarySends.UseDriverDefault)
                {
                    slots = new List<EffectSlot>((int)sends);
                }
                else
                {
                    slots = new List<EffectSlot>(4);
                }

                efxActive = true;
            }


            if (xram)
            {
                XRam = new XRamExtension();
                xRamActive = true;
            }
        }
        #endregion

        #region Destructors
        // Release EFX and Audio Context
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (isValid)
                {
                    isValid = false;
                    if (ac != null)
                    {
                        ac.Dispose();
                        ac = null;
                    }
                }
            }
        }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~OpenALEngine()
        {
            this.Dispose();
        }
        #endregion

        #region Public methods
        /// <summary>
        /// Plays the sound.
        /// </summary>
        public SourceInstance PlaySound(bool loop, SoundBuffer buffer)
        {
            SourceInstance source = new SourceInstance();
            source.id = AL.GenSource();
            source.priority = SoundPriority.BestEffort;

            AL.BindBufferToSource(source.id, buffer);

            if (loop)
            {
                AL.Source(source.id, ALSourceb.Looping, true);
                source.looping = true;
            }
            else
            {
                source.looping = false;
            }

            AL.SourcePlay(source.id);
            sources.Add(source);

            return sources[sources.Count - 1];
        }

        /// <summary>
        /// Plays the sound with custom volume.
        /// </summary>
        /// <param name="buffer">the sound buffer id/param>
        /// <param name="volume">sets the sound effect volume</param>
        public SourceInstance PlaySound(bool loop, SoundBuffer buffer, float volume)
        {
            SourceInstance source = new SourceInstance();
            source.id = AL.GenSource();
            source.priority = SoundPriority.BestEffort;

            AL.BindBufferToSource(source.id, buffer);
            AL.Source(source.id, ALSourcef.Gain, volume);

            if (loop)
            {
                AL.Source(source.id, ALSourceb.Looping, true);
                source.looping = true;
            }
            else
            {
                source.looping = false;
            }

            AL.SourcePlay(source.id);
            sources.Add(source);

            return sources[sources.Count - 1];
        }

        ///                         ///
        /// Two-dimension functions ///
        ///                         ///

        /// <summary>
        /// Plays the sound with custom volume on 2D screen space.
        /// </summary>
        /// <param name="buffer">the sound buffer id/param>
        /// <param name="volume">sets the sound effect volume</param>
        /// <param name="position">object position on 2D space</param>
        public SourceInstance PlaySound(bool loop, SoundBuffer buffer, float volume, Vector2 position)
        {
            Vector3 audioPosition = Convert2Dto3D((int)position.X, (int)position.Y);

            SourceInstance source = new SourceInstance();
            source.id = AL.GenSource();
            source.priority = SoundPriority.BestEffort;

            AL.BindBufferToSource(source.id, buffer);
            AL.Source(source.id, ALSource3f.Position, ref audioPosition);
            AL.Source(source.id, ALSourcef.Gain, volume);

            if (loop)
            {
                AL.Source(source.id, ALSourceb.Looping, true);
                source.looping = true;
            }
            else
            {
                source.looping = false;
            }

            AL.SourcePlay(source.id);
            sources.Add(source);

            return sources[sources.Count - 1];
        }

        /// <summary>
        /// Plays the sound with custom volume on 2D screen space.
        /// </summary>
        /// <param name="buffer">the sound buffer id/param>
        /// <param name="volume">sets the sound effect volume</param>
        /// <param name="xPosition">x position of the object on 2D space</param>
        /// <param name="yPosition">y position of the object on 2D space</param>
        public SourceInstance PlaySound(bool loop, SoundBuffer buffer, float volume, int xPosition, int yPosition)
        {
            Vector3 audioPosition = Convert2Dto3D(xPosition, yPosition);

            SourceInstance source = new SourceInstance();
            source.id = AL.GenSource();
            source.priority = SoundPriority.BestEffort;

            AL.BindBufferToSource(source.id, buffer);
            AL.Source(source.id, ALSource3f.Position, ref audioPosition);
            AL.Source(source.id, ALSourcef.Gain, volume);

            if (loop)
            {
                AL.Source(source.id, ALSourceb.Looping, true);
                source.looping = true;
            }
            else
            {
                source.looping = false;
            }

            AL.SourcePlay(source.id);
            sources.Add(source);

            return sources[sources.Count - 1];
        }

        /// <summary>
        /// Plays the sound with custom volume on 2D screen space, with doppler effect.
        /// </summary>
        /// <param name="buffer">the sound buffer id/param>
        /// <param name="volume">sets the sound effect volume</param>
        /// <param name="position">object position on 2D space</param>
        /// <param name="velocity">object velocity on 2D space</param>
        public SourceInstance PlaySound(bool loop, SoundBuffer buffer, float volume, Vector2 position, Vector2 velocity)
        {
            Vector3 audioPosition = Convert2Dto3D((int)position.X, (int)position.Y);
            Vector3 audioVelocity = Convert2Dto3D((int)velocity.X, (int)velocity.Y);

            SourceInstance source = new SourceInstance();
            source.id = AL.GenSource();
            source.priority = SoundPriority.BestEffort;

            AL.BindBufferToSource(source.id, buffer);
            AL.Source(source.id, ALSource3f.Position, ref audioPosition);
            AL.Source(source.id, ALSource3f.Velocity, ref audioVelocity);
            AL.Source(source.id, ALSourcef.Gain, volume);

            if (loop)
            {
                AL.Source(source.id, ALSourceb.Looping, true);
                source.looping = true;
            }
            else
            {
                source.looping = false;
            }

            AL.SourcePlay(source.id);
            sources.Add(source);

            return sources[sources.Count - 1];
        }

        /// <summary>
        /// Plays the sound with custom volume on 2D screen space, with doppler effect.
        /// </summary>
        /// <param name="buffer">the sound buffer id/param>
        /// <param name="volume">sets the sound effect volume</param>
        /// <param name="xPosition">x position of the object on 2D space</param>
        /// <param name="yPosition">y position of the object on 2D space</param>
        /// <param name="xVelocity">x component of the object velocity on 2D space</param>
        /// <param name="yVelocity">x component of the object velocity on 2D space</param>
        public SourceInstance PlaySound(bool loop, SoundBuffer buffer, float volume, int xPosition, int yPosition, int xVelocity, int yVelocity)
        {
            Vector3 audioPosition = Convert2Dto3D(xPosition, yPosition);
            Vector3 audioVelocity = Convert2Dto3D(xVelocity, yVelocity);

            SourceInstance source = new SourceInstance();
            source.id = AL.GenSource();
            source.priority = SoundPriority.BestEffort;

            AL.BindBufferToSource(source.id, buffer);
            AL.Source(source.id, ALSource3f.Position, ref audioPosition);
            AL.Source(source.id, ALSource3f.Velocity, ref audioVelocity);
            AL.Source(source.id, ALSourcef.Gain, volume);

            if (loop)
            {
                AL.Source(source.id, ALSourceb.Looping, true);
                source.looping = true;
            }
            else
            {
                source.looping = false;
            }

            AL.SourcePlay(source.id);
            sources.Add(source);

            return sources[sources.Count - 1];
        }

        ///                           ///
        /// Three-dimension functions ///
        ///                           ///

        /// <summary>
        /// Plays the sound with custom volume on 3D screen space.
        /// </summary>
        /// <param name="buffer">the sound buffer id/param>
        /// <param name="volume">sets the sound effect volume</param>
        /// <param name="audioPosition">object position on 3D space</param>
        public SourceInstance PlaySound(bool loop, SoundBuffer buffer, float volume, ref Vector3 audioPosition)
        {
            SourceInstance source = new SourceInstance();
            source.id = AL.GenSource();
            source.priority = SoundPriority.BestEffort;

            AL.BindBufferToSource(source.id, buffer);
            AL.Source(source.id, ALSource3f.Position, ref audioPosition);
            AL.Source(source.id, ALSourcef.Gain, volume);

            if (loop)
            {
                AL.Source(source.id, ALSourceb.Looping, true);
                source.looping = true;
            }
            else
            {
                source.looping = false;
            }

            AL.SourcePlay(source.id);
            sources.Add(source);

            return sources[sources.Count - 1];
        }

        /// <summary>
        /// Plays the sound with custom volume on 3D screen space.
        /// </summary>
        /// <param name="buffer">the sound buffer id/param>
        /// <param name="volume">sets the sound effect volume</param>
        /// <param name="xPosition">x position of the object on 3D space</param>
        /// <param name="yPosition">y position of the object on 3D space</param>
        /// <param name="zPosition">z position of the object on 3D space</param>
        public SourceInstance PlaySound(bool loop, SoundBuffer buffer, float volume, int xPosition, int yPosition, int zPosition)
        {
            Vector3 audioPosition = new Vector3(xPosition, yPosition, zPosition);

            SourceInstance source = new SourceInstance();
            source.id = AL.GenSource();
            source.priority = SoundPriority.BestEffort;

            AL.BindBufferToSource(source.id, buffer);
            AL.Source(source.id, ALSource3f.Position, ref audioPosition);
            AL.Source(source.id, ALSourcef.Gain, volume);

            if (loop)
            {
                AL.Source(source.id, ALSourceb.Looping, true);
                source.looping = true;
            }
            else
            {
                source.looping = false;
            }

            AL.SourcePlay(source.id);
            sources.Add(source);

            return sources[sources.Count - 1];
        }

        /// <summary>
        /// Plays the sound with custom volume on 3D screen space, with doppler effect.
        /// </summary>
        /// <param name="buffer">the sound buffer id/param>
        /// <param name="volume">sets the sound effect volume</param>
        /// <param name="audioPosition">object position on 3D space</param>
        /// <param name="audioVelocity"></param>
        public SourceInstance PlaySound(bool loop, SoundBuffer buffer, float volume, ref Vector3 audioPosition, ref Vector3 audioVelocity)
        {
            SourceInstance source = new SourceInstance();
            source.id = AL.GenSource();
            source.priority = SoundPriority.BestEffort;

            AL.BindBufferToSource(source.id, buffer);
            AL.Source(source.id, ALSource3f.Position, ref audioPosition);
            AL.Source(source.id, ALSource3f.Velocity, ref audioVelocity);
            AL.Source(source.id, ALSourcef.Gain, volume);

            if (loop)
            {
                AL.Source(source.id, ALSourceb.Looping, true);
                source.looping = true;
            }
            else
            {
                source.looping = false;
            }

            AL.SourcePlay(source.id);
            sources.Add(source);

            return sources[sources.Count - 1];
        }

        /// <summary>
        /// Plays the sound with custom volume on 3D screen space, with doppler effect.
        /// </summary>
        /// <param name="buffer">sound buffer id/param>
        /// <param name="volume">sets the sound effect volume</param>
        /// <param name="xPosition">x position of the object on 3D space</param>
        /// <param name="yPosition">y position of the object on 3D space</param>
        /// <param name="zPosition">z position of the object on 3D space</param>
        /// <param name="xVelocity">x component of the object velocity on 3D space</param>
        /// <param name="yVelocity">x component of the object velocity on 3D space</param>
        /// <param name="zVelocity">z component of the object velocity on 3D space</param>
        public SourceInstance PlaySound(bool loop, SoundBuffer buffer, float volume, int xPosition, int yPosition, int zPosition, int xVelocity, int yVelocity, int zVelocity)
        {
            Vector3 audioPosition = new Vector3(xPosition, yPosition, zPosition);
            Vector3 audioVelocity = new Vector3(xVelocity, yVelocity, zVelocity);

            SourceInstance source = new SourceInstance();
            source.id = AL.GenSource();
            source.priority = SoundPriority.BestEffort;

            AL.BindBufferToSource(source.id, buffer);
            AL.Source(source.id, ALSource3f.Position, ref audioPosition);
            AL.Source(source.id, ALSource3f.Velocity, ref audioVelocity);
            AL.Source(source.id, ALSourcef.Gain, volume);

            if (loop)
            {
                AL.Source(source.id, ALSourceb.Looping, true);
                source.looping = true;
            }
            else
            {
                source.looping = false;
            }

            AL.SourcePlay(source.id);
            sources.Add(source);

            return sources[sources.Count - 1];
        }

        ///             ///
        /// EFX methods ///
        ///             ///

        public Filter GenFilter()
        {
            return efx.GenFilter();
        }

        // filter overloads
        public void SetFilterToSource(Filter filter, SoundSource source)
        {
            if (efx.IsFilter(filter))
            {
                efx.BindFilterToSource(source, filter);
            }
        }

        public void GenEffect(EfxEffectType type)
        {
            EffectInstance effect = new EffectInstance();
            effect.id = efx.GenEffect();
            effect.type = type;

            efx.BindEffect(effect.id, type);

            effects.Add(effect);
        }

        // effect overloads
        public void NewSlot()
        {
            if (slots.Count < slots.Capacity)
            {
                slots.Add(efx.GenAuxiliaryEffectSlot());
            }
        }

        /// <summary>
        /// Deletes a desired buffer and releases the memory.
        /// </summary>
        /// <param name="buffer">sound buffer id</param>
        public void DeleteBuffer(SoundBuffer buffer)
        {
            AL.DeleteBuffer(buffer);
        }

        /// <summary>
        /// Checks all generated sources and removes those that stopped playing.
        /// </summary>
        public void Check()
        {
            for (int i = sources.Count - 1; i >= 0; --i)
            {
                if (AL.GetSourceState(sources[i].id) == ALSourceState.Stopped)
                {
                    AL.DeleteSource(sources[i].id);
                    sources.RemoveAt(i);
                }
            }
        }

        public void StopAll()
        {
            foreach (SourceInstance source in sources)
            {
                AL.SourceStop(source.id);
            }
        }

        public ALError CheckALError(string message)
        {
            error = AL.GetError();

            if (error != ALError.NoError)
            {
                System.Diagnostics.Trace.WriteLine("Error " + message + ": " + error);
            }

            return error;
        }
        #endregion

        #region Private methods
        /// <summary>
        /// Converts a 2D audio from the game screen to a 3D sound in front of the user.
        /// </summary>
        /// <param name="xPos">object x position</param>
        /// <param name="yPos">object y position</param>
        /// <returns>object position on 3D space</returns>
        private static OpenTK.Vector3 Convert2Dto3D(int xPos, int yPos)
        {
            float soundScreenScaleFactor = 1;
            float soundScreendistance = -1;
            int WindowHeight = 800;
            int WindowWidth = 600;

            // on screen Y goes 0 (up) to Height (down), here there is code to to invert it and use on OpenAL.
            OpenTK.Vector3 output = new OpenTK.Vector3((xPos / (float)WindowWidth * 2 - 1) * soundScreenScaleFactor,
                ((1 - yPos / (float)WindowHeight) * 2 - 1) * soundScreenScaleFactor,
                soundScreendistance);

            return output;
        }

        /// <summary>
        /// Enforces source priority, but only if OpenAL errored out generating a source at least once.
        /// </summary>
        /// <param name="source">source instance</param>
        /// <returns>Returns true if it is safe to play, false otherwise.</returns>
        private bool CheckSourcePriority(SourceInstance source)
        {
            bool safety = false;
            if (sourceReachedLimit)
            {
                if (source.priority == SoundPriority.MustPlay)
                {
                    safety = ForceSourceStop(source.priority);
                    return true;
                }
                return false;
            }
            else
            {
                return true;
            }
        }

        private bool ForceSourceStop(SoundPriority priority)
        {
            if (priorityNumber.BestEffort > 0)
            {
                foreach (SourceInstance source in sources)
                {
                    if (source.priority == SoundPriority.BestEffort)
                    {
                        AL.SourceStop(source.id);
                        AL.DeleteSource(source.id);
                        if (CheckALError("deleting source") != ALError.NoError)
                        {
                            return false;
                        }
                        else
                        {
                            --priorityNumber.BestEffort;
                            return true;
                        }
                    }
                }
            }
            else if (priorityNumber.Low > 0 && priority > SoundPriority.BestEffort)
            {
                foreach (SourceInstance source in sources)
                {
                    if (source.priority == SoundPriority.Low)
                    {
                        AL.SourceStop(source.id);
                        AL.DeleteSource(source.id);
                        if (CheckALError("deleting source") != ALError.NoError)
                        {
                            return false;
                        }
                        else
                        {
                            --priorityNumber.Low;
                            return true;
                        }
                    }
                }
            }
            else if (priorityNumber.Medium > 0 && priority > SoundPriority.Low)
            {
                foreach (SourceInstance source in sources)
                {
                    if (source.priority == SoundPriority.Medium)
                    {
                        AL.SourceStop(source.id);
                        AL.DeleteSource(source.id);
                        if (CheckALError("deleting source") != ALError.NoError)
                        {
                            return false;
                        }
                        else
                        {
                            --priorityNumber.Medium;
                            return true;
                        }
                    }
                }
            }
            else if (priorityNumber.High > 0 && priority > SoundPriority.Medium)
            {
                foreach (SourceInstance source in sources)
                {
                    if (source.priority == SoundPriority.High)
                    {
                        AL.SourceStop(source.id);
                        AL.DeleteSource(source.id);
                        if (CheckALError("deleting source") != ALError.NoError)
                        {
                            return false;
                        }
                        else
                        {
                            --priorityNumber.High;
                            return true;
                        }
                    }
                }
            }
            else
            {
                if (priority == SoundPriority.MustPlay)
                {
                    foreach (SourceInstance source in sources)
                    {
                        if (source.priority == SoundPriority.MustPlay)
                        {
                            AL.SourceStop(source.id);
                            AL.DeleteSource(source.id);
                            if (CheckALError("deleting source") != ALError.NoError)
                            {
                                return false;
                            }
                            else
                            {
                                --priorityNumber.MustPlay;
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        private void UpdatePriorityNumber(SoundPriority priority)
        {
            switch (priority)
            {
                case SoundPriority.BestEffort:
                    ++priorityNumber.BestEffort;
                    break;
                case SoundPriority.Low:
                    ++priorityNumber.Low;
                    break;
                case SoundPriority.Medium:
                    ++priorityNumber.Medium;
                    break;
                case SoundPriority.High:
                    ++priorityNumber.High;
                    break;
                case SoundPriority.MustPlay:
                    ++priorityNumber.MustPlay;
                    break;
            }
        }
        #endregion

        #region Enumerations and structs
        public enum SoundPriority
        {
            BestEffort,
            Low,
            Medium,
            High,
            MustPlay
        };

        public struct SourceInstance
        {
            public SoundSource id;
            public SoundPriority priority;
            public bool looping;
        }

        struct EffectInstance
        {
            public Effect id;
            public EfxEffectType type;
        }

        struct PriorityNumbers
        {
            public int BestEffort;
            public int Low;
            public int Medium;
            public int High;
            public int MustPlay;
        }
        #endregion
    }
}