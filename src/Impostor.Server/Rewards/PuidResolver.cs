using System;
using System.Collections.Concurrent;
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

            _cache[puid] = (string.IsNullOrEmpty(baas) ? null : baas, now);
            return string.IsNullOrEmpty(baas) ? null : baas;
        }
    }
}
