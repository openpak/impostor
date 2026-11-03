using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Impostor.Server.Rewards
{
    /// <summary>
    /// Asks OpenPak's eosd which BAAS account a product user id belongs to
    /// (GET /internal/v1/puid/{puid}, X-Internal-Key). A PUID eosd never issued (another platform, or
    /// eosd restarted since the login) is unknown and is retried after <c>retry</c>.
    /// </summary>
    internal sealed class PuidResolver
    {
        private readonly string _url;
        private readonly string _key;
        private readonly HttpClient _http;
        private readonly TimeSpan _retry;
        private readonly ConcurrentDictionary<string, (string? Baas, DateTimeOffset At)> _cache = new();

        public PuidResolver(string url, string key, HttpClient http, TimeSpan retry)
        {
            _url = url.TrimEnd('/');
            _key = key;
            _http = http;
            _retry = retry;
        }

        /// <summary>Gets or sets a callback told every PUID that resolved (to persist it).</summary>
        public Func<string, string, Task>? OnResolved { get; set; }

        /// <summary>Loads mappings saved earlier. They are served until eosd answers differently.</summary>
        /// <param name="known">PUID to BAAS user id.</param>
        /// <param name="now">The time they count as seen.</param>
        public void Preload(IReadOnlyDictionary<string, string> known, DateTimeOffset now)
        {
            foreach (var kv in known)
            {
                _cache[kv.Key] = (kv.Value, now);
            }
        }

        public async Task<string?> ResolveAsync(string puid, DateTimeOffset now, CancellationToken ct)
        {
            if (puid.Length == 0 || _url.Length == 0 || _key.Length == 0)
            {
                return null;
            }

            if (_cache.TryGetValue(puid, out var hit) && (hit.Baas != null || now - hit.At < _retry))
            {
                return hit.Baas;
            }

            string? baas = null;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, _url + "/internal/v1/puid/" + Uri.EscapeDataString(puid));
                req.Headers.Add("X-Internal-Key", _key);
                using var resp = await _http.SendAsync(req, ct);
                if (resp.StatusCode == HttpStatusCode.OK)
                {
                    using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                    if (doc.RootElement.TryGetProperty("baas_user_id", out var v))
                    {
                        baas = v.GetString();
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
            }

            if (!string.IsNullOrEmpty(baas) && OnResolved != null)
            {
                try
                {
                    await OnResolved(puid, baas);
                }
                catch (Exception)
                {
                    // the database being down must not stop the mapping; the next lookup of this PUID retries
                    _cache.TryRemove(puid, out _);
                    return baas;
                }
            }

            _cache[puid] = (string.IsNullOrEmpty(baas) ? null : baas, now);
            return string.IsNullOrEmpty(baas) ? null : baas;
        }
    }
}
