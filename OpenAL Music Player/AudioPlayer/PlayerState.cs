namespace OpenALMusicPlayer.AudioPlayer
{
  internal enum PlayerState : byte
  {
    Stopped,
    StartPlayback,
    Playing,
    Paused,
    ChangingTrack,
    Pausing,
    Unpausing,
    StopPlayback
  }
}
