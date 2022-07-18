namespace OpenALMusicPlayer.AudioEngine
{
  internal class SimpleCancellationToken
  {
    private volatile bool canceled = false;

    public void Cancel()
    {
      canceled = true;
    }

    public bool IsCancellationRequested => canceled;
  }
}
