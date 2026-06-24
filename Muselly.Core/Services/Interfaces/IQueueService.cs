using Muselly.Core.Models;

namespace Muselly.Core.Services.Interfaces;

/// <summary>
/// The play queue: the ordered list of tracks that will play, plus the current position within it. The
/// queue's order is literal (the queue UI shows exactly this order); shuffle operations reorder the
/// upcoming items in place rather than hiding a separate sequence.
/// </summary>
public interface IQueueService
{
    IReadOnlyList<Track> Items { get; }

    int CurrentIndex { get; }

    Track? Current { get; }

    RepeatMode RepeatMode { get; set; }

    bool Shuffle { get; set; }

    bool HasNext { get; }

    bool HasPrevious { get; }

    /// <summary>Raised when the queue contents or order change.</summary>
    event EventHandler? QueueChanged;

    /// <summary>Raised when the current item (or index) changes.</summary>
    event EventHandler? CurrentChanged;

    /// <summary>Replaces the queue with the given tracks and starts at <paramref name="startIndex"/>.</summary>
    void PlayNow(IReadOnlyList<Track> tracks, int startIndex = 0);

    /// <summary>Replaces the queue with the given tracks, shuffled, starting at the top.</summary>
    void PlayShuffled(IReadOnlyList<Track> tracks);

    /// <summary>Appends tracks to the end of the queue.</summary>
    void Enqueue(IReadOnlyList<Track> tracks);

    /// <summary>Appends tracks, shuffled, to the end of the queue.</summary>
    void EnqueueShuffled(IReadOnlyList<Track> tracks);

    /// <summary>Inserts tracks immediately after the current item (play next).</summary>
    void EnqueueNext(IReadOnlyList<Track> tracks);

    /// <summary>Inserts tracks at an explicit position (used by drag-and-drop into the queue).</summary>
    void InsertAt(int index, IReadOnlyList<Track> tracks);

    /// <summary>Advances to and returns the next track per repeat/shuffle rules, or null at the end.</summary>
    Track? MoveNext();

    /// <summary>Returns the track <see cref="MoveNext"/> would play next, without advancing (for pre-buffering).</summary>
    Track? PeekNext();

    /// <summary>Steps back to and returns the previous track, or null at the start.</summary>
    Track? MovePrevious();

    void SetCurrentIndex(int index);

    void RemoveAt(int index);

    void Move(int fromIndex, int toIndex);

    void Clear();
}
