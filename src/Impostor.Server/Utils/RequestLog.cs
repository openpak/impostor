using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Impostor.Api.Net.Messages;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Impostor.Server.Utils;

/// <summary>
/// One log line per HTTP request and per Hazel message, for the host log collector (stdout, picked up
/// from journald): <c>http GET /api/games -> 200 3ms</c> and <c>cmd HostGame -> ok 2ms</c>. Only the
/// path or the message name: never the query string, headers, bodies, nicknames or message bytes.
/// </summary>
internal static class RequestLog
{
    /// <summary>The per-packet relay messages; one line each would be thousands per second per game.</summary>
    public static bool IsHot(byte flag) =>
        flag == MessageFlags.GameData || flag == MessageFlags.GameDataTo || flag == MessageFlags.PackedGameDataTo;

    public static void Command(ILogger logger, string name, string result, long startTimestamp)
    {
        logger.LogInformation("cmd {Name} -> {Result} {Ms}ms", Clean(name), result, (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
    }

    public static void Http(ILogger logger, string method, string path, int status, long startTimestamp)
    {
        if (path == "/healthz")
        {
            return;
        }

        logger.LogInformation("http {Method} {Path} -> {Status} {Ms}ms", Clean(method), Clean(path), status, (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
    }

    public static RequestDelegate Middleware(RequestDelegate next, ILogger logger) => async context =>
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            await next(context);
        }
        catch
        {
            Http(logger, context.Request.Method, context.Request.Path.Value ?? "/", 500, start);
            throw;
        }

        Http(logger, context.Request.Method, context.Request.Path.Value ?? "/", context.Response.StatusCode, start);
    };

    /// <summary>Keeps a client-chosen token from forging log lines or growing without bound.</summary>
    internal static string Clean(string s) =>
        new(s.Take(64).Select(c => c < 0x20 || c == 0x7f || c == ' ' ? '?' : c).ToArray());
}
