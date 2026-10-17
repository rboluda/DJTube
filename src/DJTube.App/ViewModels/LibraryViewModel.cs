using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DJTube.App.Localization;
using DJTube.Core.Abstractions;
using DJTube.Core.Analysis;
using DJTube.Core.Logic;
using DJTube.Core.Models;
using DJTube.Sources.YouTube;

namespace DJTube.App.ViewModels;

/// <summary>Una canción de la biblioteca. Artista y título se pueden editar (se guardan al confirmar).</summary>
public partial class LibraryItem : ObservableObject
{
    private readonly Action<LibraryItem> _save;
    private bool _loading = true;

    [ObservableProperty]
    private string? _artist;

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private double? _bpm;

    [ObservableProperty]
    private ImageSource? _artwork;

    public LibraryItem(TrackInfo track, Action<LibraryItem> save)
    {
        Id = track.Id;
        _artist = track.Artist;
        _title = track.Title;
        Duration = track.Duration;
        _save = save;
        _loading = false;
    }

    public string Id { get; }

    public TimeSpan? Duration { get; }

    public string DurationText => Duration is { } d ? $"{(int)d.TotalMinutes}:{d.Seconds:00}" : "";

    public string BpmText => Bpm is { } b ? b.ToString("0.0", CultureInfo.InvariantCulture) : "";

    public string DisplayName => TrackTitleParser.Display(Artist, Title);

    public string Link => YouTubeUrl.ToWatchUrl(Id);

    partial void OnBpmChanged(double? value) => OnPropertyChanged(nameof(BpmText));

    partial void OnArtistChanged(string? value)
    {
        OnPropertyChanged(nameof(DisplayName));
        if (!_loading)
            _save(this);
    }

    partial void OnTitleChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayName));
        if (!_loading)
            _save(this);
    }

    /// <summary>Restaura los valores guardados (si la edición no era válida) sin volver a guardar.</summary>
    internal void Reset(TrackInfo track)
    {
        _loading = true;
        Artist = track.Artist;
        Title = track.Title;
        _loading = false;
    }
}

/// <summary>
/// Biblioteca: las canciones ya descargadas (caché de YouTube), con búsqueda, carga en los platos,
/// edición de artista y título, y borrado.
/// </summary>
public partial class LibraryViewModel : ObservableObject
{
    private readonly YouTubeTrackSource _source;
    private readonly ITrackAnalyzer _analyzer;
    private readonly DeckViewModel _deckA;
    private readonly DeckViewModel _deckB;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private LibraryItem? _selected;

