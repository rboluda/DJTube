using System.Text.Json;

namespace DJTube.Core.Library;

/// <summary>Una lista importada: su nombre, de dónde viene y sus canciones (IDs de vídeo, en orden).</summary>
public sealed record LibraryPlaylist(string Id, string Title, string Url, IReadOnlyList<string> VideoIds, DateTime Added);

/// <summary>
/// Listas de la biblioteca, guardadas en un JSON. Las canciones siguen en la caché de YouTube: aquí
/// solo se recuerda a qué listas pertenece cada una (una canción puede estar en varias).
/// </summary>
/// <remarks>Se escribe de forma atómica (archivo temporal + renombrar): un corte no lo deja a medias.</remarks>
public sealed class PlaylistStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly Lock _sync = new();
    private List<LibraryPlaylist> _playlists;

    public PlaylistStore(string path)
    {
        _path = path;
        _playlists = Load(path);
    }

    public IReadOnlyList<LibraryPlaylist> Playlists
    {
        get { lock (_sync) return _playlists.ToList(); }
    }

    /// <summary>Listas que contienen un vídeo.</summary>
    public IReadOnlyList<LibraryPlaylist> ListsContaining(string videoId)
    {
        lock (_sync)
            return _playlists.Where(p => p.VideoIds.Contains(videoId)).ToList();
    }

    /// <summary>Añade una lista o, si ya estaba, actualiza su nombre y su contenido.</summary>
    public LibraryPlaylist AddOrUpdate(string id, string title, string url, IEnumerable<string> videoIds)
    {
        lock (_sync)
        {
            var ids = videoIds.Distinct().ToList();
            int index = _playlists.FindIndex(p => p.Id == id);
            var playlist = index >= 0
                ? _playlists[index] with { Title = title, Url = url, VideoIds = ids }
                : new LibraryPlaylist(id, title, url, ids, DateTime.Now);
            if (index >= 0)
                _playlists[index] = playlist;
            else
                _playlists.Add(playlist);
            SaveNoLock();
            return playlist;
        }
    }

    public void Rename(string id, string title)
    {
        lock (_sync)
        {
            int index = _playlists.FindIndex(p => p.Id == id);
            if (index < 0 || string.IsNullOrWhiteSpace(title))
                return;
            _playlists[index] = _playlists[index] with { Title = title.Trim() };
            SaveNoLock();
        }
    }

    /// <summary>Quita la lista (las canciones siguen en la biblioteca).</summary>
    public void Remove(string id)
    {
        lock (_sync)
        {
            if (_playlists.RemoveAll(p => p.Id == id) > 0)
                SaveNoLock();
        }
    }

    /// <summary>Una canción borrada de la biblioteca sale de todas las listas.</summary>
    public void RemoveVideo(string videoId)
    {
        lock (_sync)
        {
            bool changed = false;
            for (int i = 0; i < _playlists.Count; i++)
            {
                if (_playlists[i].VideoIds.Contains(videoId))
                {
                    _playlists[i] = _playlists[i] with { VideoIds = _playlists[i].VideoIds.Where(v => v != videoId).ToList() };
                    changed = true;
                }
            }
            if (changed)
                SaveNoLock();
        }
    }

    private static List<LibraryPlaylist> Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<List<LibraryPlaylist>>(File.ReadAllText(path), JsonOptions)?
                    .Where(p => p is { Id: not null, Title: not null, VideoIds: not null })
                    .ToList() ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Archivo dañado o ilegible: se empieza sin listas (las canciones siguen en la caché).
        }
        return [];
    }

    private void SaveNoLock()
    {
        string? folder = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);
        string tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_playlists, JsonOptions));
        File.Move(tmp, _path, overwrite: true);
    }
}
