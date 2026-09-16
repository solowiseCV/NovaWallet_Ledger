namespace NovaWallet.Application.Dtos;

public record CreateWalletRequest(string CustomerId);

public record WalletResponse(
    Guid WalletId,
    string CustomerId,
    long BalanceKobo,
    string Currency,
    DateTimeOffset CreatedAtUtc);

public record CreditWalletRequest(long AmountKobo, string? Description);

public record TransferRequest(Guid FromWalletId, Guid ToWalletId, long AmountKobo, string? Description);

public record TransferResponse(
    Guid TransferGroupId,
    Guid FromWalletId,
    Guid ToWalletId,
    long AmountKobo,
    long FromWalletBalanceAfterKobo,
    long ToWalletBalanceAfterKobo,
    DateTimeOffset CreatedAtUtc);

public record TransactionResponse(
    Guid Id,
    string Type,
    long AmountKobo,
    long BalanceAfterKobo,
    Guid? CounterpartyWalletId,
    string? Description,
    DateTimeOffset CreatedAtUtc);

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount);
