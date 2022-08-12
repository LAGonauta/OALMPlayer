using System.ComponentModel;

namespace OpenALMusicPlayer.GUI.Model
{
  public class PlaylistItem : INotifyPropertyChanged
  {
    private string filePath;
    private string number;
    private string title;
    private string performer;
    private string album;
    private uint discNumber;
    private string fileName;
    private uint trackNumber;

    public event PropertyChangedEventHandler PropertyChanged;

    private void OnPropertyChanged(string propertyName)
    {
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public string FilePath { get => filePath; set { filePath = value; OnPropertyChanged(nameof(FilePath)); } }
    public string Number { get => number; set { number = value; OnPropertyChanged(nameof(Number)); } }
    public string Title { get => title; set { title = value; OnPropertyChanged(nameof(Title)); } }
    public string Performer { get => performer; set { performer = value; OnPropertyChanged(nameof(Performer)); } }
    public string Album { get => album; set { album = value; OnPropertyChanged(nameof(Album)); } }
    public uint DiscNumber { get => discNumber; set { discNumber = value; OnPropertyChanged(nameof(DiscNumber)); } }
    public string FileName { get => fileName; set { fileName = value; OnPropertyChanged(nameof(FileName)); } }
    public uint TrackNumber { get => trackNumber; set { trackNumber = value; OnPropertyChanged(nameof(TrackNumber)); } }
  }
}
