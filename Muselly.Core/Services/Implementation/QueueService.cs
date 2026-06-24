using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;

namespace Muselly.Core.Services.Implementation;

/// <summary>
/// In-memory <see cref="IQueueService"/>. The queue list is the literal play order; navigation is
/// sequential through it. Enabling shuffle reorders the items after the current one; "shuffled" enqueue
/// operations shuffle just the block being added.
/// </summary>
public sealed class QueueService : IQueueService
{
    private readonly object _gate = new();
    private readonly Random _rng = new();
    private readonly List<Track> _items = new();
    private int _currentIndex = -1;
    private bool _shuffle;

    public IReadOnlyList<Track> Items => _items;

    public int CurrentIndex => _currentIndex;

    public Track? Current => _currentIndex >= 0 && _currentIndex < _items.Count ? _items[_currentIndex] : null;

    public RepeatMode RepeatMode { get; set; } = RepeatMode.Off;

    public bool Shuffle
    {
        get => _shuffle;
        set
        {
            if (_shuffle == value) return;
            _shuffle = value;
            if (value) ShuffleUpcoming();
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool HasNext => RepeatMode != RepeatMode.Off || _currentIndex < _items.Count - 1;

    public bool HasPrevious => RepeatMode != RepeatMode.Off || _currentIndex > 0;

    public event EventHandler? QueueChanged;
    public event EventHandler? CurrentChanged;

    public void PlayNow(IReadOnlyList<Track> tracks, int startIndex = 0)
    {
        lock (_gate)
        {
            _items.Clear();
            _items.AddRange(tracks);
            _currentIndex = _items.Count == 0 ? -1 : Math.Clamp(startIndex, 0, _items.Count - 1);
        }
        QueueChanged?.Invoke(this, EventArgs.Empty);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void PlayShuffled(IReadOnlyList<Track> tracks)
    {
        var shuffled = Shuffled(tracks);
        PlayNow(shuffled, 0);
    }

    public void Enqueue(IReadOnlyList<Track> tracks)
    {
        if (tracks.Count == 0) return;
        bool wasEmpty;
        lock (_gate)
        {
            wasEmpty = _items.Count == 0;
            _items.AddRange(tracks);
            if (_currentIndex < 0 && _items.Count > 0) _currentIndex = 0;
        }
        QueueChanged?.Invoke(this, EventArgs.Empty);
        if (wasEmpty) CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void EnqueueShuffled(IReadOnlyList<Track> tracks) => Enqueue(Shuffled(tracks));

    public void EnqueueNext(IReadOnlyList<Track> tracks)
    {
        if (tracks.Count == 0) return;
        lock (_gate)
        {
            var insertAt = _currentIndex < 0 ? _items.Count : _currentIndex + 1;
            _items.InsertRange(insertAt, tracks);
            if (_currentIndex < 0 && _items.Count > 0) _currentIndex = 0;
        }
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    public void InsertAt(int index, IReadOnlyList<Track> tracks)
    {
        if (tracks.Count == 0) return;
        lock (_gate)
        {
            var at = Math.Clamp(index, 0, _items.Count);
            _items.InsertRange(at, tracks);
            if (_currentIndex < 0) _currentIndex = 0;
            else if (at <= _currentIndex) _currentIndex += tracks.Count;
        }
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    public Track? MoveNext()
    {
        lock (_gate)
        {
            if (_items.Count == 0) return null;
            if (RepeatMode == RepeatMode.One) return Current;

            if (_currentIndex < _items.Count - 1) _currentIndex++;
            else if (RepeatMode == RepeatMode.All) _currentIndex = 0;
            else return null;
        }
        CurrentChanged?.Invoke(this, EventArgs.Empty);
        return Current;
    }

    public Track? PeekNext()
    {
        lock (_gate)
        {
            if (_items.Count == 0) return null;
            if (RepeatMode == RepeatMode.One) return Current;
            if (_currentIndex < _items.Count - 1) return _items[_currentIndex + 1];
            if (RepeatMode == RepeatMode.All) return _items[0];
            return null;
        }
    }

    public Track? MovePrevious()
    {
        lock (_gate)
        {
            if (_items.Count == 0) return null;

            if (_currentIndex > 0) _currentIndex--;
            else if (RepeatMode == RepeatMode.All) _currentIndex = _items.Count - 1;
            else return null;
        }
        CurrentChanged?.Invoke(this, EventArgs.Empty);
        return Current;
    }

    public void SetCurrentIndex(int index)
    {
        lock (_gate)
        {
            if (index < 0 || index >= _items.Count) return;
            _currentIndex = index;
        }
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveAt(int index)
    {
        bool currentChanged = false;
        lock (_gate)
        {
            if (index < 0 || index >= _items.Count) return;
            _items.RemoveAt(index);
            if (index < _currentIndex) { _currentIndex--; }
            else if (index == _currentIndex)
            {
                if (_currentIndex >= _items.Count) _currentIndex = _items.Count - 1;
                currentChanged = true;
            }
        }
        QueueChanged?.Invoke(this, EventArgs.Empty);
        if (currentChanged) CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Move(int fromIndex, int toIndex)
    {
        lock (_gate)
        {
            if (fromIndex < 0 || fromIndex >= _items.Count) return;
            if (toIndex < 0 || toIndex >= _items.Count) return;
            if (fromIndex == toIndex) return;

            var item = _items[fromIndex];
            _items.RemoveAt(fromIndex);
            _items.Insert(toIndex, item);

            // Keep the current pointer on the same track.
            if (fromIndex == _currentIndex) _currentIndex = toIndex;
            else if (fromIndex < _currentIndex && toIndex >= _currentIndex) _currentIndex--;
            else if (fromIndex > _currentIndex && toIndex <= _currentIndex) _currentIndex++;
        }
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _items.Clear();
            _currentIndex = -1;
        }
        QueueChanged?.Invoke(this, EventArgs.Empty);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ShuffleUpcoming()
    {
        lock (_gate)
        {
            var start = _currentIndex + 1;
            for (var i = _items.Count - 1; i > start; i--)
            {
                var j = _rng.Next(start, i + 1);
                (_items[i], _items[j]) = (_items[j], _items[i]);
            }
        }
    }

    private List<Track> Shuffled(IReadOnlyList<Track> tracks)
    {
        var list = new List<Track>(tracks);
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = _rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }
}
