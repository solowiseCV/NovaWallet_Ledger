using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NovaWallet.Api.Tests.Fixtures;
using NovaWallet.Application.Dtos;
using NovaWallet.Domain.Exceptions;
using Xunit;

namespace NovaWallet.Api.Tests;

/// <summary>
/// Tests the API layer itself — auth enforcement, routing, status codes, and
/// the RFC 7807 Problem Details shape — with the use-case layer stubbed out.
/// Whether the *business logic* is correct is Application.Tests' and
/// Infrastructure.Tests' job; this project only answers "given the service
/// says X, does the HTTP layer respond correctly?"
/// </summary>
public class WalletsControllerTests
{
    [Fact]
    public async Task GetWallet_WithoutToken_Returns401()
    {
        await using var factory = new NovaWalletApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/wallets/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetWallet_WhenNotFound_ReturnsProblemDetailsWith404()
    {
        await using var factory = new NovaWalletApiFactory();
        var walletId = Guid.NewGuid();
        factory.WalletServiceStub.OnGetWallet = (_, _) => Task.FromException<WalletResponse>(new WalletNotFoundException(walletId));

        var client = await factory.CreateClient().AuthenticatedAsAsync("cust-001");
        var response = await client.GetAsync($"/api/wallets/{walletId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Title.Should().Be("Wallet not found");
        problem.Extensions.Should().ContainKey("traceId");
    }

    [Fact]
    public async Task GetWallet_WhenFound_ReturnsWalletResponse()
    {
        await using var factory = new NovaWalletApiFactory();
        var walletId = Guid.NewGuid();
        var expected = new WalletResponse(walletId, "cust-001", 5_000, "NGN", DateTimeOffset.UtcNow);
        factory.WalletServiceStub.OnGetWallet = (_, _) => Task.FromResult(expected);

        var client = await factory.CreateClient().AuthenticatedAsAsync("cust-001");
        var response = await client.GetAsync($"/api/wallets/{walletId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<WalletResponse>();
        body!.BalanceKobo.Should().Be(5_000);
    }

    [Fact]
    public async Task CreateWallet_ReturnsCreatedWithLocationHeader()
    {
        await using var factory = new NovaWalletApiFactory();
        var created = new WalletResponse(Guid.NewGuid(), "cust-001", 0, "NGN", DateTimeOffset.UtcNow);
        factory.WalletServiceStub.OnCreateWallet = (_, _) => Task.FromResult(created);

        var client = await factory.CreateClient().AuthenticatedAsAsync("cust-001");
        var response = await client.PostAsJsonAsync("/api/wallets", new CreateWalletRequest("cust-001"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Contain(created.WalletId.ToString());
    }

    [Fact]
    public async Task Transfer_WithoutIdempotencyKeyHeader_Returns400()
    {
        await using var factory = new NovaWalletApiFactory();
        var client = await factory.CreateClient().AuthenticatedAsAsync("cust-001");

        var response = await client.PostAsJsonAsync("/api/wallets/transfer",
            new TransferRequest(Guid.NewGuid(), Guid.NewGuid(), 1_000, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Transfer_WhenCallerDoesNotOwnSourceWallet_Returns403()
    {
        await using var factory = new NovaWalletApiFactory();
        factory.WalletServiceStub.OnTransfer = (_, _, _, _) =>
            Task.FromException<TransferResponse>(new ForbiddenOperationException("You may only transfer funds out of your own wallet."));

        var client = await factory.CreateClient().AuthenticatedAsAsync("cust-001");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/wallets/transfer")
        {
            Content = JsonContent.Create(new TransferRequest(Guid.NewGuid(), Guid.NewGuid(), 1_000, null))
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Transfer_WhenDailyLimitExceeded_Returns422()
    {
        await using var factory = new NovaWalletApiFactory();
        factory.WalletServiceStub.OnTransfer = (_, _, _, _) =>
            Task.FromException<TransferResponse>(new DailyLimitExceededException(50_000_000));

        var client = await factory.CreateClient().AuthenticatedAsAsync("cust-001");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/wallets/transfer")
        {
            Content = JsonContent.Create(new TransferRequest(Guid.NewGuid(), Guid.NewGuid(), 1_000, null))
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be((HttpStatusCode)422);
    }

    [Fact]
    public async Task Transfer_WhenIdempotencyKeyReused_Returns409()
    {
        await using var factory = new NovaWalletApiFactory();
        factory.WalletServiceStub.OnTransfer = (_, _, _, _) =>
            Task.FromException<TransferResponse>(new IdempotencyKeyConflictException());

        var client = await factory.CreateClient().AuthenticatedAsAsync("cust-001");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/wallets/transfer")
        {
            Content = JsonContent.Create(new TransferRequest(Guid.NewGuid(), Guid.NewGuid(), 1_000, null))
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task LivenessEndpoint_DoesNotRequireAuth_AndReturnsOk()
    {
        await using var factory = new NovaWalletApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SwaggerJson_IsReachable()
    {
        await using var factory = new NovaWalletApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
