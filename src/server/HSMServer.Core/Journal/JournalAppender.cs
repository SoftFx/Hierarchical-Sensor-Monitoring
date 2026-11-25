using HSMServer.Core.Model;
using HSMServer.Core.TableOfChanges;
using NLog;
using System;
using System.Collections.Generic;
using System.Threading;

namespace HSMServer.Core.Journal;

/// <summary>
/// The writer of the journal-visible-only records an entity gets from one configuration
/// write that changes it in several places — the chat settings (#1828) and the server
/// settings. One record per change, under the write's enviroment wording (the text the
/// operation tag derives from: "Create chat", "Remove chat", "Chat update"). No two
/// records of one entity ever share a storage key. Best-effort, as the journal's
/// durability rule requires (ADR-0010): a failure is logged and never fails the write
/// that already landed. What a record may carry is the caller's rule — a secret never
/// reaches a <see cref="Change"/>.
/// </summary>
public sealed class JournalAppender
{
    /// <summary>
    /// One changed setting: the journal's Property, Old value and New value cells. A secret
    /// setting (a webhook, the bot token) is named by the state of the setting instead of its
    /// value — see <see cref="OfSecret"/>.
    /// </summary>
    public readonly record struct Change(string Property, string OldValue, string NewValue)
    {
        /// <summary>
        /// Whether a secret setting changed between two values. An absent and an empty value
        /// are the same state.
        /// </summary>
        public static bool SecretChanged(string before, string after) =>
            IsSet(before) != IsSet(after) || (IsSet(after) && before != after);

        /// <summary>
        /// The change of a secret setting: the record reads as the state of the setting —
        /// not set, set, changed, removed — never as the old or the new value.
        /// </summary>
        public static Change OfSecret(string property, string before, string after)
        {
            var wasSet = IsSet(before);

            return new(property, wasSet ? "set" : "not set", !IsSet(after) ? "removed" : wasSet ? "changed" : "set");
        }

        private static bool IsSet(string value) => !string.IsNullOrEmpty(value);
    }

    // A wall clock this far BEHIND the last stamp stepped back; it starts a new time line
    // instead of being a slow clock.
    private static readonly long ReanchorAfter = TimeSpan.FromSeconds(5).Ticks;

    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly IJournalService _journal;
    private readonly TimeProvider _time;

    private long _lastStamp;


    public JournalAppender(IJournalService journal, TimeProvider time)
    {
        _journal = journal;
        _time = time ?? TimeProvider.System;
    }


    /// <summary>Appends one record per change; an empty list writes nothing.</summary>
    public void Write(Guid entityId, string kind, string enviroment, string path, InitiatorInfo initiator,
        IReadOnlyList<Change> changes)
    {
        if (changes is null || changes.Count == 0)
            return;

        try
        {
            var stamp = Reserve(changes.Count);

            for (var i = 0; i < changes.Count; i++)
                _journal.AddRecord(new JournalRecordModel(entityId, initiator, stamp + i)
                {
                    Enviroment = enviroment,
                    Kind = kind,
                    Path = path,
                    PropertyName = changes[i].Property,
                    OldValue = changes[i].OldValue,
                    NewValue = changes[i].NewValue,
                });
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, $"Cannot journal '{enviroment}' ({kind}) of entity '{entityId}': the write itself is unaffected.");
        }
    }

    // Reserves `records` successive ticks for one write. The storage key of a record is
    // (entity id, ticks, record type) and the append is a plain put, while the wall clock
    // keeps one value for about a millisecond on Windows: records written in a loop — in
    // one write or in two writes of one entity that follow each other — would land on the
    // same key and silently overwrite each other. So a reservation starts at the wall
    // clock, or one tick past the previous reservation when the clock has not moved that
    // far yet. Only a clock that stepped far back starts over from itself, so the stamps
    // never run ahead of real time for long.
    private long Reserve(int records)
    {
        while (true)
        {
            var last = Volatile.Read(ref _lastStamp);
            var now = _time.GetUtcNow().UtcTicks;
            var first = now > last || now < last - ReanchorAfter ? now : last + 1;

            if (Interlocked.CompareExchange(ref _lastStamp, first + records - 1, last) == last)
                return first;
        }
    }
}