    public LibraryViewModel(YouTubeTrackSource source, ITrackAnalyzer analyzer, DeckViewModel deckA, DeckViewModel deckB)
    {
        _source = source;
        _analyzer = analyzer;
        _deckA = deckA;
        _deckB = deckB;
        View = CollectionViewSource.GetDefaultView(Items);
        View.Filter = Matches;
        View.SortDescriptions.Add(new SortDescription(nameof(LibraryItem.DisplayName), ListSortDirection.Ascending));

        // Descargas nuevas y BPM recién calculados o corregidos.
        source.TrackAdded += (_, track) => Application.Current?.Dispatcher.InvokeAsync(() => AddOrUpdate(track));
        deckA.GridChanged += (_, path) => RefreshBpm(path);
        deckB.GridChanged += (_, path) => RefreshBpm(path);
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CountText));
        Loc.Instance.LanguageChanged += (_, _) => OnPropertyChanged(nameof(CountText));
    }

    public ObservableCollection<LibraryItem> Items { get; } = [];

    public ICollectionView View { get; }

    public string CountText => Loc.F("Library_Count", Items.Count);

    public bool IsEmpty => Items.Count == 0;

    partial void OnSearchTextChanged(string value) => View.Refresh();

    /// <summary>Lee la caché (en segundo plano) y rellena la lista.</summary>
    public async Task LoadAsync()
    {
        var tracks = new List<TrackInfo>();
        await foreach (var track in _source.BrowseAsync(null))
            tracks.Add(track);

        Items.Clear();
        foreach (var track in tracks)
            Items.Add(new LibraryItem(track, Save));
        OnPropertyChanged(nameof(IsEmpty));

        // BPM y miniaturas sin bloquear la interfaz.
        foreach (var item in Items.ToList())
            _ = FillDetailsAsync(item);
    }

    [RelayCommand]
    private Task LoadToA(LibraryItem? item) => LoadInto(_deckA, item);

    [RelayCommand]
    private Task LoadToB(LibraryItem? item) => LoadInto(_deckB, item);

    /// <summary>Doble clic: al plato que no está sonando (A si ninguno suena).</summary>
    [RelayCommand]
    private Task LoadToFree(LibraryItem? item) =>
        LoadInto(_deckA.IsPlaying && !_deckB.IsPlaying ? _deckB : _deckA, item);

    [RelayCommand]
    private void Delete(LibraryItem? item)
    {
        if ((item ?? Selected) is not { } target)
            return;
        if (_deckA.LoadedTrack?.Id == target.Id || _deckB.LoadedTrack?.Id == target.Id)
        {
            MessageBox.Show(Loc.F("Library_InUse", target.DisplayName), "DJTube", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(Loc.F("Library_DeleteConfirm", target.DisplayName), "DJTube",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;

        if (_analyzer is CachingTrackAnalyzer cache)
            cache.Forget(_source.GetAudioPath(target.Id));
        _source.Delete(target.Id);
        Items.Remove(target);
        OnPropertyChanged(nameof(IsEmpty));
    }

    private async Task LoadInto(DeckViewModel deck, LibraryItem? item)
    {
        if ((item ?? Selected) is { } target)
            await deck.LoadTrackAsync(_source, _source.CreateTrackInfo(target.Id));
    }

    private void Save(LibraryItem item)
    {
        // Si el título queda vacío no se guarda y se vuelve a lo que había. La vista recoloca la fila
        // sola al confirmar la edición (aquí no se puede llamar a Refresh: la edición sigue abierta).
        var saved = _source.UpdateMetadata(item.Id, item.Artist, item.Title);
        item.Reset(saved ?? _source.CreateTrackInfo(item.Id));
    }

    private void AddOrUpdate(TrackInfo track)
    {
        var existing = Items.FirstOrDefault(i => i.Id == track.Id);
        if (existing is not null)
            existing.Reset(track);
        else
        {
            var item = new LibraryItem(track, Save);
            Items.Add(item);
            _ = FillDetailsAsync(item);
        }
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void RefreshBpm(string? path)
    {
        if (path is null)
            return;
        var item = Items.FirstOrDefault(i => string.Equals(_source.GetAudioPath(i.Id), path, StringComparison.OrdinalIgnoreCase));
        if (item is not null)
            item.Bpm = _analyzer.TryGetCachedGrid(path)?.Bpm;
    }

    private async Task FillDetailsAsync(LibraryItem item)
    {
        string audio = _source.GetAudioPath(item.Id);
        var (bpm, image) = await Task.Run(() => (_analyzer.TryGetCachedGrid(audio)?.Bpm, LoadThumbnail(Path.ChangeExtension(audio, ".jpg"))));
        item.Bpm = bpm;
        item.Artwork = image;
        if (image is null)
        {
            // Canciones que nunca se han visto en un plato: se baja la miniatura (y queda en la caché).
            string? downloaded = await _source.GetArtworkAsync(_source.CreateTrackInfo(item.Id));
            if (downloaded is not null)
                item.Artwork = await Task.Run(() => LoadThumbnail(downloaded));
        }
    }

    private static ImageSource? LoadThumbnail(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 96;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private bool Matches(object o)
    {
        if (o is not LibraryItem item || string.IsNullOrWhiteSpace(SearchText))
            return true;
        var words = SearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string text = item.DisplayName;
        return words.All(w => text.Contains(w, StringComparison.CurrentCultureIgnoreCase));
    }
}
