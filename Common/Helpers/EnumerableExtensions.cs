namespace Common.Helpers
{
    public static class EnumerableExtensions
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
