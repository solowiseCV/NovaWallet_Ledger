using NovaWallet.Application.Dtos;

namespace NovaWallet.Application.Interfaces;

public interface IWalletService
{
    Task<WalletResponse> CreateWalletAsync(string customerId, CancellationToken ct);
    Task<WalletResponse> GetWalletAsync(Guid walletId, CancellationToken ct);
    Task<WalletResponse> CreditWalletAsync(Guid walletId, long amountKobo, string? description, string actorId, CancellationToken ct);
    Task<TransferResponse> TransferAsync(TransferRequest request, string idempotencyKey, string actorId, CancellationToken ct);
    Task<PagedResult<TransactionResponse>> GetStatementAsync(Guid walletId, int page, int pageSize, CancellationToken ct);
}
