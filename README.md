# NovaWallet Ledger Service

A simplified wallet ledger for FirstBank NovaPay's NovaWallet module, built in C# / .NET 8.

## Running it

```bash
# macOS/Linux
cp .env.example .env
# PowerShell
Copy-Item .env.example .env
# Edit .env and replace every placeholder before continuing.
docker compose up --build
```

The local `.env` file supplies the database credentials and JWT signing key and is ignored
by Git. The compose stack starts Postgres, waits for it to be healthy, then builds and
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

## Running the tests

```bash
dotnet test
```

The concurrency and idempotency tests use [Testcontainers](https://dotnet.testcontainers.org/)
to spin up a real disposable PostgreSQL container per test run — Docker must be running
locally, but nothing else needs to be set up. This is deliberate: the service's
concurrency safety depends on Postgres row-level locking (`SELECT ... FOR UPDATE`),
which an in-memory or SQLite provider does not faithfully emulate. Testing against
anything else would give false confidence. See `AI_USAGE.md` for how this was caught.

## Architecture

Four projects, dependencies flowing one way:

```
NovaWallet.Domain          entities, enums, exceptions — no dependencies
NovaWallet.Application     DTOs + service interfaces — depends on Domain
NovaWallet.Infrastructure  EF Core DbContext + WalletService — depends on Domain, Application
NovaWallet.Api             controllers, auth, middleware — depends on Infrastructure, Application
```

`Wallet` and `WalletTransferPolicy` in Domain own the invariant rules: valid amounts,
wallet ownership, sufficient funds, distinct transfer parties, and the daily limit.
`WalletService` in Infrastructure orchestrates persistence-specific concerns such as
Postgres row locks, transactions, idempotency storage, and statement queries. There's no repository
abstraction over EF Core in a project this size — `DbContext` already *is* the
unit-of-work/repository pattern combined, and adding another layer over it would just be
indirection with no present benefit. If a second datastore or read-model were ever needed,
that's when I'd introduce one.

### Data model

- **wallets** — id, customer_id, balance_kobo (bigint), currency, created_at
- **ledger_transactions** — the queryable statement: one row per posted movement
  (Credit / TransferOut / TransferIn), with `balance_after_kobo` snapshotted at post time
- **audit_logs** — a *separate*, append-only table recording every mutation
  (before/after balance, actor, metadata). The `AppendOnlyAuditInterceptor` throws if
  application code ever tries to UPDATE or DELETE an audit row via EF Core, as
  defence-in-depth on top of the "we just never call Update/Remove on it" discipline.
  In a real deployment I'd also `REVOKE UPDATE, DELETE` at the DB grant level for the
  app's role.
- **idempotency_records** — keyed by the `Idempotency-Key` header value; see below.

### Concurrency safety

Every balance mutation runs in one DB transaction. Before reading a wallet's balance,
the transaction takes a row lock with `SELECT ... FOR UPDATE`. Concurrent requests
against the *same* wallet therefore queue at the database, not in application memory —
this is correct even across multiple instances of the API, which an in-process `lock`
would not be.

Transfers touch two wallets, so both rows are locked **in a fixed, ascending order**
(`ORDER BY id`) before either is read. This prevents the classic deadlock where
Transfer A (wallet 1 → wallet 2) and Transfer B (wallet 2 → wallet 1) each hold one
lock and wait on the other.

The balance check (`from.Balance >= amount`) and the daily-limit check both happen
*inside* the same locked transaction as the write, so there's no window for another
request to slip in between "check" and "act".

### Idempotency

`idempotency_records.key` has a unique constraint. A transfer request:

1. Looks up the key. If found and the stored request hash matches, replay the stored
   response. If found with a different hash, `409` conflict.
2. If not found, insert a `Processing` row for the key. If that insert fails on the
   unique constraint (another request won the race), fall back to step 1's lookup.
3. Perform the transfer, then update the same row to `Completed` with the serialized
   response — all inside the transfer's own transaction, so a crash mid-transfer rolls
   the idempotency record back too rather than leaving it stuck at `Processing` forever.

### Daily limit

₦500,000/day per wallet, computed as the sum of `TransferOut` transactions posted
between the start and end of the *current day in WAT* (UTC+1, no DST — Nigeria doesn't
observe daylight saving). That sum is computed inside the same locked transaction as
the transfer it's checking, so it can't be bypassed by a race.

### Auth

Endpoints require a JWT bearer token. `POST /api/auth/token` is a **mock issuer** — it
signs a token for whatever `customerId` you send it, with no credential check at all.
That's intentional per the brief ("a simplified/mock issuer is fine — the point is the
middleware and claims handling"). The transfer endpoint additionally checks that the
caller's `customerId` claim matches the source wallet's owner; `credit` does not enforce
ownership, on the assumption it simulates a trusted inbound-NIP webhook rather than an
end-user action — in production that would sit behind a service credential, not a
customer JWT, entirely.

### Errors

All errors are RFC 7807 Problem Details (`application/problem+json`), mapped centrally
in `ExceptionHandlingMiddleware` from a small set of domain exceptions
(`WalletNotFoundException`, `InsufficientFundsException`, `DailyLimitExceededException`,
`IdempotencyKeyConflictException`, etc.) to the right HTTP status.

Successful responses use a consistent `{ success, data, traceId }` envelope. Controller
actions explicitly declare their request sources and OpenAPI response types; validation
and domain failures are documented as Problem Details responses.

### Tests

Tests are separated by layer:

- `NovaWallet.Domain.Tests` tests wallet invariants without a database.
- `NovaWallet.Application.Tests` tests application contracts.
- `NovaWallet.Api.Tests` tests response and API metadata contracts.
- `NovaWallet.Infrastructure.Tests` retains the Postgres/Testcontainers integration,
  idempotency, and concurrency tests.

## Trade-offs / things I'd do differently with more time

- **EnsureCreated, not EF migrations.** For a 48–72 hour take-home I chose to have the
  API create its schema on startup rather than commit migration files, so `docker
  compose up` is guaranteed to work regardless of the machine it's run on. In a real
  codebase this would be `dotnet ef migrations` from day one.
- **Outbox pattern** (stretch goal) is not implemented — transfers don't publish a
  `TransferCompleted` event. Given the time box I prioritized the hard constraints
  (concurrency, idempotency, the daily limit, audit trail) over this.
- **Rate limiting** is a simple fixed-window limiter (10 req/10s per customer) on the
  transfer endpoint — enough to demonstrate the middleware, not tuned for production
  traffic shapes.
- **KYC tiers, BVN/NIN, USSD** are out of scope for this ledger microservice as
  specified — noted here only so it's clear they weren't missed by oversight.
- **Single currency (NGN)** — the `Currency` column exists on `Wallet` but nothing
  enforces or converts between currencies; multi-currency wallets would need that
  addressed explicitly.

## Assumptions

- "Concurrency-safe" is interpreted per the brief as: correct under concurrent requests
  to a single instance sharing one Postgres database (the `docker compose` topology).
  Row-level locking gives this correctly across multiple API instances too, since the
  lock lives in Postgres, not in process memory.
- The daily limit resets at midnight **WAT**, not UTC, per the brief's operating context.
- `Idempotency-Key` is required on transfer and rejected with `400` if absent, rather
  than silently proceeding without idempotency protection.
