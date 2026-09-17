using NovaWallet.Application.Dtos;
using NovaWallet.Application.Interfaces;

namespace NovaWallet.Api.Tests.Fakes;

/// <summary>
/// A hand-written stub, not a mocking-framework object — IWalletService only has
/// five methods and each test needs a different canned outcome (success, 404,
/// 403, 409...), which a couple of settable Func delegates handle perfectly well
/// without pulling in Moq/NSubstitute for a handful of call sites.
/// </summary>
public class StubWalletService : IWalletService
{
    public Func<string, CancellationToken, Task<WalletResponse>>? OnCreateWallet { get; set; }
    public Func<Guid, CancellationToken, Task<WalletResponse>>? OnGetWallet { get; set; }
    public Func<Guid, long, string?, string, CancellationToken, Task<WalletResponse>>? OnCreditWallet { get; set; }
    public Func<TransferRequest, string, string, CancellationToken, Task<TransferResponse>>? OnTransfer { get; set; }
    public Func<Guid, int, int, CancellationToken, Task<PagedResult<TransactionResponse>>>? OnGetStatement { get; set; }

    public Task<WalletResponse> CreateWalletAsync(string customerId, CancellationToken ct) =>
        OnCreateWallet?.Invoke(customerId, ct) ?? throw new NotImplementedException("Set OnCreateWallet for this test.");

    public Task<WalletResponse> GetWalletAsync(Guid walletId, CancellationToken ct) =>
        OnGetWallet?.Invoke(walletId, ct) ?? throw new NotImplementedException("Set OnGetWallet for this test.");

    public Task<WalletResponse> CreditWalletAsync(Guid walletId, long amountKobo, string? description, string actorId, CancellationToken ct) =>
        OnCreditWallet?.Invoke(walletId, amountKobo, description, actorId, ct) ?? throw new NotImplementedException("Set OnCreditWallet for this test.");

    public Task<TransferResponse> TransferAsync(TransferRequest request, string idempotencyKey, string actorId, CancellationToken ct) =>
        OnTransfer?.Invoke(request, idempotencyKey, actorId, ct) ?? throw new NotImplementedException("Set OnTransfer for this test.");

    public Task<PagedResult<TransactionResponse>> GetStatementAsync(Guid walletId, int page, int pageSize, CancellationToken ct) =>
        OnGetStatement?.Invoke(walletId, page, pageSize, ct) ?? throw new NotImplementedException("Set OnGetStatement for this test.");
}
