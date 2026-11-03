using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Impostor.Api.Games.Managers;
using Impostor.Server.Net.Inner.Objects;
using Impostor.Server.Net.State;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Impostor.Server.Rewards
{
    /// <summary>
    /// Reports Among Us online sessions (<c>game_sessions.among-us</c>) to OpenPak rewards. Every 20 seconds
    /// it snapshots the rooms, maps each player's EOS product user id to a BAAS account through eosd, and
    /// feeds <see cref="SessionTracker"/>. Idle unless REWARDS_EVENTS_URL, REWARDS_EVENTS_KEY, EOS_INTERNAL_URL
    /// and EOS_INTERNAL_KEY are all set. See docs/deploy.md.
    /// </summary>
    internal sealed class RewardsService : BackgroundService
    {
        private const string Metric = "game_sessions.among-us";

        private readonly IGameManager _games;
        private readonly ILogger<RewardsService> _logger;
        private readonly Dictionary<string, DateTimeOffset> _roomStart = new();

        public RewardsService(IGameManager games, ILogger<RewardsService> logger)
        {
            _games = games;
            _logger = logger;
        }

        public static TimeSpan SessionMin()
        {
            return TimeSpan.TryParse(Environment.GetEnvironmentVariable("REWARDS_SESSION_MIN"), out var d) && d > TimeSpan.Zero
                ? d
                : TimeSpan.FromMinutes(3);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var url = Environment.GetEnvironmentVariable("REWARDS_EVENTS_URL") ?? string.Empty;
            var key = Environment.GetEnvironmentVariable("REWARDS_EVENTS_KEY") ?? string.Empty;
            var eosUrl = Environment.GetEnvironmentVariable("EOS_INTERNAL_URL") ?? string.Empty;
            var eosKey = Environment.GetEnvironmentVariable("EOS_INTERNAL_KEY") ?? string.Empty;
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var emitter = new RewardsEmitter(url, key, "impostor", http, _logger);
            if (!emitter.Enabled || eosUrl.Length == 0 || eosKey.Length == 0)
            {
                _logger.LogInformation("Rewards events are off (REWARDS_EVENTS_URL/KEY or EOS_INTERNAL_URL/KEY unset)");
                return;
            }

            var resolver = new PuidResolver(eosUrl, eosKey, http, TimeSpan.FromSeconds(60));
            var tracker = new SessionTracker(Metric, SessionMin());
            _logger.LogInformation("Rewards events are on: {Metric}", Metric);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
            do
            {
                try
                {
                    var now = DateTimeOffset.UtcNow;
                    var rooms = await SnapshotAsync(resolver, now, stoppingToken);
                    foreach (var e in tracker.Tick(now, rooms))
                    {
                        emitter.Event(e);
                    }

                    await emitter.FlushAsync(now, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Rewards tick failed");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }

        private async Task<List<Room>> SnapshotAsync(PuidResolver resolver, DateTimeOffset now, CancellationToken ct)
        {
            var rooms = new List<Room>();
            var live = new HashSet<string>();
            foreach (var game in _games.Games.OfType<Game>())
            {
                var code = game.Code.Code;
                live.Add(code);
                if (!_roomStart.ContainsKey(code))
                {
                    _roomStart[code] = now; // a code is reused after the room ends: the start time keeps ids unique
                }

                var players = new List<Identity>();
                foreach (var cp in game.Players)
                {
                    var puid = (cp.Character as InnerPlayerControl)?.PlayerInfo?.ProductUserId ?? string.Empty;
                    var baas = await resolver.ResolveAsync(puid, now, ct);
                    if (baas != null)
                    {
                        players.Add(new Identity("switch", baas));
                    }
                }

                rooms.Add(new Room($"{code}-{_roomStart[code].ToUnixTimeSeconds()}", players));
            }

            foreach (var gone in _roomStart.Keys.Where(k => !live.Contains(k)).ToList())
            {
                _roomStart.Remove(gone);
            }

            return rooms;
        }
    }
}
