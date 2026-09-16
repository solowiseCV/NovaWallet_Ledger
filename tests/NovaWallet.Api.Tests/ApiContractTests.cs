using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NovaWallet.Api.Contracts;
using NovaWallet.Api.Controllers;
using Xunit;

namespace NovaWallet.Api.Tests;

public class ApiContractTests
{
    [Fact]
    public void ApiResponse_AlwaysMarksSuccessfulPayloadsAsSuccessful()
    {
        var response = ApiResponse<string>.Ok("value", "trace-123");

        response.Success.Should().BeTrue();
        response.Data.Should().Be("value");
        response.TraceId.Should().Be("trace-123");
    }

    [Fact]
    public void TransferAction_DeclaresItsSuccessAndProblemResponses()
    {
        var attributes = typeof(WalletsController).GetMethod(nameof(WalletsController.Transfer))!
            .GetCustomAttributes(typeof(ProducesResponseTypeAttribute), inherit: true)
            .Cast<ProducesResponseTypeAttribute>();

        attributes.Select(attribute => attribute.StatusCode).Should().Contain(new[] { 200, 400, 403, 409, 422 });
    }
}
