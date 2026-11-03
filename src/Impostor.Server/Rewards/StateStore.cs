using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Impostor.Server.Rewards
{
    /// <summary>
    /// Durable state in Postgres (IMPOSTOR_DATABASE_URL): the session clock, the PUID to BAAS map and the
    /// outbox of counted sessions rewards has not accepted yet, so a restart loses none of them.
    /// </summary>
    internal sealed class StateStore : IAsyncDisposable
    {
        private const string Schema = @"
CREATE TABLE IF NOT EXISTS impostor_session_clock (metric text PRIMARY KEY, state text NOT NULL, saved_at timestamptz NOT NULL DEFAULT now());
CREATE TABLE IF NOT EXISTS impostor_puid (puid text PRIMARY KEY, baas_user_id text NOT NULL, last_seen timestamptz NOT NULL DEFAULT now());
CREATE TABLE IF NOT EXISTS impostor_outbox (event_id text PRIMARY KEY, metric text NOT NULL, namespace text NOT NULL, subject text NOT NULL, room_id text NOT NULL, occurred_at timestamptz NOT NULL);";

        private readonly NpgsqlDataSource _db;

        private StateStore(NpgsqlDataSource db)
        {
            _db = db;
        }

        /// <summary>Turns postgres://user:pass@host:port/db?sslmode=disable into an Npgsql connection string.</summary>
        /// <param name="url">The URL.</param>
        /// <returns>The connection string, pool capped at two connections.</returns>
        public static string ConnectionString(string url)
        {
            if (!url.StartsWith("postgres", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            var u = new Uri(url);
            var ui = u.UserInfo.Split(':', 2);
            var b = new NpgsqlConnectionStringBuilder
            {
                Host = u.Host,
                Port = u.IsDefaultPort ? 5432 : u.Port,
                Database = u.AbsolutePath.TrimStart('/'),
                Username = Uri.UnescapeDataString(ui[0]),
                Password = ui.Length > 1 ? Uri.UnescapeDataString(ui[1]) : null,
                MaxPoolSize = 2,
            };
            if (u.Query.Contains("sslmode=disable"))
            {
                b.SslMode = SslMode.Disable;
            }

            return b.ConnectionString;
        }

        public static async Task<StateStore> OpenAsync(string url, CancellationToken ct)
        {
            var db = NpgsqlDataSource.Create(ConnectionString(url));
            await using var cmd = db.CreateCommand(Schema);
            await cmd.ExecuteNonQueryAsync(ct);
            return new StateStore(db);
        }

        public async Task SaveClockAsync(string metric, ClockState state, CancellationToken ct)
        {
            await using var cmd = _db.CreateCommand("INSERT INTO impostor_session_clock (metric, state, saved_at) VALUES ($1,$2,now()) ON CONFLICT (metric) DO UPDATE SET state = EXCLUDED.state, saved_at = now()");
            cmd.Parameters.AddWithValue(metric);
            cmd.Parameters.AddWithValue(JsonSerializer.Serialize(state));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<ClockState?> LoadClockAsync(string metric, CancellationToken ct)
        {
            await using var cmd = _db.CreateCommand("SELECT state FROM impostor_session_clock WHERE metric = $1");
            cmd.Parameters.AddWithValue(metric);
            var raw = await cmd.ExecuteScalarAsync(ct) as string;
            return raw == null ? null : JsonSerializer.Deserialize<ClockState>(raw);
        }

        public async Task UpsertPuidAsync(string puid, string baas, CancellationToken ct)
        {
            await using var cmd = _db.CreateCommand("INSERT INTO impostor_puid (puid, baas_user_id, last_seen) VALUES ($1,$2,now()) ON CONFLICT (puid) DO UPDATE SET baas_user_id = EXCLUDED.baas_user_id, last_seen = now()");
            cmd.Parameters.AddWithValue(puid);
            cmd.Parameters.AddWithValue(baas);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<Dictionary<string, string>> LoadPuidsAsync(CancellationToken ct)
        {
            var map = new Dictionary<string, string>();
            await using var cmd = _db.CreateCommand("SELECT puid, baas_user_id FROM impostor_puid");
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                map[r.GetString(0)] = r.GetString(1);
            }

            return map;
        }

        public async Task OutboxAddAsync(IEnumerable<SessionEvent> events, CancellationToken ct)
        {
            foreach (var e in events)
            {
                await using var cmd = _db.CreateCommand("INSERT INTO impostor_outbox (event_id, metric, namespace, subject, room_id, occurred_at) VALUES ($1,$2,$3,$4,$5,$6) ON CONFLICT DO NOTHING");
                cmd.Parameters.AddWithValue(e.EventId);
                cmd.Parameters.AddWithValue(e.Metric);
                cmd.Parameters.AddWithValue(e.Player.Namespace);
                cmd.Parameters.AddWithValue(e.Player.Subject);
                cmd.Parameters.AddWithValue(e.RoomId);
                cmd.Parameters.AddWithValue(e.At.UtcDateTime);
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }

        public async Task OutboxDeleteAsync(IReadOnlyList<string> ids, CancellationToken ct)
        {
            await using var cmd = _db.CreateCommand("DELETE FROM impostor_outbox WHERE event_id = ANY($1)");
            cmd.Parameters.AddWithValue(new List<string>(ids));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<List<SessionEvent>> OutboxLoadAsync(CancellationToken ct)
        {
            var list = new List<SessionEvent>();
            await using var cmd = _db.CreateCommand("SELECT event_id, metric, namespace, subject, room_id, occurred_at FROM impostor_outbox ORDER BY occurred_at");
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add(new SessionEvent(r.GetString(0), r.GetString(1), new Identity(r.GetString(2), r.GetString(3)), r.GetString(4), new DateTimeOffset(r.GetDateTime(5), TimeSpan.Zero)));
            }

            return list;
        }

        public ValueTask DisposeAsync() => _db.DisposeAsync();
    }
}
