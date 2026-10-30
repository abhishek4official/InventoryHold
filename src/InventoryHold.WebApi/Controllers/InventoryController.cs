using InventoryHold.Contracts.Responses;
using InventoryHold.Domain.Services;
using Microsoft.AspNetCore.Mvc;

namespace InventoryHold.WebApi.Controllers;

[ApiController]
[Route("api/inventory")]
[Produces("application/json")]
public sealed class InventoryController(IHoldService holds) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<InventoryItemResponse>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<InventoryItemResponse>> Get(CancellationToken cancellationToken) =>
        holds.GetInventoryAsync(cancellationToken);
}
