using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Impostor.Server.Rewards
{
    /// <summary>
    /// Delivers events to the rewards service (POST /internal/v1/events), batched, retried with backoff,
    /// dropped when old. It never blocks the game: a full queue drops the new event. Disabled when the URL
    /// or key is empty.
    /// </summary>
    internal sealed class RewardsEmitter
    {
        private const int Batch = 50;
        private const int QueueSize = 2048;
        private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

        private readonly string _url;
        private readonly string _key;
        private readonly string _source;
        private readonly HttpClient _http;
        private readonly ILogger? _logger;
        private readonly ConcurrentQueue<SessionEvent> _queue = new();
        private TimeSpan _backoff = TimeSpan.Zero;
        private DateTimeOffset _next = DateTimeOffset.MinValue;

        public RewardsEmitter(string url, string key, string source, HttpClient http, ILogger? logger = null)
        {
            _url = url.TrimEnd('/');
            _key = key;
            _source = source;
            _http = http;
            _logger = logger;
        }

        public bool Enabled => _url.Length > 0 && _key.Length > 0;

        public int Pending => _queue.Count;

        /// <summary>Gets or sets a callback told the ids of events rewards accepted (to drop them from the outbox).</summary>
        public Func<IReadOnlyList<string>, Task>? OnDelivered { get; set; }

        public void Event(SessionEvent e)
        {
            if (Enabled && _queue.Count < QueueSize)
            {
                _queue.Enqueue(e);
            }
        }

        /// <summary>Sends what is due. Called from the ticker; returns the number delivered.</summary>
        public async Task<int> FlushAsync(DateTimeOffset now, CancellationToken ct)
        {
            if (!Enabled || now < _next)
            {
                return 0;
            }

            var sent = 0;
            var expired = new List<string>();
            while (!_queue.IsEmpty)
            {
                var batch = new List<SessionEvent>();
                while (batch.Count < Batch && _queue.TryPeek(out var e))
                {
                    if (now - e.At > MaxAge)
                    {
                        _queue.TryDequeue(out _);
                        expired.Add(e.EventId);
                        continue;
                    }

                    batch.Add(e);
                    _queue.TryDequeue(out _);
                }

                if (batch.Count == 0)
                {
                    break;
                }

                if (await PostAsync(batch, ct))
                {
                    sent += batch.Count;
                    if (OnDelivered != null)
                    {
                        try
                        {
                            await OnDelivered(batch.Select(e => e.EventId).ToList());
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, "rewards: outbox cleanup failed (events are deduplicated, harmless)");
                        }
                    }

                    _backoff = TimeSpan.Zero;
                    continue;
                }

                foreach (var e in batch)
                {
                    _queue.Enqueue(e); // order is not significant; the event id dedupes
                }

                _backoff = TimeSpan.FromSeconds(Math.Min(Math.Max(_backoff.TotalSeconds * 2, 1), 60));
                _next = now + _backoff;
                break;
            }

            if (expired.Count > 0 && OnDelivered != null)
            {
                await OnDelivered(expired);
            }

            return sent;
        }

        /// <summary>Queues events saved in the outbox by an earlier run.</summary>
        /// <param name="events">The saved events.</param>
        public void Preload(IEnumerable<SessionEvent> events)
        {
            foreach (var e in events)
            {
                Event(e);
            }
        }

        private async Task<bool> PostAsync(List<SessionEvent> batch, CancellationToken ct)
        {
            var body = JsonSerializer.Serialize(new
            {
                events = batch.Select(e => new
                {
                    event_id = e.EventId,
                    source = _source,
                    metric = e.Metric,
                    identity = new { @namespace = e.Player.Namespace, subject = e.Player.Subject },
                    keys = new[] { e.RoomId },
                    occurred_at = e.At.UtcDateTime,
                }),
            });
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, _url + "/internal/v1/events")
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                };
                req.Headers.Add("X-Internal-Key", _key);
                using var resp = await _http.SendAsync(req, ct);
                var code = (int)resp.StatusCode;
                if (code == 200)
                {
                    return true;
                }

                if (code == 429 || code >= 500)
                {
                    return false;
                }

                _logger?.LogWarning("rewards: batch of {Count} refused with {Code}; dropped", batch.Count, code);
                return true;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return false;
            }
        }
    }
}
