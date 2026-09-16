using NovaWallet.Domain.Exceptions;

namespace NovaWallet.Domain.Services;

/// <summary>Business rules that apply to a wallet transfer independently of persistence or HTTP.</summary>
public static class WalletTransferPolicy
{
    public const long DailyOutboundLimitKobo = 50_000_000;

    public static void EnsurePositiveAmount(long amountKobo)
    {
        if (amountKobo <= 0)
            throw new InvalidTransferException("amountKobo must be a positive integer number of kobo.");
    }

    public static void EnsureDistinctWallets(Guid fromWalletId, Guid toWalletId)
    {
        if (fromWalletId == toWalletId)
            throw new InvalidTransferException("fromWalletId and toWalletId must differ.");
    }

    public static void EnsureWithinDailyOutboundLimit(long alreadySentKobo, long amountKobo)
    {
        if (alreadySentKobo < 0 || alreadySentKobo > DailyOutboundLimitKobo - amountKobo)
            throw new DailyLimitExceededException(DailyOutboundLimitKobo);
    }
}
