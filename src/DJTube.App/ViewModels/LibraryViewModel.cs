using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DJTube.App.Localization;
using DJTube.App.Services;
using DJTube.Core.Abstractions;
using DJTube.Core.Analysis;
using DJTube.Core.Library;
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

    /// <summary>Pone los valores guardados sin volver a guardar.</summary>
    internal void Reset(TrackInfo track)
    {
        _loading = true;
        Artist = track.Artist;
        Title = track.Title;
        _loading = false;
    }
}

/// <summary>Una rama del árbol de la biblioteca: una lista importada o las canciones sueltas.</summary>
public partial class LibraryGroup : ObservableObject
{
    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private int _count;

    /// <summary>Canciones de la lista en YouTube (descargadas o no); 0 en las sueltas.</summary>
    [ObservableProperty]
    private int _total;

    public LibraryGroup(string? playlistId, string title, string? url)
    {
        PlaylistId = playlistId;
        Url = url;
        _title = title;
    }

    /// <summary>null = canciones sueltas.</summary>
    public string? PlaylistId { get; }

    public string? Url { get; set; }

    public bool IsPlaylist => PlaylistId is not null;

    /// <summary>Orden de las ramas: listas por nombre y las sueltas al final.</summary>
    public int Order { get; set; }

    public string CountText => IsPlaylist && Total > Count ? Loc.F("Library_PlaylistCount", Count, Total) : Count.ToString(CultureInfo.CurrentCulture);

    partial void OnCountChanged(int value) => OnPropertyChanged(nameof(CountText));

    partial void OnTotalChanged(int value) => OnPropertyChanged(nameof(CountText));

    internal void RefreshTexts() => OnPropertyChanged(nameof(CountText));
}

/// <summary>Una fila de la biblioteca: una canción dentro de una rama (la misma canción puede estar en varias listas).</summary>
public sealed class LibraryEntry(LibraryItem track, LibraryGroup group, int position)
{
    public LibraryItem Track { get; } = track;

    public LibraryGroup Group { get; } = group;

    /// <summary>Posición dentro de la lista de YouTube.</summary>
    public int Position { get; set; } = position;
}

/// <summary>
/// Biblioteca: las canciones ya descargadas (caché de YouTube) en forma de árbol, una rama por lista
/// importada y otra con las sueltas. Permite importar canciones o listas enteras, buscar, cargar en los
/// platos, editar artista y título, y quitar canciones o listas. El BPM se calcula en segundo plano.
/// </summary>
public partial class LibraryViewModel : ObservableObject
{
    public const double MinFontSize = 10;
    public const double MaxFontSize = 20;
    private const int ParallelDownloads = 2;

    /// <summary>A partir de cuántas canciones nuevas se pide confirmación antes de descargar una lista.</summary>
    private const int LargePlaylist = 100;

    private readonly YouTubeTrackSource _source;
    private readonly ITrackAnalyzer _analyzer;
    private readonly PlaylistStore _playlists;
    private readonly DeckViewModel _deckA;
    private readonly DeckViewModel _deckB;
    private readonly AppSettings _settings;
    private readonly Action _saveSettings;
    private readonly Dictionary<string, LibraryItem> _tracks = [];
    private readonly Dictionary<string, LibraryGroup> _groups = [];
    private readonly LibraryGroup _loose;
    private readonly SemaphoreSlim _importGate = new(1, 1);
    private readonly Channel<string> _analysisQueue = Channel.CreateUnbounded<string>();
    private readonly HashSet<string> _analysisPending = [];
    private CancellationTokenSource _importCts = new();
    private int _importsWaiting;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private LibraryEntry? _selected;

    [ObservableProperty]
    private string _importText = "";

    [ObservableProperty]
    private bool _isImporting;

    [ObservableProperty]
    private string _importStatus = "";

    [ObservableProperty]
    private double? _importProgress;

    [ObservableProperty]
    private bool _importHasError;

    [ObservableProperty]
    private string _analysisStatus = "";

    [ObservableProperty]
    private double _fontSize;

