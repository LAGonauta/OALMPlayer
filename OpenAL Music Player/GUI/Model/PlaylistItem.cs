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

    public string FilePath { get => filePath; set { filePath = value; OnPropertyChanged("FilePath"); } }
    public string Number { get => number; set { number = value; OnPropertyChanged("Number"); } }
    public string Title { get => title; set { title = value; OnPropertyChanged("Title"); } }
    public string Performer { get => performer; set { performer = value; OnPropertyChanged("Performer"); } }
    public string Album { get => album; set { album = value; OnPropertyChanged("Album"); } }
    public uint DiscNumber { get => discNumber; set { discNumber = value; OnPropertyChanged("DiscNumber"); } }
    public string FileName { get => fileName; set { fileName = value; OnPropertyChanged("FileName"); } }
    public uint TrackNumber { get => trackNumber; set { trackNumber = value; OnPropertyChanged("TrackNumber"); } }
  }
}
