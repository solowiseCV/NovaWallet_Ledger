using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NovaWallet.Application.Dtos;
using NovaWallet.Application.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace NovaWallet.Api.Controllers;

[ApiController]
[Route("api/wallets")]
[Authorize]
[Produces("application/json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[SwaggerTag("Create and manage wallets, credits, transfers, and transaction statements.")]
public class WalletsController : ControllerBase
{
    private readonly IWalletService _walletService;

    public WalletsController(IWalletService walletService) => _walletService = walletService;

    private string ActorId =>
        User.FindFirstValue("customerId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";

    [HttpPost(Name = "CreateWallet")]
    [SwaggerOperation(Summary = "Create a wallet", Description = "Creates a wallet for a customer with a starting balance of zero.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(WalletResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<WalletResponse>> Create([FromBody] CreateWalletRequest request, CancellationToken ct)
    {
        var wallet = await _walletService.CreateWalletAsync(request.CustomerId, ct);
        return CreatedAtRoute("GetWallet", new { walletId = wallet.WalletId }, wallet);
    }

    [HttpGet("{walletId:guid}", Name = "GetWallet")]
    [SwaggerOperation(Summary = "Get wallet balance", Description = "Returns the current balance in kobo and the wallet currency.")]
    [ProducesResponseType(typeof(WalletResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WalletResponse>> Get(Guid walletId, CancellationToken ct)
        => Ok(await _walletService.GetWalletAsync(walletId, ct));

    [HttpPost("{walletId:guid}/credit", Name = "CreditWallet")]
    [SwaggerOperation(Summary = "Credit a wallet", Description = "Simulates an inbound NIP transfer and increases the wallet balance.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(WalletResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WalletResponse>> Credit(Guid walletId, [FromBody] CreditWalletRequest request, CancellationToken ct)
        => Ok(await _walletService.CreditWalletAsync(walletId, request.AmountKobo, request.Description, ActorId, ct));

    [HttpPost("transfer", Name = "TransferFunds")]
    [SwaggerOperation(Summary = "Transfer funds", Description = "Atomically moves funds between wallets. Requires an Idempotency-Key header; reusing it with a different payload returns 409.")]
    [Consumes("application/json")]
    [EnableRateLimiting("transfer")]
    [ProducesResponseType(typeof(TransferResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<TransferResponse>> Transfer(
        [FromBody] TransferRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Problem(title: "Idempotency-Key header is required.", statusCode: StatusCodes.Status400BadRequest);

        var result = await _walletService.TransferAsync(request, idempotencyKey, ActorId, ct);
        return Ok(result);
    }

    [HttpGet("{walletId:guid}/statement", Name = "GetWalletStatement")]
    [SwaggerOperation(Summary = "Get wallet statement", Description = "Returns the wallet transaction history in reverse chronological order with pagination.")]
    [ProducesResponseType(typeof(PagedResult<TransactionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<TransactionResponse>>> Statement(
        Guid walletId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => Ok(await _walletService.GetStatementAsync(walletId, page, pageSize, ct));
}