    public LibraryViewModel(
        YouTubeTrackSource source, ITrackAnalyzer analyzer, PlaylistStore playlists,
        DeckViewModel deckA, DeckViewModel deckB, AppSettings settings, Action saveSettings)
    {
        _source = source;
        _analyzer = analyzer;
        _playlists = playlists;
        _deckA = deckA;
        _deckB = deckB;
        _settings = settings;
        _saveSettings = saveSettings;
        _fontSize = Math.Clamp(settings.LibraryFontSize, MinFontSize, MaxFontSize);
        _loose = new LibraryGroup(null, Loc.T("Library_Loose"), null) { Order = int.MaxValue, IsExpanded = true };

        View = CollectionViewSource.GetDefaultView(Entries);
        View.Filter = Matches;
        ApplyDefaultSort();

        // Descargas nuevas (de una pletina o de una importación) y BPM recién calculados o corregidos.
        source.TrackAdded += (_, track) => Application.Current?.Dispatcher.InvokeAsync(() => OnTrackAdded(track));
        deckA.GridChanged += (_, path) => RefreshBpm(path);
        deckB.GridChanged += (_, path) => RefreshBpm(path);
        Entries.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CountText));
        Loc.Instance.LanguageChanged += (_, _) =>
        {
            _loose.Title = Loc.T("Library_Loose");
            foreach (var group in _groups.Values)
                group.RefreshTexts();
            OnPropertyChanged(nameof(CountText));
        };
        _ = RunAnalysisQueueAsync();
    }

    public ObservableCollection<LibraryEntry> Entries { get; } = [];

    public ICollectionView View { get; }

    public string CountText => Loc.F("Library_Count", _tracks.Count);

    public bool IsEmpty => _tracks.Count == 0;

    /// <summary>Hay listas: la biblioteca se muestra como árbol.</summary>
    public bool IsGrouped => View.GroupDescriptions.Count > 0;

    /// <summary>Alto de fila según el tamaño de letra.</summary>
    public double RowHeight => Math.Round(FontSize * 2.4);

    /// <summary>Miniatura a la altura de la fila (16:9).</summary>
    public double ArtworkHeight => RowHeight - 7;

    public double ArtworkWidth => Math.Round(ArtworkHeight * 16 / 9);

    partial void OnSearchTextChanged(string value)
    {
        // Al buscar se despliegan las listas para ver los resultados.
        if (!string.IsNullOrWhiteSpace(value))
        {
            foreach (var group in _groups.Values)
                group.IsExpanded = true;
        }
        View.Refresh();
    }

    partial void OnFontSizeChanged(double value)
    {
        OnPropertyChanged(nameof(RowHeight));
        OnPropertyChanged(nameof(ArtworkHeight));
        OnPropertyChanged(nameof(ArtworkWidth));
        _settings.LibraryFontSize = value;
        _saveSettings();
    }

    [RelayCommand]
    private void FontLarger() => FontSize = Math.Min(MaxFontSize, FontSize + 1);

    [RelayCommand]
    private void FontSmaller() => FontSize = Math.Max(MinFontSize, FontSize - 1);

    /// <summary>Lee la caché y las listas, rellena el árbol y pone en cola el BPM que falte.</summary>
    public async Task LoadAsync()
    {
        try
        {
            var tracks = new List<TrackInfo>();
            await foreach (var track in _source.BrowseAsync(null))
                tracks.Add(track);

            _tracks.Clear();
            foreach (var track in tracks)
                _tracks[track.Id] = new LibraryItem(track, Save);
            Rebuild();

            foreach (var item in _tracks.Values.ToList())
                _ = FillDetailsAsync(item, analyzeIfMissing: true);
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            ShowImportResult(ex.Message, error: true);
        }
    }

    // ---------- Cargar en los platos ----------

    [RelayCommand]
    private Task LoadToA(LibraryEntry? entry) => LoadInto(_deckA, entry);

    [RelayCommand]
    private Task LoadToB(LibraryEntry? entry) => LoadInto(_deckB, entry);

    /// <summary>Doble clic: al plato que no está sonando (A si ninguno suena).</summary>
    [RelayCommand]
    private Task LoadToFree(LibraryEntry? entry) =>
        LoadInto(_deckA.IsPlaying && !_deckB.IsPlaying ? _deckB : _deckA, entry);

    private async Task LoadInto(DeckViewModel deck, LibraryEntry? entry)
    {
        if ((entry ?? Selected) is { } target)
            await deck.LoadTrackAsync(_source, _source.CreateTrackInfo(target.Track.Id));
    }

    // ---------- Importar ----------

    /// <summary>Importa el enlace escrito en el campo de la biblioteca.</summary>
    [RelayCommand]
    private async Task Import()
    {
        string text = ImportText.Trim();
        if (text.Length == 0)
            return;
        ImportText = "";
        await ImportAsync(text);
    }

    /// <summary>
    /// Importa un enlace: una canción o una lista entera (se descargan en segundo plano y van apareciendo).
    /// Si el enlace es una canción dentro de una lista, pregunta qué se quiere.
    /// </summary>
    public async Task ImportAsync(string text)
    {
        bool hasList = YouTubeUrl.TryGetPlaylistId(text, out string listId);
        bool hasVideo = YouTubeUrl.TryGetVideoId(text, out string videoId);
        if (!hasList && !hasVideo)
        {
            ShowImportResult(Loc.T("Err_NotYouTubeLink"), error: true);
            return;
        }
        if (hasList && hasVideo)
        {
            var answer = MessageBox.Show(Loc.T("Library_ImportWhich"), "DJTube", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel)
                return;
            hasList = answer == MessageBoxResult.Yes;
        }

        string url = hasList ? YouTubeUrl.ToPlaylistUrl(listId, hasVideo ? videoId : null) : YouTubeUrl.ToWatchUrl(videoId);
        await RunImportAsync(hasList ? listId : null, hasList ? null : videoId, url);
    }

    /// <summary>Vuelve a leer una lista y descarga las canciones nuevas.</summary>
    [RelayCommand]
    private Task UpdatePlaylist(LibraryGroup? group) =>
        group is { PlaylistId: { } id, Url: { } url } ? RunImportAsync(id, null, url) : Task.CompletedTask;

    [RelayCommand]
    private void CancelImport()
    {
        _importCts.Cancel();
        _importCts = new CancellationTokenSource();
    }

    private async Task RunImportAsync(string? playlistId, string? videoId, string url)
    {
        var ct = _importCts.Token;
        IsImporting = true;
        ImportHasError = false;
        Interlocked.Increment(ref _importsWaiting);
        if (_importGate.CurrentCount == 0)
            ImportStatus = Loc.T("Library_ImportQueued");
        try
        {
            await _importGate.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            FinishImport();
            return;
        }

        try
        {
            if (playlistId is null)
            {
                bool already = _source.IsCached(videoId!);
                ImportStatus = Loc.T("Library_DownloadingSong");
                ImportProgress = null;
                var track = await _source.EnsureDownloadedAsync(videoId!, new Progress<double>(p => ImportProgress = p), ct);
                ShowImportResult(Loc.F(already ? "Library_SongAlready" : "Library_SongAdded",
                    TrackTitleParser.Display(track.Artist, track.Title)), error: false);
                return;
            }

            ImportStatus = Loc.T("Library_ReadingList");
            ImportProgress = null;
            var playlist = await _source.GetPlaylistAsync(url, ct);
            int toDownload = playlist.Entries.Count(e => !_source.IsCached(e.Id));
            if (toDownload > LargePlaylist
                && MessageBox.Show(Loc.F("Library_LargePlaylist", playlist.Title, toDownload, toDownload * 4), "DJTube",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            {
                ShowImportResult(Loc.T("Library_ImportCancelled"), error: false);
                return;
            }
            var stored = _playlists.AddOrUpdate(playlistId, playlist.Title, url, playlist.Entries.Select(e => e.Id));
            var group = GroupFor(stored);
            group.IsExpanded = true;
            Rebuild();
            await DownloadAllAsync(playlist.Entries.Select(e => e.Id).ToList(), playlist.Title, ct);
        }
        catch (OperationCanceledException)
        {
            ShowImportResult(Loc.T("Library_ImportCancelled"), error: false);
        }
        catch (Exception ex)
        {
            ShowImportResult(Loc.F("Library_ImportError", ex.Message), error: true);
        }
        finally
        {
            _importGate.Release();
            FinishImport();
        }
    }

    private void FinishImport()
    {
        if (Interlocked.Decrement(ref _importsWaiting) == 0)
        {
            IsImporting = false;
            ImportProgress = null;
        }
    }

    /// <summary>Descarga las que falten, de dos en dos; las que fallen (borradas, bloqueadas…) se saltan.</summary>
    private async Task DownloadAllAsync(IReadOnlyList<string> ids, string label, CancellationToken ct)
    {
        var missing = ids.Where(id => !_source.IsCached(id)).ToList();
        int total = missing.Count, done = 0, failed = 0;
        string? lastError = null;

        void Report() => Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            ImportProgress = total == 0 ? 1 : done / (double)total;
            ImportStatus = Loc.F("Library_Downloading", label, done, total)
                + (failed > 0 ? " · " + Loc.F("Library_DownloadErrors", failed) : "");
        });

        Report();
        await Parallel.ForEachAsync(missing, new ParallelOptions { MaxDegreeOfParallelism = ParallelDownloads, CancellationToken = ct },
            async (id, token) =>
            {
                try
                {
                    await _source.EnsureDownloadedAsync(id, null, token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref failed);
                    lastError = ex.Message;
                }
                Interlocked.Increment(ref done);
                Report();
            });

        int added = total - failed;
        string result = Loc.F("Library_ImportDone", label, added);
        if (failed > 0)
            result += " · " + Loc.F("Library_DownloadErrors", failed) + (lastError is null ? "" : $" ({lastError})");
        ShowImportResult(result, error: failed > 0 && added == 0);
    }

    private void ShowImportResult(string text, bool error)
    {
        ImportStatus = text;
        ImportHasError = error;
    }

    [RelayCommand]
    private void DismissImportStatus()
    {
        if (!IsImporting)
            ImportStatus = "";
    }

    // ---------- Quitar ----------

    /// <summary>Quita la canción de la biblioteca (de todas las listas) y borra el audio.</summary>
    [RelayCommand]
    private void Delete(LibraryEntry? entry)
    {
        if ((entry ?? Selected)?.Track is not { } track)
            return;
        if (IsOnDeck(track.Id))
        {
            MessageBox.Show(Loc.F("Library_InUse", track.DisplayName), "DJTube", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(Loc.F("Library_DeleteConfirm", track.DisplayName), "DJTube",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        DeleteTrack(track.Id);
        Rebuild();
    }

    /// <summary>Saca la canción de una lista (sigue en la biblioteca).</summary>
    [RelayCommand]
    private void RemoveFromPlaylist(LibraryEntry? entry)
    {
        if ((entry ?? Selected) is not { Group: { PlaylistId: { } id } } target)
            return;
        var playlist = _playlists.Playlists.FirstOrDefault(p => p.Id == id);
        if (playlist is null)
            return;
        _playlists.AddOrUpdate(id, playlist.Title, playlist.Url, playlist.VideoIds.Where(v => v != target.Track.Id));
        Rebuild();
    }

    /// <summary>Quita la lista; sus canciones pasan a sueltas (o siguen en otras listas).</summary>
    [RelayCommand]
    private void RemovePlaylist(LibraryGroup? group)
    {
        if (group?.PlaylistId is not { } id)
            return;
        _playlists.Remove(id);
        _groups.Remove(id);
        Rebuild();
    }

    /// <summary>Quita la lista y borra las canciones que solo estaban en ella (no las cargadas en un plato).</summary>
    [RelayCommand]
    private void RemovePlaylistAndSongs(LibraryGroup? group)
    {
        if (group?.PlaylistId is not { } id || _playlists.Playlists.FirstOrDefault(p => p.Id == id) is not { } playlist)
            return;
        var onlyHere = playlist.VideoIds
            .Where(v => _tracks.ContainsKey(v) && _playlists.ListsContaining(v).Count == 1 && !IsOnDeck(v))
            .ToList();
        if (MessageBox.Show(Loc.F("Library_RemovePlaylistConfirm", playlist.Title, onlyHere.Count), "DJTube",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        _playlists.Remove(id);
        _groups.Remove(id);
        foreach (string videoId in onlyHere)
            DeleteTrack(videoId);
        Rebuild();
    }

    private bool IsOnDeck(string id) => _deckA.LoadedTrack?.Id == id || _deckB.LoadedTrack?.Id == id;

    private void DeleteTrack(string id)
    {
        if (_analyzer is CachingTrackAnalyzer cache)
            cache.Forget(_source.GetAudioPath(id));
        _source.Delete(id);
        _playlists.RemoveVideo(id);
        _tracks.Remove(id);
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CountText));
    }

    // ---------- Árbol ----------

    /// <summary>Rehace las filas a partir de las canciones y las listas (conserva qué ramas están abiertas).</summary>
    private void Rebuild()
    {
        var playlists = _playlists.Playlists.OrderBy(p => p.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        var entries = new List<LibraryEntry>();
        var inSomeList = new HashSet<string>();
        int order = 0;
        foreach (var playlist in playlists)
        {
            var group = GroupFor(playlist);
            group.Order = order++;
            int position = 0, count = 0;
            foreach (string id in playlist.VideoIds)
            {
                position++;
                if (!_tracks.TryGetValue(id, out var track))
                    continue;
                entries.Add(new LibraryEntry(track, group, position));
                inSomeList.Add(id);
                count++;
            }
            group.Count = count;
            group.Total = playlist.VideoIds.Count;
        }

        int loose = 0;
        foreach (var track in _tracks.Values.Where(t => !inSomeList.Contains(t.Id)))
        {
            entries.Add(new LibraryEntry(track, _loose, 0));
            loose++;
        }
        _loose.Count = loose;

        // (Sin DeferRefresh: la colección no se puede tocar mientras la vista tiene el refresco aplazado.)
        Entries.Clear();
        SetGrouping(playlists.Count > 0);
        foreach (var entry in entries)
            Entries.Add(entry);
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CountText));
    }

    private LibraryGroup GroupFor(LibraryPlaylist playlist)
    {
        if (!_groups.TryGetValue(playlist.Id, out var group))
        {
            group = new LibraryGroup(playlist.Id, playlist.Title, playlist.Url);
            _groups[playlist.Id] = group;
        }
        group.Title = playlist.Title;
        group.Url = playlist.Url;
        return group;
    }

    private void SetGrouping(bool grouped)
    {
        if (grouped == IsGrouped)
            return;
        View.GroupDescriptions.Clear();
        if (grouped)
            View.GroupDescriptions.Add(new PropertyGroupDescription(nameof(LibraryEntry.Group)));
        ApplyDefaultSort();
        OnPropertyChanged(nameof(IsGrouped));
    }

    /// <summary>Ramas en orden; dentro de cada lista, el orden de YouTube; las sueltas, por nombre.</summary>
    public void ApplyDefaultSort()
    {
        View.SortDescriptions.Clear();
        View.SortDescriptions.Add(new SortDescription("Group.Order", ListSortDirection.Ascending));
        View.SortDescriptions.Add(new SortDescription(nameof(LibraryEntry.Position), ListSortDirection.Ascending));
        View.SortDescriptions.Add(new SortDescription("Track.DisplayName", ListSortDirection.Ascending));
    }

    /// <summary>Ordenar por una columna sin deshacer el árbol (las ramas siguen en su sitio).</summary>
    public void SortBy(string memberPath, ListSortDirection direction)
    {
        using (View.DeferRefresh())
        {
            View.SortDescriptions.Clear();
            View.SortDescriptions.Add(new SortDescription("Group.Order", ListSortDirection.Ascending));
            View.SortDescriptions.Add(new SortDescription(memberPath, direction));
        } // aquí sí: solo cambian los criterios, no la colección
    }

    private void OnTrackAdded(TrackInfo track)
    {
        if (_tracks.TryGetValue(track.Id, out var existing))
        {
            existing.Reset(track);
            return;
        }
        var item = new LibraryItem(track, Save);
        _tracks[track.Id] = item;
        _ = FillDetailsAsync(item, analyzeIfMissing: true);

        // Se coloca en sus listas (o en sueltas) sin rehacer todo el árbol.
        var lists = _playlists.ListsContaining(track.Id);
        if (lists.Count == 0)
        {
            Entries.Add(new LibraryEntry(item, _loose, 0));
            _loose.Count++;
        }
        foreach (var playlist in lists)
        {
            var group = GroupFor(playlist);
            int position = playlist.VideoIds.ToList().IndexOf(track.Id) + 1;
            Entries.Add(new LibraryEntry(item, group, position));
            group.Count++;
        }
        if (lists.Count > 0 && !IsGrouped)
            Rebuild();
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CountText));
    }

    private void Save(LibraryItem item)
    {
        // Si el título queda vacío no se guarda y se vuelve a lo que había. La vista recoloca la fila
        // sola al confirmar la edición (aquí no se puede llamar a Refresh: la edición sigue abierta).
        var saved = _source.UpdateMetadata(item.Id, item.Artist, item.Title);
        item.Reset(saved ?? _source.CreateTrackInfo(item.Id));
    }

    // ---------- BPM y miniaturas ----------

    private void RefreshBpm(string? path)
    {
        if (path is null)
            return;
        var item = _tracks.Values.FirstOrDefault(i => string.Equals(_source.GetAudioPath(i.Id), path, StringComparison.OrdinalIgnoreCase));
        if (item is not null)
            item.Bpm = _analyzer.TryGetCachedGrid(path)?.Bpm;
    }

    private async Task FillDetailsAsync(LibraryItem item, bool analyzeIfMissing)
    {
        string audio = _source.GetAudioPath(item.Id);
        var (bpm, image) = await Task.Run(() => (_analyzer.TryGetCachedGrid(audio)?.Bpm, LoadThumbnail(Path.ChangeExtension(audio, ".jpg"))));
        item.Bpm = bpm;
        item.Artwork = image;
        if (bpm is null && analyzeIfMissing)
            QueueAnalysis(audio);
        if (image is null)
        {
            // Canciones que nunca se han visto en un plato: se baja la miniatura (y queda en la caché).
            string? downloaded = await _source.GetArtworkAsync(_source.CreateTrackInfo(item.Id));
            if (downloaded is not null)
                item.Artwork = await Task.Run(() => LoadThumbnail(downloaded));
        }
    }

    private void QueueAnalysis(string path)
    {
        lock (_analysisPending)
        {
            if (!_analysisPending.Add(path))
                return;
        }
        _analysisQueue.Writer.TryWrite(path);
        UpdateAnalysisStatus();
    }

    /// <summary>Una canción cada vez y sin prisa, para no quitar CPU mientras se pincha.</summary>
    private async Task RunAnalysisQueueAsync()
    {
        await foreach (string path in _analysisQueue.Reader.ReadAllAsync())
        {
            try
            {
                if (File.Exists(path))
                {
                    await _analyzer.AnalyzeAsync(path);
                    RefreshBpm(path);
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex);
            }
            lock (_analysisPending)
                _analysisPending.Remove(path);
            UpdateAnalysisStatus();
            await Task.Delay(200);
        }
    }

    private void UpdateAnalysisStatus()
    {
        int pending;
        lock (_analysisPending)
            pending = _analysisPending.Count;
        AnalysisStatus = pending > 0 ? Loc.F("Library_Analyzing", pending) : "";
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
            bitmap.DecodePixelWidth = 160;
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
        if (o is not LibraryEntry entry || string.IsNullOrWhiteSpace(SearchText))
            return true;
        var words = SearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string text = entry.Track.DisplayName;
        return words.All(w => text.Contains(w, StringComparison.CurrentCultureIgnoreCase));
    }
}
