
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

public class AudioTrackModel : INotifyPropertyChanged
{
    private double _volumeDb = 0.0;

    public List<float> WaveformData { get; set; }
    public double VolumeDb
    {
        get => _volumeDb;
        set
        {
            if (_volumeDb != value)
            {
                _volumeDb = value;
                OnPropertyChanged(); 
            }
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
