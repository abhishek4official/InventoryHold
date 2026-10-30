using InventoryHold.Contracts.Enums;
using InventoryHold.Contracts.Requests;
using InventoryHold.Contracts.Responses;
using InventoryHold.Domain.Services;
using Microsoft.AspNetCore.Mvc;

namespace InventoryHold.WebApi.Controllers;

[ApiController]
[Route("api/holds")]
[Produces("application/json")]
public sealed class HoldsController(IHoldService holds) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<HoldResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<HoldResponse>> Create(CreateHoldRequest request, CancellationToken cancellationToken)
    {
        var hold = await holds.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { holdId = hold.Id }, hold);
    }

    [HttpGet("{holdId}")]
    [ProducesResponseType<HoldResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<HoldResponse> Get(string holdId, CancellationToken cancellationToken) =>
        holds.GetAsync(holdId, cancellationToken);

    /// <summary>Not in the brief's endpoint list; the frontend's Active Holds List needs it.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<HoldResponse>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<HoldResponse>> List([FromQuery] HoldStatus? status, CancellationToken cancellationToken) =>
        holds.ListAsync(status, cancellationToken);

    [HttpDelete("{holdId}")]
    [ProducesResponseType<HoldResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone)]
    public Task<HoldResponse> Release(string holdId, CancellationToken cancellationToken) =>
        holds.ReleaseAsync(holdId, cancellationToken);
}
