# NovaWallet Ledger Service

A simplified wallet ledger for FirstBank NovaPay's NovaWallet module, built in C# / .NET 8.

## Running it

```bash
docker compose up --build
```

That's the whole setup: it starts Postgres, waits for it to be healthy, then builds and
starts the API. On first boot the API creates its own schema (see "EnsureCreated vs
migrations" below) so there's nothing else to run.

- Swagger UI: http://localhost:8080/swagger
- Liveness: http://localhost:8080/health/live
- Readiness (checks DB): http://localhost:8080/health/ready

### Getting a token

There's no real identity provider here (see "Auth" below). Mint a JWT for any customer id:

```bash
curl -X POST http://localhost:8080/api/auth/token \
  -H "Content-Type: application/json" \
  -d '{"customerId":"cust-001"}'
```

Use the returned `accessToken` as a `Bearer` token, either via the "Authorize" button in
Swagger or an `Authorization: Bearer <token>` header on requests.

### Typical flow

```bash
TOKEN=<paste access token here>

# Create a wallet for cust-001
curl -X POST http://localhost:8080/api/wallets \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"customerId":"cust-001"}'

# Credit it (simulates an inbound NIP transfer), amounts are always in kobo
curl -X POST http://localhost:8080/api/wallets/<walletId>/credit \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"amountKobo": 500000, "description": "Salary"}'

# Transfer — Idempotency-Key is required
curl -X POST http://localhost:8080/api/wallets/transfer \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -H "Idempotency-Key: $(uuidgen)" \
  -d '{"fromWalletId":"...","toWalletId":"...","amountKobo":10000,"description":"rent"}'

# Statement
curl http://localhost:8080/api/wallets/<walletId>/statement?page=1&pageSize=20 \
  -H "Authorization: Bearer $TOKEN"
```

## Architecture

Four projects, dependencies flowing one way:

```
NovaWallet.Domain          entities with behavior, exceptions, pure policies — no dependencies
NovaWallet.Application     use-case orchestration (WalletService) + repository/UoW interfaces — depends on Domain
NovaWallet.Infrastructure  EF Core implementations of those interfaces — depends on Domain, Application
NovaWallet.Api             controllers, auth, Problem Details — depends on Infrastructure, Application
```

### Domain rules live in Domain, not in a service class

`Wallet` is not an anemic property bag — `BalanceKobo` has a private setter, and the
*only* ways to change it are `Credit(amount)` and `Debit(amount)`. `Debit` is the one
method in the entire codebase that can reduce a balance, and it refuses to let it go
negative. This means the "never negative" invariant can't be bypassed by a service
class forgetting to check first — there's no other way to touch the field at all.

The same pattern applies elsewhere:
- `Wallet.EnsureOwnedBy(customerId)` — the ownership check for transfers.
- `DailyTransferLimitPolicy.EnsureWithinLimit(alreadySentToday, amount)` — a pure
  static function; it doesn't query anything itself, so it's trivially unit-testable
  with no database.
- `WatClock.GetDayBoundsUtc(nowUtc)` — pure WAT (UTC+1, no DST) day-boundary math.
- `IdempotencyRecord.Resolve(requestHash)` — decides Replay/Conflict/InProgress from
  data already on the record, with no I/O and no knowledge of Application's DTOs.

`WalletService` (in Application) is left with exactly one job: orchestrate the
*shape* of each workflow — begin transaction, lock, load, ask the domain objects to
do their thing, persist, commit. It doesn't contain a single business rule itself.

### A small repository / unit-of-work boundary — and why

`IWalletRepository` and `IUnitOfWork` (defined in Application, implemented in
Infrastructure as `EfWalletRepository` / `EfUnitOfWork`) are the seam between them.
This is a deliberately narrow abstraction — one repository, one unit of work, not a
generic-repository-per-entity ceremony — added for two concrete reasons rather than
as a default habit:

1. **It's what makes the Domain encapsulation above possible.** If `WalletService`
   held a `NovaWalletDbContext` directly, Application would have to reference EF Core
   and Postgres-specific types, which defeats the point of pulling business rules out
   into a Domain project that's supposed to have zero infrastructure dependencies.
2. **It's what makes Application independently testable** (see below) — orchestration
   logic can be verified against an in-memory fake with no database at all, while the
   locking behaviour it depends on is proven separately against real Postgres.

The interface is intentionally small: `LockAsync` / `LockOrderedAsync` hide that
locking means `SELECT ... FOR UPDATE` at all; `TryAddIdempotencyRecordAsync` returns
`bool` rather than letting a `DbUpdateException` from a unique-constraint violation
leak into Application code that shouldn't need to know constraints exist.

### Concurrency safety

Every balance mutation runs in one DB transaction (`IUnitOfWork`). Before reading a
wallet's balance, the transaction takes a row lock with `SELECT ... FOR UPDATE`.
Concurrent requests against the *same* wallet therefore queue at the database, not in
application memory — this is correct even across multiple instances of the API, which
an in-process lock would not be.

Transfers touch two wallets, so both rows are locked **in a fixed, ascending order**
(`LockOrderedAsync`, `ORDER BY id`) before either is read. This prevents the classic
deadlock where Transfer A (wallet 1 → wallet 2) and Transfer B (wallet 2 → wallet 1)
each hold one lock and wait on the other.

The balance check (`Wallet.Debit` throwing `InsufficientFundsException`) and the
daily-limit check both happen *inside* the same locked transaction as the write, so
there's no window for another request to slip in between "check" and "act".

### Idempotency

`idempotency_records.key` has a unique database constraint. A transfer request:

1. Looks up the key. If found, `IdempotencyRecord.Resolve` decides: same hash and
   completed → replay the stored response; same hash and still processing → 409
   "in progress"; different hash → 409 conflict.
2. If not found, attempts to insert a `Processing` row. `TryAddIdempotencyRecordAsync`
   returns `false` if the insert loses a race on the unique constraint — **note that a
   failed statement aborts the entire Postgres transaction**, so the code rolls back
   immediately on `false` before doing anything else, then re-reads and resolves as in
   step 1.
3. Otherwise, performs the transfer and marks the record `Completed` with the
   serialized response — all inside the transfer's own transaction, so a crash
   mid-transfer rolls the idempotency record back too rather than leaving it stuck at
   `Processing` forever.

### Daily limit

₦500,000/day per wallet (`DailyTransferLimitPolicy`), computed against the start/end
of the *current day in WAT* (`WatClock`, UTC+1, no DST). The sum of today's transfers
is fetched inside the same locked transaction as the transfer it's checking, so it
can't be bypassed by a race.

### Auth

Endpoints require a JWT bearer token. `POST /api/auth/token` is a **mock issuer** — it
signs a token for whatever `customerId` you send it, with no credential check at all,
per the brief ("a simplified/mock issuer is fine"). The transfer endpoint additionally
checks (`Wallet.EnsureOwnedBy`) that the caller's `customerId` claim matches the source
wallet's owner; `credit` does not enforce ownership, on the assumption it simulates a
trusted inbound-NIP webhook rather than an end-user action.

### Errors: RFC 7807 via .NET 8's built-in pipeline

Errors are RFC 7807 Problem Details, produced by `DomainExceptionHandler`
(`IExceptionHandler`) plus `builder.Services.AddProblemDetails()` — the framework's own
.NET 8 extension point for this, rather than a hand-rolled try/catch middleware. This
matters for two reasons: it composes with `[ApiController]`'s automatic
`ValidationProblemDetails` for model-binding errors (so a malformed request body is
*also* RFC 7807-shaped, not just our own thrown exceptions), and `CustomizeProblemDetails`
lets us attach a trace id to every problem response from one place.

Every action is annotated with `[ProducesResponseType]` for each status code it can
actually return, so the generated OpenAPI spec documents the real contract (including
error shapes) rather than just the happy path.

**On a generic `ApiResponse<T>` success wrapper:** I deliberately did *not* add one
(e.g. `{ success: true, data: {...} }`) around successful responses. Reasoning: the
brief already asks for RFC 7807 on errors, and REST/ASP.NET Core convention is that
success is conveyed by the HTTP status code and the resource is returned directly —
wrapping successes but not errors means two different envelope shapes in the same API,
and wrapping *everything* (including errors) would compete with Problem Details rather
than complement it. It also costs real OpenAPI/Swagger ergonomics: every response type
becomes `ApiResponse<WalletResponse>` instead of `WalletResponse`, and generated
clients have to unwrap a layer for no informational gain. If your team has a standing
convention that expects an envelope, it's a small, mechanical change from here.

## Testing — one project per layer

```bash
dotnet test
```

- **`NovaWallet.Domain.Tests`** — pure unit tests for `Wallet`, `IdempotencyRecord`,
  `DailyTransferLimitPolicy`, `WatClock`. No mocks, no database, no async waiting;
  these run in milliseconds and pin down the actual business rules.
- **`NovaWallet.Application.Tests`** — tests `WalletService`'s orchestration against
  `FakeWalletRepository`, a small in-memory stand-in for `IWalletRepository`. Deliberately
  a *fake*, not a mock: these tests care about resulting state (balances, ledger rows)
  after a use case runs, which a stateful fake represents more naturally than
  interaction-verification would. No real locking is exercised here — there's nothing
  concurrent in-process to lock against.
- **`NovaWallet.Infrastructure.Tests`** — the same workflows, wired to the *real*
  `EfWalletRepository` / `EfUnitOfWork` against a disposable Postgres container
  ([Testcontainers](https://dotnet.testcontainers.org/)). This is where the
  concurrency test lives, because concurrency safety is specifically a claim about
  Postgres's row-locking behaviour — an in-memory or SQLite provider doesn't implement
  real transactions/row locks the same way, so a concurrency test built on either would
  pass even with the `FOR UPDATE` locking deleted. Requires Docker running locally.
- **`NovaWallet.Api.Tests`** — `WebApplicationFactory<Program>` tests against the real
  middleware pipeline (real JWT auth, real Problem Details mapping, real Swagger), with
  `IWalletService` swapped for a controllable stub and the DbContext swapped for EF
  Core's InMemory provider (purely so startup's `EnsureCreated()` succeeds — no test
  here asserts anything about persistence). Answers "given the service returns X, does
  the HTTP layer respond with the right status code and shape?" — a different question
  from whether the business logic itself is correct.

## Trade-offs / things I'd do differently with more time

- **EnsureCreated, not EF migrations.** The API creates its schema on startup rather
  than shipping migration files, so `docker compose up` works regardless of the
  machine it's run on. In a real codebase this would be `dotnet ef migrations` from
  day one.
- **Outbox pattern** (stretch goal) is not implemented — transfers don't publish a
  `TransferCompleted` event.
- **Rate limiting** is a simple fixed-window limiter (10 req/10s per customer) on the
  transfer endpoint — enough to demonstrate the middleware, not tuned for production
  traffic shapes.
- **KYC tiers, BVN/NIN, USSD** are out of scope for this ledger microservice as
  specified — noted here only so it's clear they weren't missed by oversight.
- **Single currency (NGN)** — the `Currency` column exists on `Wallet` but nothing
  enforces or converts between currencies.

## Assumptions

- "Concurrency-safe" is interpreted per the brief as: correct under concurrent requests
  sharing one Postgres database (the `docker compose` topology). Row-level locking
  gives this correctly across multiple API instances too, since the lock lives in
  Postgres, not in process memory.
- The daily limit resets at midnight **WAT**, not UTC, per the brief's operating context.
- `Idempotency-Key` is required on transfer and rejected with `400` if absent, rather
  than silently proceeding without idempotency protection.
