using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Impostor.Server.Rewards
{
    /// <summary>
    /// The reporter without the game: ticks room snapshots through the session clock, keeps the clock, the
    /// PUID map and the undelivered events in Postgres when a store is attached, and delivers to rewards.
    /// A store that cannot be reached is logged at error level and retried every 30 seconds; meanwhile the
    /// reporter runs on memory.
    /// </summary>
    internal sealed class RewardsCore
    {
        private readonly string _metric;
        private readonly SessionTracker _tracker;
        private readonly PuidResolver _resolver;
        private readonly RewardsEmitter _emitter;
        private readonly Func<CancellationToken, Task<StateStore>>? _open;
        private readonly ILogger? _logger;
        private StateStore? _store;
        private string _lastClock = string.Empty;
        private DateTimeOffset _nextTry = DateTimeOffset.MinValue;
        private DateTimeOffset _lastWarn = DateTimeOffset.MinValue;
        private bool _ticked;

        public RewardsCore(string metric, SessionTracker tracker, PuidResolver resolver, RewardsEmitter emitter, Func<CancellationToken, Task<StateStore>>? open, ILogger? logger)
        {
            _metric = metric;
            _tracker = tracker;
            _resolver = resolver;
            _emitter = emitter;
            _open = open;
            _logger = logger;
        }

        public async Task TickAsync(DateTimeOffset now, IEnumerable<Room> rooms, CancellationToken ct)
        {
            await PrepareAsync(now, ct);
            _ticked = true;
            var events = _tracker.Tick(now, rooms);
            if (_store != null && events.Count > 0)
            {
                try
                {
                    await _store.OutboxAddAsync(events, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Warn(now, ex, "saving counted sessions failed");
                }
            }

            foreach (var e in events)
            {
                _emitter.Event(e);
            }

            await _emitter.FlushAsync(now, ct);
            await SaveClockAsync(now, ct);
        }

        public async Task MapAsync(string puid, DateTimeOffset now, Action<string> onBaas, CancellationToken ct)
        {
            var baas = await _resolver.ResolveAsync(puid, now, ct);
            if (baas != null)
            {
                onBaas(baas);
            }
        }

        public async Task PrepareAsync(DateTimeOffset now, CancellationToken ct)
        {
            var erased = new List<string>();
            while (RewardsErasure.Pending.TryDequeue(out var subject))
            {
                erased.Add(subject);
            }

            if (erased.Count > 0)
            {
                _tracker.Forget(erased);
                _resolver.Forget(erased);
            }

            if (_store != null || _open == null || now < _nextTry)
            {
                return;
            }

            try
            {
                var store = await _open(ct);
                _resolver.Preload(await store.LoadPuidsAsync(ct), now);
                _resolver.OnResolved = (puid, baas) => store.UpsertPuidAsync(puid, baas, CancellationToken.None);
                _emitter.OnDelivered = ids => store.OutboxDeleteAsync(ids, CancellationToken.None);
                _emitter.Preload(await store.OutboxLoadAsync(ct));
                if (!_ticked)
                {
                    var saved = await store.LoadClockAsync(_metric, ct);
                    if (saved != null)
                    {
                        _tracker.Restore(saved);
                    }
                }

                _store = store;
                _logger?.LogInformation("Rewards state persisted in Postgres (session clock, PUID map, outbox)");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _nextTry = now + TimeSpan.FromSeconds(30);
                Warn(now, ex, "Postgres unavailable, running on memory and retrying every 30s");
            }
        }

        private async Task SaveClockAsync(DateTimeOffset now, CancellationToken ct)
        {
            if (_store == null)
            {
                return;
            }

            var state = _tracker.State();
            var json = System.Text.Json.JsonSerializer.Serialize(state);
            if (json == _lastClock)
            {
                return;
            }

            try
            {
                await _store.SaveClockAsync(_metric, state, ct);
                _lastClock = json;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Warn(now, ex, "saving the session clock failed (will retry)");
            }
        }

        private void Warn(DateTimeOffset now, Exception ex, string msg)
        {
            if (now - _lastWarn > TimeSpan.FromMinutes(1))
            {
                _lastWarn = now;
                _logger?.LogError("{Msg}: {Type}", msg, ex.GetType().Name);
            }
        }
    }
}
