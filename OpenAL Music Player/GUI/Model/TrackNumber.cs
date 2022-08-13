using Common.Model;

namespace OpenALMusicPlayer.GUI.Model
{
  internal class TrackNumber : BaseModel, ITrackNumber
  {
    private static readonly string DEFAULT = "-";
    private string value = DEFAULT;
    private int rawValue;

    public string Value
    {
      get => value;
      set
      {
        this.value = value;
        OnPropertyChanged(nameof(Value));
      }
    }

    public void Set(int value)
    {
      rawValue = value;
      this.Value = value == 0 ? DEFAULT : value.ToString();
    }
  }
}
