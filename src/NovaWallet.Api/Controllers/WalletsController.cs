using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NovaWallet.Api.Contracts;
using NovaWallet.Application.Dtos;
using NovaWallet.Application.Interfaces;

namespace NovaWallet.Api.Controllers;

[ApiController]
[Route("api/wallets")]
[Authorize]
[Produces("application/json", "application/problem+json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
public class WalletsController : ControllerBase
{
    private readonly IWalletService _walletService;

    public WalletsController(IWalletService walletService) => _walletService = walletService;

    private string ActorId =>
        User.FindFirstValue("customerId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";

    /// <summary>Create a wallet for a customer. Starting balance is always zero.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<WalletResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<WalletResponse>>> Create([FromBody] CreateWalletRequest request, CancellationToken ct)
    {
        var wallet = await _walletService.CreateWalletAsync(request.CustomerId, ct);
        return CreatedAtAction(nameof(Get), new { walletId = wallet.WalletId }, this.ToApiResponse(wallet));
    }

    /// <summary>Get current balance (in kobo) and currency for a wallet.</summary>
    [HttpGet("{walletId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<WalletResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<WalletResponse>>> Get([FromRoute] Guid walletId, CancellationToken ct)
        => Ok(this.ToApiResponse(await _walletService.GetWalletAsync(walletId, ct)));

    /// <summary>Simulates an inbound NIP transfer landing in the wallet.</summary>
    [HttpPost("{walletId:guid}/credit")]
    [ProducesResponseType(typeof(ApiResponse<WalletResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<WalletResponse>>> Credit([FromRoute] Guid walletId, [FromBody] CreditWalletRequest request, CancellationToken ct)
        => Ok(this.ToApiResponse(await _walletService.CreditWalletAsync(walletId, request.AmountKobo, request.Description, ActorId, ct)));

    /// <summary>
    /// Atomically move funds between two wallets. Requires an Idempotency-Key
    /// header; replaying the same key with the same payload returns the original
    /// result, replaying with a different payload is rejected with 409.
    /// </summary>
    [HttpPost("transfer")]
    [EnableRateLimiting("transfer")]
    [ProducesResponseType(typeof(ApiResponse<TransferResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ApiResponse<TransferResponse>>> Transfer(
        [FromBody] TransferRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Problem(title: "Idempotency-Key header is required.", statusCode: StatusCodes.Status400BadRequest);

        var result = await _walletService.TransferAsync(request, idempotencyKey, ActorId, ct);
        return Ok(this.ToApiResponse(result));
    }

    /// <summary>Paginated transaction history for a wallet, newest first.</summary>
    [HttpGet("{walletId:guid}/statement")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<TransactionResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PagedResult<TransactionResponse>>>> Statement(
        [FromRoute] Guid walletId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(this.ToApiResponse(await _walletService.GetStatementAsync(walletId, page, pageSize, ct)));
}
