using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Impostor.Server.Rewards;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Impostor.Server.Http;

/// <summary>
/// DELETE /internal/accounts/{accountId}/data, called by the website's account finaliser with
/// "Authorization: Bearer ACCOUNT_ERASE_KEY". 503 while ACCOUNT_ERASE_KEY is unset.
/// </summary>
[ApiController]
public sealed class EraseController : ControllerBase
{
    private static readonly HttpClient Http = new(new SocketsHttpHandler()) { Timeout = TimeSpan.FromSeconds(15) };
    private readonly ILogger<EraseController> _logger;

    public EraseController(ILogger<EraseController> logger)
    {
        _logger = logger;
    }

    [HttpDelete("/internal/accounts/{accountId}/data")]
    public async Task<IActionResult> Erase(string accountId, CancellationToken ct)
    {
        var key = Environment.GetEnvironmentVariable("ACCOUNT_ERASE_KEY") ?? string.Empty;
        if (key.Length == 0)
        {
            return StatusCode(503, new { error = "erase not configured" });
        }

        if (!AccountErasure.KeyMatches(Request.Headers.Authorization.ToString(), key))
        {
            return Unauthorized(new { error = "unauthorized" });
        }

        if (!Guid.TryParse(accountId, out var id))
        {
            return BadRequest(new { error = "account_id must be a UUID" });
        }

        var addr = Environment.GetEnvironmentVariable("ACCOUNT_GRPC_ADDR");
        var internalKey = Environment.GetEnvironmentVariable("ACCOUNT_INTERNAL_KEY") ?? string.Empty;
        if (internalKey.Length == 0)
        {
            return StatusCode(503, new { error = "account core not configured" });
        }

        StateStore? store = null;
        var dbUrl = Environment.GetEnvironmentVariable("IMPOSTOR_DATABASE_URL") ?? string.Empty;
        try
        {
            if (dbUrl.Length > 0)
            {
                store = await StateStore.OpenAsync(dbUrl, ct);
            }

            var links = new CoreLinks(Http, string.IsNullOrEmpty(addr) ? "openpak-account:20001" : addr, internalKey);
            var rows = await new AccountErasure(links, store, _logger).EraseAsync(id.ToString("D"), ct);
            return Ok(new { service = AccountErasure.Service, rows });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError("erase failed: {Type}", ex.GetType().Name);
            return StatusCode(500, new { error = "erase failed" });
        }
        finally
        {
            if (store != null)
            {
                await store.DisposeAsync();
            }
        }
    }
}
