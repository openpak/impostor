using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Impostor.Server.Rewards;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Impostor.Tests
{
    public class ErasureTests
    {
        private const string Acct = "11111111-1111-1111-1111-111111111111";
        private static readonly DateTimeOffset T0 = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

        private sealed class Links : ILinkSource
        {
            public Task<IReadOnlyList<string>> SwitchSubjectsAsync(string accountId, CancellationToken ct)
                => Task.FromResult<IReadOnlyList<string>>(accountId == Acct ? new[] { "aaaa" } : Array.Empty<string>());
        }

        private sealed class Store : IErasureStore
        {
            // subject -> rows still held; a second erase finds none.
            public Dictionary<string, int> Held = new() { ["aaaa"] = 3, ["bbbb"] = 2 };

            public Task<int> EraseSubjectsAsync(IReadOnlyCollection<string> subjects, CancellationToken ct)
            {
                var n = 0;
                foreach (var s in subjects)
                {
                    if (Held.Remove(s, out var c))
                    {
                        n += c;
                    }
                }

                return Task.FromResult(n);
            }
        }

        [Fact]
        public async Task EraseRemovesOnlyThatAccountAndIsIdempotent()
        {
            var store = new Store();
            var e = new AccountErasure(new Links(), store, NullLogger.Instance);
            Assert.Equal(3, await e.EraseAsync(Acct, CancellationToken.None));
            Assert.True(store.Held.ContainsKey("bbbb"));
            Assert.Equal(0, await e.EraseAsync(Acct, CancellationToken.None));
        }

        [Fact]
        public async Task EraseForgetsReporterMemory()
        {
            while (RewardsErasure.Pending.TryDequeue(out _))
            {
            }

            var tracker = new SessionTracker("m", TimeSpan.Zero);
            var a = new Identity("switch", "aaaa");
            var b = new Identity("switch", "bbbb");
            tracker.Tick(T0, new[] { new Room("R", new[] { a, b }) });
            await new AccountErasure(new Links(), null, NullLogger.Instance).EraseAsync(Acct, CancellationToken.None);
            var drained = new List<string>();
            while (RewardsErasure.Pending.TryDequeue(out var s))
            {
                drained.Add(s);
            }

            tracker.Forget(drained);
            Assert.DoesNotContain(tracker.State().Since.Keys, k => k.EndsWith("\0aaaa"));
            Assert.Contains(tracker.State().Since.Keys, k => k.EndsWith("\0bbbb"));
        }

        [Fact]
        public void KeyCheck()
        {
            Assert.False(AccountErasure.KeyMatches("Bearer k", string.Empty));
            Assert.False(AccountErasure.KeyMatches(null, "k"));
            Assert.False(AccountErasure.KeyMatches(string.Empty, "k"));
            Assert.False(AccountErasure.KeyMatches("Bearer wrong", "k"));
            Assert.False(AccountErasure.KeyMatches("k", "k"));
            Assert.True(AccountErasure.KeyMatches("Bearer k", "k"));
        }

        [Fact]
        public void ParsesLinksResponse()
        {
            static byte[] Str(int no, string s) => new[] { (byte)((no << 3) | 2), (byte)s.Length }.Concat(Encoding.UTF8.GetBytes(s)).ToArray();
            byte[] Link(string ns, string sub) => Str(2, ns).Concat(Str(3, sub)).ToArray();
            var l1 = Link("switch", "aaaa");
            var l2 = Link("nex", "77");
            var msg = new[] { (byte)0x0a, (byte)l1.Length }.Concat(l1).Concat(new[] { (byte)0x0a, (byte)l2.Length }).Concat(l2).ToArray();
            var framed = new byte[] { 0, 0, 0, 0, (byte)msg.Length }.Concat(msg).ToArray();
            Assert.Equal(new[] { "aaaa" }, CoreLinks.ParseResponse(framed));
        }

        // Against a real Postgres when IMPOSTOR_TEST_DATABASE_URL is set.
        [Fact]
        public async Task StoreEraseAgainstPostgres()
        {
            var url = Environment.GetEnvironmentVariable("IMPOSTOR_TEST_DATABASE_URL");
            if (string.IsNullOrEmpty(url))
            {
                return;
            }

            var st = await StateStore.OpenAsync(url, CancellationToken.None);
            await using (var c = new Npgsql.NpgsqlConnection(StateStore.ConnectionString(url)))
            {
                await c.OpenAsync();
                await new Npgsql.NpgsqlCommand("TRUNCATE impostor_session_clock, impostor_puid, impostor_outbox", c).ExecuteNonQueryAsync();
            }

            await st.UpsertPuidAsync("p1", "aaaa", CancellationToken.None);
            await st.UpsertPuidAsync("p2", "bbbb", CancellationToken.None);
            await st.OutboxAddAsync(
                new[]
                {
                    new SessionEvent("e1", "m", new Identity("switch", "aaaa"), "R", T0),
                    new SessionEvent("e2", "m", new Identity("switch", "bbbb"), "R", T0),
                },
                CancellationToken.None);
            await st.SaveClockAsync("m", new ClockState(new() { ["R\0switch\0aaaa"] = 1, ["R\0switch\0bbbb"] = 1 }, new() { "R\0switch\0aaaa" }), CancellationToken.None);

            Assert.Equal(4, await st.EraseSubjectsAsync(new[] { "aaaa" }, CancellationToken.None)); // puid + outbox + since + emitted
            Assert.Equal(0, await st.EraseSubjectsAsync(new[] { "aaaa" }, CancellationToken.None));
            Assert.Single(await st.LoadPuidsAsync(CancellationToken.None));
            Assert.Single(await st.OutboxLoadAsync(CancellationToken.None));
            var clock = await st.LoadClockAsync("m", CancellationToken.None);
            Assert.Single(clock!.Since);
            Assert.Empty(clock.Emitted);
            await st.DisposeAsync();
        }
    }
}
