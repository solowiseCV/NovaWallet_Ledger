using FluentAssertions;
using NovaWallet.Application.Dtos;
using Xunit;

namespace NovaWallet.Application.Tests;

public class ApplicationContractTests
{
    [Fact]
    public void TransferRequest_PreservesTheUseCaseInput()
    {
        var fromWalletId = Guid.NewGuid();
        var toWalletId = Guid.NewGuid();

        var request = new TransferRequest(fromWalletId, toWalletId, 12_500, "Rent");

        request.FromWalletId.Should().Be(fromWalletId);
        request.ToWalletId.Should().Be(toWalletId);
        request.AmountKobo.Should().Be(12_500);
        request.Description.Should().Be("Rent");
    }
}
