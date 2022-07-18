namespace OpenALMusicPlayer.AudioEngine
{
  internal class SimpleCancellationToken
  {
    private bool canceled = false;
    private readonly object lockObj = new();

    public void Cancel()
    {
      lock (lockObj)
      {
        canceled = true;
      }
    }

    public bool IsCancellationRequested { get { lock (lockObj) { return canceled; } } }
  }
}
