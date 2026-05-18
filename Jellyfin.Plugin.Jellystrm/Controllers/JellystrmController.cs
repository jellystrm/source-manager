using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Text.Json;
using Jellyfin.Plugin.Jellystrm.Models;
using Jellyfin.Plugin.Jellystrm.Services;
using Jellyfin.Plugin.Jellystrm.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Jellystrm.Controllers;

[ApiController]
[Authorize]
[Route("Jellystrm")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class JellystrmController : ControllerBase
{
    private static readonly JsonSerializerOptions SseJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RequestWorkflowService _requestWorkflowService;
    private readonly RequestEventBroker _eventBroker;

    public JellystrmController(RequestWorkflowService requestWorkflowService, RequestEventBroker eventBroker)
    {
        _requestWorkflowService = requestWorkflowService;
        _eventBroker = eventBroker;
    }

    [HttpGet("Capabilities")]
    [ProducesResponseType<CapabilitiesResponse>(StatusCodes.Status200OK)]
    public ActionResult<CapabilitiesResponse> GetCapabilities()
        => Ok(new CapabilitiesResponse(true, "1", true));

    [HttpPost("Request")]
    [ProducesResponseType<MediaRequestDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<MediaRequestDto>> CreateRequest(
        [FromBody, Required] CreateRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!TryNormalizeAndAuthorizeUserId(request.UserId, out var normalizedUserId, out var errorResult))
        {
            return errorResult;
        }

        try
        {
            var normalizedRequest = request with { UserId = normalizedUserId };
            var created = await _requestWorkflowService.CreateRequestAsync(normalizedRequest, cancellationToken).ConfigureAwait(false);
            return Ok(created.ToDto());
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("Requests")]
    [ProducesResponseType<IReadOnlyList<MediaRequestDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<MediaRequestDto>>> GetRequests(
        [FromQuery, Required] string userId,
        CancellationToken cancellationToken)
    {
        if (!TryNormalizeAndAuthorizeUserId(userId, out var normalizedUserId, out var errorResult))
        {
            return errorResult;
        }

        var requests = await _requestWorkflowService.GetUserRequestsAsync(normalizedUserId, cancellationToken).ConfigureAwait(false);
        return Ok(requests.Select(request => request.ToDto()).ToArray());
    }

    [HttpGet("Requests/Events")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [Produces("text/event-stream")]
    public async Task GetRequestEvents([FromQuery, Required] string userId)
    {
        if (!TryNormalizeAndAuthorizeUserId(userId, out var normalizedUserId, out var errorResult))
        {
            Response.StatusCode = errorResult is ForbidResult ? StatusCodes.Status403Forbidden : StatusCodes.Status400BadRequest;
            return;
        }

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        var reader = _eventBroker.Subscribe(normalizedUserId, out var subscription);
        using (subscription)
        {
            await Response.WriteAsync(": connected\n\n", HttpContext.RequestAborted).ConfigureAwait(false);
            await Response.Body.FlushAsync(HttpContext.RequestAborted).ConfigureAwait(false);

            await foreach (var request in reader.ReadAllAsync(HttpContext.RequestAborted).ConfigureAwait(false))
            {
                var json = JsonSerializer.Serialize(request, SseJsonOptions);
                await Response.WriteAsync("event: request\n", HttpContext.RequestAborted).ConfigureAwait(false);
                await Response.WriteAsync($"data: {json}\n\n", HttpContext.RequestAborted).ConfigureAwait(false);
                await Response.Body.FlushAsync(HttpContext.RequestAborted).ConfigureAwait(false);
            }
        }
    }

    [HttpGet("Admin/Requests")]
    [Authorize(Policy = JellyfinAuthorizationPolicies.RequiresElevation)]
    [ProducesResponseType<IReadOnlyList<MediaRequestDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<MediaRequestDto>>> GetAdminRequests(
        [FromQuery] string status = RequestStatus.Pending,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var requests = await _requestWorkflowService.GetAdminRequestsAsync(status, cancellationToken).ConfigureAwait(false);
            return Ok(requests.Select(request => request.ToDto()).ToArray());
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("Admin/Requests/{requestId}/Approve")]
    [Authorize(Policy = JellyfinAuthorizationPolicies.RequiresElevation)]
    [ProducesResponseType<MediaRequestDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MediaRequestDto>> ApproveRequest(
        [FromRoute, Required] string requestId,
        CancellationToken cancellationToken)
    {
        var updated = await _requestWorkflowService.ApproveAsync(requestId, cancellationToken).ConfigureAwait(false);
        var latest = updated.LastOrDefault();
        return latest is null ? NotFound() : Ok(latest.ToDto());
    }

    [HttpPost("Admin/Requests/{requestId}/Reject")]
    [Authorize(Policy = JellyfinAuthorizationPolicies.RequiresElevation)]
    [ProducesResponseType<MediaRequestDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MediaRequestDto>> RejectRequest(
        [FromRoute, Required] string requestId,
        [FromBody] RejectRequestDto? request,
        CancellationToken cancellationToken)
    {
        var rejected = await _requestWorkflowService.RejectAsync(requestId, request?.Reason, cancellationToken).ConfigureAwait(false);
        return rejected is null ? NotFound() : Ok(rejected.ToDto());
    }

    [HttpPost("Admin/Requests/{requestId}/Refresh")]
    [Authorize(Policy = JellyfinAuthorizationPolicies.RequiresElevation)]
    [ProducesResponseType<MediaRequestDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MediaRequestDto>> RefreshRequest(
        [FromRoute, Required] string requestId,
        CancellationToken cancellationToken)
    {
        var refreshed = await _requestWorkflowService.RefreshAsync(requestId, cancellationToken).ConfigureAwait(false);
        return refreshed is null ? NotFound() : Ok(refreshed.ToDto());
    }

    private bool TryNormalizeAndAuthorizeUserId(string userId, out string normalizedUserId, out ActionResult errorResult)
    {
        normalizedUserId = string.Empty;
        errorResult = BadRequest("userId must be a valid Jellyfin user id.");

        if (!Guid.TryParse(userId, out var requestedUserId))
        {
            return false;
        }

        if (requestedUserId != User.GetJellyfinUserId())
        {
            errorResult = Forbid();
            return false;
        }

        normalizedUserId = requestedUserId.ToString("N");
        return true;
    }
}
