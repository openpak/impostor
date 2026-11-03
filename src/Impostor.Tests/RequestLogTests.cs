using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Impostor.Api.Net.Messages;
using Impostor.Server.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Impostor.Tests;

public class RequestLogTests
{
    private sealed class ListLogger : ILogger
    {
        public readonly List<string> Lines = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception));
    }

    [Fact]
    public async Task Http_line_has_method_path_status_ms_and_no_query_header_or_body()
    {
        var logger = new ListLogger();
        var mw = RequestLog.Middleware(
            async ctx =>
            {
                ctx.Response.StatusCode = 418;
                await ctx.Response.WriteAsync("body-secret");
            },
            logger);

        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "POST";
        ctx.Request.Path = "/api/user";
        ctx.Request.QueryString = new QueryString("?token=QUERYSECRET");
        ctx.Request.Headers["Authorization"] = "Bearer HEADERSECRET";
        ctx.Request.Headers["Cookie"] = "s=COOKIESECRET";
        ctx.Response.Body = new System.IO.MemoryStream();
        await mw(ctx);

        var line = Assert.Single(logger.Lines);
        Assert.Matches(@"^http POST /api/user -> 418 \d+ms$", line);
        foreach (var bad in new[] { "QUERYSECRET", "HEADERSECRET", "COOKIESECRET", "body-secret", "?" })
        {
            Assert.DoesNotContain(bad, line);
        }
    }

    [Fact]
    public async Task Healthz_is_skipped_and_a_throwing_handler_logs_500()
    {
        var logger = new ListLogger();
        var ok = RequestLog.Middleware(_ => Task.CompletedTask, logger);
        var health = new DefaultHttpContext();
        health.Request.Path = "/healthz";
        await ok(health);
        Assert.Empty(logger.Lines);

        var boom = RequestLog.Middleware(_ => throw new InvalidOperationException("secret detail"), logger);
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "GET";
        ctx.Request.Path = "/x";
        await Assert.ThrowsAsync<InvalidOperationException>(() => boom(ctx));
        Assert.Matches(@"^http GET /x -> 500 \d+ms$", Assert.Single(logger.Lines));
    }

    [Fact]
    public void Message_line_names_the_message_only_and_hot_relay_messages_are_not_logged()
    {
        var logger = new ListLogger();
        RequestLog.Command(logger, MessageFlags.FlagToString(MessageFlags.HostGame), "ok", Stopwatch.GetTimestamp());
        Assert.Matches(@"^cmd HostGame -> ok \d+ms$", Assert.Single(logger.Lines));
        Assert.True(RequestLog.IsHot(MessageFlags.GameData));
        Assert.True(RequestLog.IsHot(MessageFlags.GameDataTo));
        Assert.False(RequestLog.IsHot(MessageFlags.JoinGame));
    }

    [Fact]
    public void A_client_chosen_name_cannot_forge_lines()
    {
        var logger = new ListLogger();
        RequestLog.Http(logger, "GET", "/a\nhttp GET /forged -> 200 0ms", 200, Stopwatch.GetTimestamp());
        Assert.DoesNotContain('\n', logger.Lines.Single());
        Assert.True(logger.Lines.Single().Length < 120);
    }
}
