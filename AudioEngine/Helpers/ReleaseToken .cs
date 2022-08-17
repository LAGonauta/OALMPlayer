using System;
using System.Threading;

namespace AudioEngine.Helpers
{
  public readonly struct ReleaseToken : IDisposable
  {
    private readonly SemaphoreSlim _semaphore;
    public ReleaseToken(SemaphoreSlim semaphore) => _semaphore = semaphore;
    public void Dispose() => _semaphore?.Release();
  }
}
