using Datara.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Datara.Api.Controllers;

/// <summary>
/// Supplies the single-tenant owner for the MVP. Datara ships without an identity
/// provider; this is the one place that assumption is expressed, so adding auth
/// later means replacing this class and nothing else.
/// </summary>
public sealed class OwnerContext
{
    /// <summary>Matches the seed record created by <c>SeedAsync</c>.</summary>
    public static readonly Guid DefaultOwnerId = new("6f9619ff-8b86-d011-b42d-00cf4fc964ff");

    public Guid OwnerId => DefaultOwnerId;
}

[ApiController]
[Route("api/v1/owner")]
[Produces("application/json")]
[Tags("Owner")]
public sealed class OwnerController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(OwnerDto), StatusCodes.Status200OK)]
    public ActionResult<OwnerDto> Get() => Ok(new OwnerDto
    {
        Id = OwnerContext.DefaultOwnerId,
        Mode = "single-tenant",
        Notice = "Datara is running in single-tenant MVP mode. Sign-in is not required.",
    });
}

public sealed record OwnerDto
{
    public Guid Id { get; init; }

    public string Mode { get; init; } = string.Empty;

    public string Notice { get; init; } = string.Empty;
}
