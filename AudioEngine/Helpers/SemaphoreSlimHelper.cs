using System.Threading;
using System.Threading.Tasks;

namespace AudioEngine.Helpers
{
  internal static class SemaphoreSlimHelper
  {
    public static ReleaseToken Lock(this SemaphoreSlim semaphore,
    CancellationToken cancellationToken = default)
    {
      semaphore.Wait(cancellationToken);
      return new ReleaseToken(semaphore);
    }

    public static async ValueTask<ReleaseToken> LockAsync(this SemaphoreSlim semaphore,
        CancellationToken cancellationToken = default)
    {
      await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
      return new ReleaseToken(semaphore);
    }
  }
}
