using System;
using System.Collections.Generic;
using System.Linq;

namespace AudioEngine.Helpers
{
  internal static class EnumerableExtensions
  {
    public static IEnumerable<T> Peek<T>(this IEnumerable<T> enumerable, Action<T, int> action)
    {
      return enumerable.Select((item, index) =>
      {
        action(item, index);
        return item;
      });
    }
  }
}
