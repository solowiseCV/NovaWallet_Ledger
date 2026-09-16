namespace NovaWallet.Domain.Exceptions;

public class WalletNotFoundException : Exception
{
    public WalletNotFoundException(Guid walletId) : base($"Wallet '{walletId}' was not found.") { }
}

public class InsufficientFundsException : Exception
{
    public InsufficientFundsException() : base("Wallet has insufficient funds for this transfer.") { }
}

public class DailyLimitExceededException : Exception
{
    public DailyLimitExceededException(long limitKobo)
        : base($"This transfer would exceed the daily outbound transfer limit of {limitKobo} kobo.") { }
}

public class IdempotencyKeyConflictException : Exception
{
    public IdempotencyKeyConflictException()
        : base("This Idempotency-Key was already used with a different request payload.") { }
}

public class IdempotencyRequestInProgressException : Exception
{
    public IdempotencyRequestInProgressException()
        : base("A request with this Idempotency-Key is currently being processed. Retry shortly.") { }
}

public class InvalidTransferException : Exception
{
    public InvalidTransferException(string message) : base(message) { }
}

public class ForbiddenOperationException : Exception
{
    public ForbiddenOperationException(string message) : base(message) { }
}
