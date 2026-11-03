using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Impostor.Server.Rewards;
using Xunit;

namespace Impostor.Tests
{
    public class RewardsTests
    {
        private static readonly DateTimeOffset T0 = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
        private static readonly Identity A = new("switch", "aaaa");
        private static readonly Identity B = new("switch", "bbbb");

        [Fact]
        public void CountsAfterMinimumWithAnotherPlayer()
        {
            var t = new SessionTracker("game_sessions.among-us", TimeSpan.FromMinutes(3));
            var room = new Room("ABCDEF-1", new[] { A, B });
            Assert.Empty(t.Tick(T0, new[] { room }));
            Assert.Empty(t.Tick(T0.AddMinutes(2), new[] { room }));
            var ev = t.Tick(T0.AddMinutes(3), new[] { room });
            Assert.Equal(2, ev.Count);
            Assert.Equal("game_sessions.among-us:ABCDEF-1:switch:aaaa", ev[0].EventId);
            Assert.Empty(t.Tick(T0.AddMinutes(10), new[] { room })); // once per room
        }

        [Fact]
        public void AlonePlayerOrBreakResets()
        {
            var t = new SessionTracker("m", TimeSpan.FromMinutes(3));
            Assert.Empty(t.Tick(T0, new[] { new Room("R", new[] { A, B }) }));
            Assert.Empty(t.Tick(T0.AddMinutes(2), new[] { new Room("R", new[] { A }) }));
            Assert.Empty(t.Tick(T0.AddMinutes(4), new[] { new Room("R", new[] { A, B }) }));
            Assert.Empty(t.Tick(T0.AddMinutes(6), new[] { new Room("R", new[] { A, B }) }));
            Assert.Equal(2, t.Tick(T0.AddMinutes(7), new[] { new Room("R", new[] { A, B }) }).Count);
        }

        [Fact]
        public void DuplicateIdentitiesAreNotTwoPlayers()
        {
            var t = new SessionTracker("m", TimeSpan.Zero);
            Assert.Empty(t.Tick(T0, new[] { new Room("R", new[] { A, A }) }));
        }

        [Fact]
        public async Task EmitterPostsBatchWithKeyAndRetriesOn500()
        {
            var h = new Fake(_ => HttpStatusCode.OK);
            var e = new RewardsEmitter("http://rewards/", "k", "impostor", new HttpClient(h));
            e.Event(new SessionEvent("id1", "game_sessions.among-us", A, "R-1", T0));
            h.Status = HttpStatusCode.InternalServerError;
            Assert.Equal(0, await e.FlushAsync(T0, CancellationToken.None));
            Assert.Equal(1, e.Pending);
            Assert.Equal(0, await e.FlushAsync(T0.AddMilliseconds(500), CancellationToken.None)); // backing off
            h.Status = HttpStatusCode.OK;
            Assert.Equal(1, await e.FlushAsync(T0.AddSeconds(2), CancellationToken.None));
            var body = JsonDocument.Parse(h.Bodies[^1]).RootElement.GetProperty("events")[0];
            Assert.Equal("impostor", body.GetProperty("source").GetString());
            Assert.Equal("id1", body.GetProperty("event_id").GetString());
            Assert.Equal("switch", body.GetProperty("identity").GetProperty("namespace").GetString());
            Assert.Equal("R-1", body.GetProperty("keys")[0].GetString());
            Assert.Equal("http://rewards/internal/v1/events", h.Urls[^1]);
        }

        [Fact]
        public async Task DisabledEmitterDoesNothing()
        {
            var e = new RewardsEmitter(string.Empty, string.Empty, "impostor", new HttpClient(new Fake(_ => HttpStatusCode.OK)));
            e.Event(new SessionEvent("id1", "m", A, "R", T0));
            Assert.Equal(0, e.Pending);
            Assert.Equal(0, await e.FlushAsync(T0, CancellationToken.None));
        }

        [Fact]
        public async Task ResolverMapsKnownPuidAndRetriesUnknownLater()
        {
            var h = new Fake(_ => HttpStatusCode.NotFound);
            var r = new PuidResolver("http://eos:22621", "k", new HttpClient(h), TimeSpan.FromSeconds(60));
            Assert.Null(await r.ResolveAsync("p1", T0, CancellationToken.None));
            Assert.Null(await r.ResolveAsync("p1", T0.AddSeconds(10), CancellationToken.None));
            Assert.Single(h.Urls); // negative answer cached
            h.Status = HttpStatusCode.OK;
            h.Reply = "{\"baas_user_id\":\"f00d\"}";
            Assert.Equal("f00d", await r.ResolveAsync("p1", T0.AddSeconds(61), CancellationToken.None));
            Assert.Equal("f00d", await r.ResolveAsync("p1", T0.AddHours(5), CancellationToken.None));
            Assert.Equal(2, h.Urls.Count);
            Assert.EndsWith("/internal/v1/puid/p1", h.Urls[0]);
            Assert.Null(await r.ResolveAsync(string.Empty, T0, CancellationToken.None));
        }

        private sealed class Fake : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpStatusCode> _first;

            public Fake(Func<HttpRequestMessage, HttpStatusCode> first)
            {
                _first = first;
                Status = first(new HttpRequestMessage());
            }

            public HttpStatusCode Status { get; set; }

            public string Reply { get; set; } = "{}";

            public List<string> Urls { get; } = new();

            public List<string> Bodies { get; } = new();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Urls.Add(request.RequestUri!.ToString());
                Bodies.Add(request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
                return new HttpResponseMessage(Status) { Content = new StringContent(Reply) };
            }
        }
    }
}
