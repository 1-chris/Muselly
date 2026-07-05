using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;
using Muselly.Core.Storage;

namespace Muselly.Persistence;

/// <summary>
/// SQLite-backed <see cref="ILibraryStore"/> for the desktop/headless/server heads. Stores the local library
/// in an indexed table for faster, lower-peak startup (rows are streamed and repeated strings interned) and
/// transactional bulk writes on rescans. On first use it migrates an existing <c>library.json</c> into the
/// database. Not used by the browser head (no native SQLite there — it keeps the JSON/in-memory path).
/// </summary>
public sealed class SqliteLibraryStore : ILibraryStore
{
    private const char GenreSeparator = '\u001f'; // unit separator — won't appear in genre text
    private readonly ILogger<SqliteLibraryStore> _logger;
    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly object _gate = new();
    private bool _initialized;

    public SqliteLibraryStore(ILogger<SqliteLibraryStore> logger)
    {
        _logger = logger;
        _dbPath = StoragePaths.LibraryDatabaseFile();
        _connectionString = new SqliteConnectionStringBuilder { DataSource = _dbPath }.ToString();
    }

    public Task InitializeAsync() => Task.Run(EnsureInitialized);

    /// <summary>Creates the schema (and migrates from JSON) exactly once. Called from every entry point so a
    /// query that races ahead of <see cref="InitializeAsync"/> on a fresh database still finds the table.</summary>
    private void EnsureInitialized()
    {
        if (_initialized) return;
        lock (_gate)
        {
            if (_initialized) return;
            using var conn = Open();
            CreateSchema(conn);
            MigrateFromJsonIfNeeded(conn);
            _initialized = true;
        }
    }

