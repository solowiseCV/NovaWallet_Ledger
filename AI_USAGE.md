# AI Usage

## Tools used

- **Claude (Anthropic)**, via chat, for the bulk of the initial scaffolding: project
  layout, the EF Core model, the `WalletService` concurrency/idempotency logic, the
  test suite structure, and this documentation. I treated it as a fast first-draft
  generator and a design-review partner, not as an unsupervised author — every piece
  below was read, and in several cases rejected or corrected, before being accepted.

## Example prompts and what came back

1. **"How should I make wallet transfers concurrency-safe in EF Core against
   Postgres?"** — The first answer suggested an optimistic-concurrency `RowVersion`
   token with a catch-and-retry loop. That's a legitimate pattern, but for a
   two-row operation (debit + credit) with a daily-limit check in the middle, retry
   logic gets messy fast and it's easy to retry only half the work. I asked for a
   pessimistic alternative and got the `SELECT ... FOR UPDATE` + fixed lock-ordering
   design that's in the code now — simpler to reason about and easier to explain live.

2. **"Design an idempotency-key mechanism for the transfer endpoint."** — The first
   draft was a **check-then-insert** pattern: `SELECT` for an existing key, and if
   none exists, `INSERT` a new "processing" record. That's a textbook TOCTOU
   (time-of-check-to-time-of-use) race: two requests with the same key can both pass
   the `SELECT` before either finishes its `INSERT`, and both proceed to double-debit
   the wallet — precisely the bug the requirement exists to catch. I caught this by
   asking "what happens if two requests with the same key arrive at literally the same
   moment?" and the honest answer was "it double-processes." The fix in the code relies
   on the **unique constraint** on `idempotency_records.key` doing the actual work:
   the losing request's `INSERT` fails with a constraint violation, and only then does
   it fall back to reading (and replaying) the winner's result. The database, not
   application logic, is what makes this safe.

3. **"Write a test that proves transfers can't create a negative balance under
   concurrent load."** — The first version used EF Core's `InMemory` provider so the
   test would run fast with no external dependencies. I pushed back on this myself
   during review: `InMemory` doesn't implement real transactions or row locking, so a
   test built on it would pass even if I deleted the `FOR UPDATE` locking entirely —
   it would be testing nothing. I switched the concurrency and idempotency tests to
   Testcontainers against a real disposable Postgres container, which is slower but
   actually exercises the locking behaviour the service depends on.

## A concrete case the AI got wrong, unsafe, or naive

The idempotency check-then-insert race in prompt #2 above is the clearest example: it's
the kind of bug that passes every manual test (nobody manually fires two identical
requests in the same millisecond) and only shows up in production under real concurrent
load — exactly the failure mode a financial ledger can't afford. It's also a good
illustration of a broader pattern I watched for throughout: AI-suggested solutions
tend to be correct for the *sequential* case and quietly assume the environment won't
interleave two requests at the worst possible moment. For a payments system, that
assumption is the whole ballgame, so anywhere the AI proposed "check X, then do Y," I
treated it as a probable race until I'd convinced myself otherwise (or, as here, pushed
the actual safety property down into a database constraint instead of trusting
application-level sequencing at all).
