using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Impostor.Server.Rewards
{
    /// <summary>Which console ids (the "switch" link subjects, i.e. BAAS user ids) the core holds for an account.</summary>
    internal interface ILinkSource
    {
        Task<IReadOnlyList<string>> SwitchSubjectsAsync(string accountId, CancellationToken ct);
    }

    /// <summary>The durable side of an erase.</summary>
    internal interface IErasureStore
    {
        Task<int> EraseSubjectsAsync(IReadOnlyCollection<string> subjects, CancellationToken ct);
    }

    /// <summary>
    /// Subjects to purge from the running reporter's memory (PUID cache, session clock). The reporter ticks
    /// on one thread and drains this at the start of each tick.
    /// </summary>
    internal static class RewardsErasure
    {
        public static readonly ConcurrentQueue<string> Pending = new();
    }

    /// <summary>
    /// ListAccountLinks over gRPC without generated code (the one call needed): HTTP/2 cleartext to the
    /// account core, x-internal-key metadata, hand-encoded protobuf (account_id=1, namespace=2; response
    /// links=1 {namespace=2, subject_id=3}).
    /// </summary>
    internal sealed class CoreLinks : ILinkSource
    {
        private readonly HttpClient _http;
        private readonly string _addr;
        private readonly string _key;

        public CoreLinks(HttpClient http, string addr, string key)
        {
            _http = http;
            _addr = addr.Contains("://") ? addr.TrimEnd('/') : "http://" + addr.TrimEnd('/');
            _key = key;
        }

        public async Task<IReadOnlyList<string>> SwitchSubjectsAsync(string accountId, CancellationToken ct)
        {
            var msg = new List<byte>();
            AddString(msg, 1, accountId);
            AddString(msg, 2, "switch");
            var body = new byte[5 + msg.Count];
            var n = msg.Count;
            body[1] = (byte)(n >> 24);
            body[2] = (byte)(n >> 16);
            body[3] = (byte)(n >> 8);
            body[4] = (byte)n;
            msg.CopyTo(body, 5);

            using var req = new HttpRequestMessage(HttpMethod.Post, _addr + "/openpak.account.v1.Links/ListAccountLinks")
            {
                Version = HttpVersion.Version20,
                VersionPolicy = HttpVersionPolicy.RequestVersionExact,
                Content = new ByteArrayContent(body),
            };
            req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/grpc");
            req.Headers.TryAddWithoutValidation("te", "trailers");
            req.Headers.TryAddWithoutValidation("x-internal-key", _key);
            using var resp = await _http.SendAsync(req, ct);
            var data = await resp.Content.ReadAsByteArrayAsync(ct);
            var status = resp.TrailingHeaders.TryGetValues("grpc-status", out var t) ? t.First()
                : resp.Headers.TryGetValues("grpc-status", out var h) ? h.First() : null;
            if (resp.StatusCode != HttpStatusCode.OK || status != "0")
            {
                throw new InvalidOperationException("account core refused the link lookup (grpc-status " + (status ?? "none") + ")");
            }

            return ParseResponse(data);
        }

        internal static IReadOnlyList<string> ParseResponse(byte[] data)
        {
            var subjects = new List<string>();
            var pos = 0;
            while (pos + 5 <= data.Length)
            {
                var len = (data[pos + 1] << 24) | (data[pos + 2] << 16) | (data[pos + 3] << 8) | data[pos + 4];
                pos += 5;
                if (len < 0 || pos + len > data.Length)
                {
                    break;
                }

                foreach (var link in Fields(data, pos, len).Where(f => f.No == 1))
                {
                    string ns = string.Empty, subject = string.Empty;
                    foreach (var f in Fields(data, link.Start, link.Len))
                    {
                        if (f.No == 2)
                        {
                            ns = Encoding.UTF8.GetString(data, f.Start, f.Len);
                        }
                        else if (f.No == 3)
                        {
                            subject = Encoding.UTF8.GetString(data, f.Start, f.Len);
                        }
                    }

                    if (ns == "switch" && subject.Length > 0)
                    {
                        subjects.Add(subject);
                    }
                }

                pos += len;
            }

            return subjects;
        }

        private static IEnumerable<(int No, int Start, int Len)> Fields(byte[] d, int start, int len)
        {
            var p = start;
            var end = start + len;
            while (p < end)
            {
                var tag = Varint(d, ref p);
                if ((tag & 7) != 2)
                {
                    // Only length-delimited fields matter here; skip a varint, give up on anything else.
                    if ((tag & 7) == 0)
                    {
                        Varint(d, ref p);
                        continue;
                    }

                    yield break;
                }

                var l = (int)Varint(d, ref p);
                yield return ((int)(tag >> 3), p, l);
                p += l;
            }
        }

        private static ulong Varint(byte[] d, ref int p)
        {
            ulong v = 0;
            var shift = 0;
            while (p < d.Length)
            {
                var b = d[p++];
                v |= (ulong)(b & 0x7f) << shift;
                if ((b & 0x80) == 0)
                {
                    break;
                }

                shift += 7;
            }

            return v;
        }

        private static void AddString(List<byte> m, int no, string s)
        {
            var b = Encoding.UTF8.GetBytes(s);
            m.Add((byte)((no << 3) | 2));
            var l = (uint)b.Length;
            while (l >= 0x80)
            {
                m.Add((byte)(l | 0x80));
                l >>= 7;
            }

            m.Add((byte)l);
            m.AddRange(b);
        }
    }

    /// <summary>Account erasure (openpak/prds#120): everything Impostor keeps for an OpenPak account.</summary>
    internal sealed class AccountErasure
    {
        public const string Service = "impostor";
        private readonly ILinkSource _links;
        private readonly IErasureStore? _store;
        private readonly ILogger _logger;

        public AccountErasure(ILinkSource links, IErasureStore? store, ILogger logger)
        {
            _links = links;
            _store = store;
            _logger = logger;
        }

        public async Task<int> EraseAsync(string accountId, CancellationToken ct)
        {
            var subjects = (await _links.SwitchSubjectsAsync(accountId, ct)).Distinct().ToList();
            var rows = 0;
            if (_store != null && subjects.Count > 0)
            {
                rows = await _store.EraseSubjectsAsync(subjects, ct);
            }

            foreach (var s in subjects)
            {
                RewardsErasure.Pending.Enqueue(s);
            }

            _logger.LogInformation("account erased account={Account} rows={Rows} service={Service}", accountId, rows, Service);
            return rows;
        }

        public static bool KeyMatches(string? header, string key)
        {
            if (string.IsNullOrEmpty(key) || header == null || !header.StartsWith("Bearer ", StringComparison.Ordinal))
            {
                return false;
            }

            var got = SHA256.HashData(Encoding.UTF8.GetBytes(header.Substring(7)));
            var want = SHA256.HashData(Encoding.UTF8.GetBytes(key));
            return CryptographicOperations.FixedTimeEquals(got, want);
        }
    }
}
