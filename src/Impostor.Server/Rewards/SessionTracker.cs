using System;
using System.Collections.Generic;
using System.Linq;

namespace Impostor.Server.Rewards
{
    /// <summary>A player as rewards resolves them: adapter namespace and subject.</summary>
    internal readonly record struct Identity(string Namespace, string Subject);

    /// <summary>One room as the server sees it right now. The id must be unique over time (it is the dedupe key).</summary>
    internal sealed record Room(string Id, IReadOnlyList<Identity> Players);

    /// <summary>One counted session.</summary>
    internal sealed record SessionEvent(string EventId, string Metric, Identity Player, string RoomId, DateTimeOffset At);

    /// <summary>
    /// Turns room snapshots into session events: a player counts once per room after <c>min</c> of unbroken
    /// time in it together with at least one other player. Same semantics as OpenPak's rewards-emit
    /// Sessions (Go). Not thread safe: one ticker calls it.
    /// </summary>
    internal sealed class SessionTracker
    {
        private readonly string _metric;
        private readonly TimeSpan _min;
        private readonly Dictionary<string, DateTimeOffset> _since = new();
        private readonly HashSet<string> _emitted = new();

        public SessionTracker(string metric, TimeSpan min)
        {
            _metric = metric;
            _min = min;
        }

        public IReadOnlyList<SessionEvent> Tick(DateTimeOffset now, IEnumerable<Room> rooms)
        {
            var seen = new HashSet<string>();
            var events = new List<SessionEvent>();
            foreach (var room in rooms.OrderBy(r => r.Id, StringComparer.Ordinal))
            {
                var uniq = room.Players.Where(p => p.Namespace.Length > 0 && p.Subject.Length > 0).Distinct().ToList();
                if (uniq.Count < 2)
                {
                    continue;
                }

                foreach (var p in uniq)
                {
                    var key = room.Id + "\0" + p.Namespace + "\0" + p.Subject;
                    seen.Add(key);
                    if (!_since.ContainsKey(key))
                    {
                        _since[key] = now;
                    }

                    if (!_emitted.Contains(key) && now - _since[key] >= _min)
                    {
                        _emitted.Add(key);
                        events.Add(new SessionEvent($"{_metric}:{room.Id}:{p.Namespace}:{p.Subject}", _metric, p, room.Id, now));
                    }
                }
            }

            foreach (var k in _since.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                _since.Remove(k);
            }

            _emitted.RemoveWhere(k => !seen.Contains(k));
            return events;
        }
    }
}
