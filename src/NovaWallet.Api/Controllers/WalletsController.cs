using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NovaWallet.Application.Dtos;
using NovaWallet.Application.Interfaces;

namespace NovaWallet.Api.Controllers;

[ApiController]
[Route("api/wallets")]
[Authorize]
[Produces("application/json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
public class WalletsController : ControllerBase
{
    private readonly IWalletService _walletService;

    public WalletsController(IWalletService walletService) => _walletService = walletService;

    private string ActorId =>
        User.FindFirstValue("customerId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";

    /// <summary>Create a wallet for a customer. Starting balance is always zero.</summary>
    [HttpPost(Name = "CreateWallet")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(WalletResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<WalletResponse>> Create([FromBody] CreateWalletRequest request, CancellationToken ct)
    {
        var wallet = await _walletService.CreateWalletAsync(request.CustomerId, ct);
        return CreatedAtRoute("GetWallet", new { walletId = wallet.WalletId }, wallet);
    }

    /// <summary>Get current balance (in kobo) and currency for a wallet.</summary>
    [HttpGet("{walletId:guid}", Name = "GetWallet")]
    [ProducesResponseType(typeof(WalletResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WalletResponse>> Get(Guid walletId, CancellationToken ct)
        => Ok(await _walletService.GetWalletAsync(walletId, ct));

    /// <summary>Simulates an inbound NIP transfer landing in the wallet.</summary>
    [HttpPost("{walletId:guid}/credit", Name = "CreditWallet")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(WalletResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WalletResponse>> Credit(Guid walletId, [FromBody] CreditWalletRequest request, CancellationToken ct)
        => Ok(await _walletService.CreditWalletAsync(walletId, request.AmountKobo, request.Description, ActorId, ct));

    /// <summary>
    /// Atomically move funds between two wallets. Requires an Idempotency-Key
    /// header; replaying the same key with the same payload returns the original
    /// result, replaying with a different payload is rejected with 409.
    /// </summary>
    [HttpPost("transfer", Name = "TransferFunds")]
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

    /// <summary>Paginated transaction history for a wallet, newest first.</summary>
    [HttpGet("{walletId:guid}/statement", Name = "GetWalletStatement")]
    [ProducesResponseType(typeof(PagedResult<TransactionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<TransactionResponse>>> Statement(
        Guid walletId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => Ok(await _walletService.GetStatementAsync(walletId, page, pageSize, ct));
}