    public Task<List<Track>> GetAllTracksAsync() => Task.Run(() =>
    {
        EnsureInitialized();
        var tracks = new List<Track>();
        var pool = new Dictionary<string, string>(StringComparer.Ordinal); // de-dupe repeated strings

        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SelectColumns + " FROM tracks";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                tracks.Add(ReadTrack(reader, pool));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read the library database.");
        }
        return tracks;
    });

    public Task ReplaceAllAsync(IReadOnlyList<Track> tracks) => Task.Run(() =>
    {
        EnsureInitialized();
        lock (_gate)
        {
            try
            {
                using var conn = Open();
                using var tx = conn.BeginTransaction();

                using (var del = conn.CreateCommand())
                {
                    del.Transaction = tx;
                    del.CommandText = "DELETE FROM tracks";
                    del.ExecuteNonQuery();
                }

                using var insert = conn.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText = InsertSql;
                var p = PrepareInsertParameters(insert);

                foreach (var t in tracks)
                {
                    BindInsert(p, t);
                    insert.ExecuteNonQuery();
                }

                tx.Commit();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write the library database.");
            }
        }
    });

    public Task AppendAsync(IReadOnlyList<Track> tracks) => Task.Run(() =>
    {
        if (tracks.Count == 0) return;
        EnsureInitialized();
        lock (_gate)
        {
            try
            {
                using var conn = Open();
                using var tx = conn.BeginTransaction();
                using var insert = conn.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText = InsertSql;
                var p = PrepareInsertParameters(insert);
                foreach (var t in tracks)
                {
                    BindInsert(p, t);
                    insert.ExecuteNonQuery();
                }
                tx.Commit();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to append tracks to the library database.");
            }
        }
    });

    public Task SetAlbumArtworkAsync(string albumKey, string artworkPath) => Task.Run(() =>
    {
        EnsureInitialized();
        lock (_gate)
        {
            try
            {
                using var conn = Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE tracks SET ArtworkPath = $a WHERE AlbumKey = $k";
                AddParam(cmd, "$a", artworkPath);
                AddParam(cmd, "$k", albumKey);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { _logger.LogError(ex, "Artwork update failed."); }
        }
    });

    public int Count()
    {
        EnsureInitialized();
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM tracks";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
        catch (Exception ex) { _logger.LogError(ex, "Library count failed."); return 0; }
    }

    public Track? FindTrack(string id)
    {
        EnsureInitialized();
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SelectColumns + " FROM tracks WHERE Id = $id LIMIT 1";
            AddParam(cmd, "$id", id);
            using var reader = cmd.ExecuteReader();
            var pool = new Dictionary<string, string>(StringComparer.Ordinal);
            return reader.Read() ? ReadTrack(reader, pool) : null;
        }
        catch (Exception ex) { _logger.LogError(ex, "FindTrack failed."); return null; }
    }

    public IReadOnlyList<Track> GetAlbumTracks(string albumKey) =>
        QueryWhere("AlbumKey = $v ORDER BY DiscNumber, TrackNumber", albumKey);

    public IReadOnlyList<Track> GetArtistTracks(string artistKey) =>
        QueryWhere("ArtistKey = $v ORDER BY DiscNumber, TrackNumber", artistKey);

    public IReadOnlyList<Track> GetFolderTracks(string directory) =>
        QueryWhere("Directory = $v COLLATE NOCASE ORDER BY DiscNumber, TrackNumber", directory);

    public IReadOnlyList<Track> Search(string? query, int offset, int limit)
    {
        EnsureInitialized();
        var tracks = new List<Track>();
        var pool = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            var hasSearch = !string.IsNullOrWhiteSpace(query);
            cmd.CommandText = SelectColumns + " FROM tracks" +
                (hasSearch ? " WHERE Title LIKE $q OR Artist LIKE $q OR Album LIKE $q" : "") +
                " ORDER BY Title COLLATE NOCASE LIMIT $limit OFFSET $offset";
            if (hasSearch)
                AddParam(cmd, "$q", "%" + query!.Trim().Replace("%", "\\%").Replace("_", "\\_") + "%");
            AddParam(cmd, "$limit", Math.Max(0, limit));
            AddParam(cmd, "$offset", Math.Max(0, offset));
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) tracks.Add(ReadTrack(reader, pool));
        }
        catch (Exception ex) { _logger.LogError(ex, "Library search failed."); }
        return tracks;
    }

    private IReadOnlyList<Track> QueryWhere(string whereOrderBy, string value)
    {
        EnsureInitialized();
        var tracks = new List<Track>();
        var pool = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SelectColumns + " FROM tracks WHERE " + whereOrderBy;
            AddParam(cmd, "$v", value);
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) tracks.Add(ReadTrack(reader, pool));
        }
        catch (Exception ex) { _logger.LogError(ex, "Library query failed."); }
        return tracks;
    }

    private static void AddParam(SqliteCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    // --- DB plumbing ---------------------------------------------------------------------------------

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    private static void CreateSchema(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            CREATE TABLE IF NOT EXISTS tracks (
                Id TEXT PRIMARY KEY, Source TEXT NOT NULL, Title TEXT NOT NULL,
                Artist TEXT, AlbumArtist TEXT, Album TEXT, Composer TEXT, Genres TEXT, Label TEXT,
                Year INTEGER, TrackNumber INTEGER, TrackCount INTEGER, DiscNumber INTEGER, DiscCount INTEGER,
                DurationTicks INTEGER, Bitrate INTEGER, SampleRate INTEGER, Channels INTEGER,
                Codec TEXT, Extension TEXT, FileSizeBytes INTEGER, ArtworkPath TEXT, IsCompilation INTEGER,
                AlbumKey TEXT, ArtistKey TEXT, Directory TEXT, DateAddedMs INTEGER, FileModifiedMs INTEGER
            );
            CREATE INDEX IF NOT EXISTS idx_tracks_albumkey ON tracks(AlbumKey);
            CREATE INDEX IF NOT EXISTS idx_tracks_artistkey ON tracks(ArtistKey);
            CREATE INDEX IF NOT EXISTS idx_tracks_title ON tracks(Title COLLATE NOCASE);
            """;
        cmd.ExecuteNonQuery();
    }

    private void MigrateFromJsonIfNeeded(SqliteConnection conn)
    {
        // Only import the legacy JSON cache once: if it's still present (not yet renamed) and the DB is empty.
        var json = StoragePaths.LibraryCacheFile();
        if (!File.Exists(json)) return;

        using (var count = conn.CreateCommand())
        {
            count.CommandText = "SELECT COUNT(*) FROM tracks";
            if (Convert.ToInt64(count.ExecuteScalar()) > 0) return;
        }

        try
        {
            var snapshot = JsonStore.LoadStreaming(json, () => new LibrarySnapshot(), internStrings: true);
            if (snapshot.Tracks is { Count: > 0 } tracks)
            {
                using var tx = conn.BeginTransaction();
                using var insert = conn.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText = InsertSql;
                var p = PrepareInsertParameters(insert);
                foreach (var t in tracks) { BindInsert(p, t); insert.ExecuteNonQuery(); }
                tx.Commit();
                _logger.LogInformation("Migrated {Count} tracks from library.json into the database.", tracks.Count);
            }

            // Keep the old file as a backup so the import never repeats and the data isn't lost.
            File.Move(json, json + ".migrated", overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to migrate library.json into the database.");
        }
    }

    // --- Column mapping ------------------------------------------------------------------------------

    private const string SelectColumns =
        "SELECT Id,Source,Title,Artist,AlbumArtist,Album,Composer,Genres,Label,Year,TrackNumber,TrackCount," +
        "DiscNumber,DiscCount,DurationTicks,Bitrate,SampleRate,Channels,Codec,Extension,FileSizeBytes," +
        "ArtworkPath,IsCompilation,AlbumKey,ArtistKey,Directory,DateAddedMs,FileModifiedMs";

    private const string InsertSql =
        "INSERT OR REPLACE INTO tracks (Id,Source,Title,Artist,AlbumArtist,Album,Composer,Genres,Label,Year," +
        "TrackNumber,TrackCount,DiscNumber,DiscCount,DurationTicks,Bitrate,SampleRate,Channels,Codec,Extension," +
        "FileSizeBytes,ArtworkPath,IsCompilation,AlbumKey,ArtistKey,Directory,DateAddedMs,FileModifiedMs) " +
        "VALUES ($Id,$Source,$Title,$Artist,$AlbumArtist,$Album,$Composer,$Genres,$Label,$Year,$TrackNumber," +
        "$TrackCount,$DiscNumber,$DiscCount,$DurationTicks,$Bitrate,$SampleRate,$Channels,$Codec,$Extension," +
        "$FileSizeBytes,$ArtworkPath,$IsCompilation,$AlbumKey,$ArtistKey,$Directory,$DateAddedMs,$FileModifiedMs)";

    private static SqliteParameter[] PrepareInsertParameters(SqliteCommand cmd)
    {
        string[] names =
        {
            "$Id","$Source","$Title","$Artist","$AlbumArtist","$Album","$Composer","$Genres","$Label","$Year",
            "$TrackNumber","$TrackCount","$DiscNumber","$DiscCount","$DurationTicks","$Bitrate","$SampleRate",
            "$Channels","$Codec","$Extension","$FileSizeBytes","$ArtworkPath","$IsCompilation","$AlbumKey",
            "$ArtistKey","$Directory","$DateAddedMs","$FileModifiedMs"
        };
        var ps = new SqliteParameter[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            ps[i] = cmd.CreateParameter();
            ps[i].ParameterName = names[i];
            cmd.Parameters.Add(ps[i]);
        }
        return ps;
    }

    private static void BindInsert(SqliteParameter[] p, Track t)
    {
        p[0].Value = t.Id;
        p[1].Value = t.Source;
        p[2].Value = t.Title;
        p[3].Value = (object?)t.Artist ?? DBNull.Value;
        p[4].Value = (object?)t.AlbumArtist ?? DBNull.Value;
        p[5].Value = (object?)t.Album ?? DBNull.Value;
        p[6].Value = (object?)t.Composer ?? DBNull.Value;
        p[7].Value = (object?)(t.Genres.Count > 0 ? string.Join(GenreSeparator, t.Genres) : null) ?? DBNull.Value;
        p[8].Value = (object?)t.Label ?? DBNull.Value;
        p[9].Value = (long)t.Year;
        p[10].Value = (long)t.TrackNumber;
        p[11].Value = (long)t.TrackCount;
        p[12].Value = (long)t.DiscNumber;
        p[13].Value = (long)t.DiscCount;
        p[14].Value = t.Duration.Ticks;
        p[15].Value = t.Bitrate;
        p[16].Value = t.SampleRate;
        p[17].Value = t.Channels;
        p[18].Value = (object?)t.Codec ?? DBNull.Value;
        p[19].Value = t.Extension;
        p[20].Value = t.FileSizeBytes;
        p[21].Value = (object?)t.ArtworkPath ?? DBNull.Value;
        p[22].Value = t.IsCompilation ? 1L : 0L;
        p[23].Value = t.AlbumKey;
        p[24].Value = t.ArtistKey;
        p[25].Value = t.Directory;
        p[26].Value = t.DateAdded.ToUnixTimeMilliseconds();
        p[27].Value = t.FileModified.ToUnixTimeMilliseconds();
    }

    private static Track ReadTrack(SqliteDataReader r, Dictionary<string, string> pool)
    {
        string? Intern(int i)
        {
            if (r.IsDBNull(i)) return null;
            var s = r.GetString(i);
            if (pool.TryGetValue(s, out var e)) return e;
            pool[s] = s;
            return s;
        }

        var genresRaw = Intern(7);
        IReadOnlyList<string> genres = string.IsNullOrEmpty(genresRaw)
            ? Array.Empty<string>()
            : genresRaw.Split(GenreSeparator, StringSplitOptions.RemoveEmptyEntries);

        return new Track
        {
            Id = r.GetString(0),
            Source = r.GetString(1),
            Title = r.GetString(2),
            Artist = Intern(3),
            AlbumArtist = Intern(4),
            Album = Intern(5),
            Composer = Intern(6),
            Genres = genres,
            Label = Intern(8),
            Year = (uint)r.GetInt64(9),
            TrackNumber = (uint)r.GetInt64(10),
            TrackCount = (uint)r.GetInt64(11),
            DiscNumber = (uint)r.GetInt64(12),
            DiscCount = (uint)r.GetInt64(13),
            Duration = TimeSpan.FromTicks(r.GetInt64(14)),
            Bitrate = (int)r.GetInt64(15),
            SampleRate = (int)r.GetInt64(16),
            Channels = (int)r.GetInt64(17),
            Codec = Intern(18),
            Extension = Intern(19) ?? string.Empty,
            FileSizeBytes = r.GetInt64(20),
            ArtworkPath = Intern(21),
            IsCompilation = r.GetInt64(22) != 0,
            AlbumKey = Intern(23) ?? string.Empty,
            ArtistKey = Intern(24) ?? string.Empty,
            Directory = Intern(25) ?? string.Empty,
            DateAdded = DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(26)),
            FileModified = DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(27))
        };
    }
}
