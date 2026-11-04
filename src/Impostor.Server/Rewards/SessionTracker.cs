using System;
using System.Collections.Generic;
using System.Linq;

namespace Impostor.Server.Rewards
{
    /// <summary>A player as rewards resolves them: adapter namespace and subject.</summary>
    internal readonly record struct Identity(string Namespace, string Subject);

    /// <summary>One room as the server sees it right now. The id must be unique over time (it is the dedupe key).</summary>
    internal sealed record Room(string Id, IReadOnlyList<Identity> Players);

    /// <summary>The session clock as saved: when each player-in-room started, and who was already counted.</summary>
    internal sealed record ClockState(Dictionary<string, long> Since, List<string> Emitted);

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

        /// <summary>Gets the clock as plain data, for saving.</summary>
        /// <returns>The clock.</returns>
        public ClockState State() => new(
            _since.ToDictionary(kv => kv.Key, kv => kv.Value.ToUnixTimeMilliseconds()),
            _emitted.ToList());

        /// <summary>Loads a saved clock before the first Tick; keys of rooms that are gone drop at the next Tick.</summary>
        /// <param name="state">The saved clock.</param>
        public void Restore(ClockState state)
        {
            foreach (var kv in state.Since)
            {
                _since[kv.Key] = DateTimeOffset.FromUnixTimeMilliseconds(kv.Value);
            }

            foreach (var k in state.Emitted)
            {
                _emitted.Add(k);
            }
        }

        /// <summary>Whether a clock key (room NUL namespace NUL subject) belongs to one of the switch subjects.</summary>
        /// <param name="key">The clock key.</param>
        /// <param name="subjects">BAAS subjects.</param>
        /// <returns>True when the key is for one of them.</returns>
        public static bool KeyIsFor(string key, IEnumerable<string> subjects)
        {
            var parts = key.Split('\0');
            return parts.Length == 3 && parts[1] == "switch" && subjects.Contains(parts[2]);
        }

        /// <summary>Drops the clock entries of the subjects (account erasure).</summary>
        /// <param name="subjects">BAAS subjects.</param>
        public void Forget(IReadOnlyCollection<string> subjects)
        {
            foreach (var k in _since.Keys.Where(k => KeyIsFor(k, subjects)).ToList())
            {
                _since.Remove(k);
            }

            _emitted.RemoveWhere(k => KeyIsFor(k, subjects));
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
