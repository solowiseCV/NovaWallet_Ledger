namespace NovaWallet.Domain.Enums;

public enum TransactionType
{
    /// <summary>Inbound funds not tied to a NovaWallet-to-NovaWallet transfer (e.g. simulated NIP inbound).</summary>
    Credit,
    /// <summary>Debit leg of a wallet-to-wallet transfer.</summary>
    TransferOut,
    /// <summary>Credit leg of a wallet-to-wallet transfer.</summary>
    TransferIn
}
