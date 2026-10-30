# AI Usage

<!--
DRAFT, written by the AI from the session record. Everything below happened, but the
judgement being assessed here is yours. Before submitting:
  1. Replace each "YOUR INPUT" block with your own words, or delete it.
  2. Remove anything you can't explain in a follow-up interview.
  3. Delete this comment.
-->

## AI strategy

**Tool:** Claude Code (Claude Opus 5.5) in the Claude desktop app, for planning, code, tests, debugging and a first draft of the docs.

**Context given to the AI:**
- The assignment PDF, as the single source of requirements.
- The local toolchain, checked before planning. This showed Docker was missing, which changed how verification had to be planned.

**How the work was structured:**
- **Plan before code.** The AI ran in plan mode, read the brief and wrote a plan covering the data model, concurrency, expiry, caching, messaging and HTTP semantics. I reviewed and approved it before any code was written.
- **Architecture from the brief.** The plan fixed the required `src/` layering up front, so later steps had to fit it.
- **Inside-out build, one commit per layer:** contracts, domain and tests first, then infrastructure, API, Docker, frontend and docs. The domain was tested against mocks before any infrastructure existed, which kept the interfaces honest.

> **YOUR INPUT:** What did you decide or steer beyond approving the plan? Mention any prompt or instruction that changed the outcome.

## Human audit

### Decisions made by me

- **Verification:** with Docker missing, the AI offered to skip Docker verification. I chose to install Docker so the stack could be tested for real, because `docker-compose up --build` is a stated requirement and an unrun compose file proves nothing.
- **Delivery:** I chose local commits and pushing myself, over giving the AI access to publish to GitHub.

> **YOUR INPUT:** Add suggestions you personally **rejected or changed**, and why. The brief asks for this specifically, and it's the part only you can write. Reading the code and README with a critical eye should produce some. Candidates to form your own view on:
> - Is the crash window in the README an acceptable trade-off, or would you use transactions?
> - Should a failed event publish really not fail the request?
> - Is `GET /api/holds` listing every customer's holds acceptable?
> - Is 410 the right status for releasing an expired hold, compared with 409?

### AI proposals in the approved plan

| Proposal | Outcome | Reasoning |
|---|---|---|
| Atomic conditional `$inc` for reservations | Accepted | The check and deduct are one operation, so there's no race window |
| Compare-and-set on hold status as the gate for restocking | Accepted | Prevents double restock when a release races the sweeper |
| Mongo TTL index for expiry | Rejected | It would delete holds without restoring stock or emitting `HoldExpired` |
| Multi-document transactions | Rejected | Needs a replica set; the remaining crash window is documented instead |
| Redux for frontend state | Rejected for TanStack Query | Almost all state is server state |

### Problems caught during review and verification

These were errors in the AI's own output, found while checking it. They're the concrete reason nothing was accepted without running it.

1. **A failing build was committed.** A script ran the build through a pipe, then committed regardless, so the commit "Add Mongo, Redis and RabbitMQ infrastructure and the Web API" went in despite an `MSB3030` error. It was noticed in the output and investigated rather than rerun. The cause was the 260-character Windows path limit (a 181-character workspace path plus build output), not the code, so the source in that commit was fine. A clean build was then confirmed by redirecting the output to a short path, and the README now warns about this.
2. **A hidden error.** An output filter used to shorten build logs stripped the real compiler error text, so the first look showed only fragments. Rerunning without it revealed a missing package, `Microsoft.Extensions.Options.DataAnnotations`.
3. **A known-vulnerable dependency.** The template's OpenAPI package pulled in `Microsoft.OpenApi` 2.0.0, flagged high severity (NU1903) at build. Upgrading the direct package removed it, confirmed with `dotnet list package --vulnerable`.
4. **Ambiguous overloads.** Two cache helpers shared the name `TryCacheAsync`, and passing a method group could have been ambiguous between them. The read helper was renamed `ReadCacheAsync` before the first compile.
5. **A stray import.** A test file imported `StackExchange.Redis`, which the test project doesn't reference and shouldn't need.
6. **A Redis outage stalled every request.** The design said a cache outage must not hurt requests, and the mocked unit tests agreed, since a mock throws instantly. Stopping the real Redis container showed that requests still succeeded but took about 11 seconds, because StackExchange.Redis queues commands while reconnecting by default. Setting `BacklogPolicy.FailFast` brought them to about 20 ms. No unit test here could have caught this; only breaking the real dependency did.
7. **A bug in the check script itself.** The first end-to-end run reported `HoldExpired` missing. Reading the queue directly showed the events were there, and that the script was trusting RabbitMQ's sampled queue depth, which lags behind recent publishes. The script was fixed rather than the service, after confirming which of the two was wrong.

## Verification

**Unit tests.** The AI wrote the suite alongside the domain code, with cases chosen from the brief's edge cases: non-existent, already-released and expired holds, partial failures, and concurrency. 40 NUnit tests, all mocked and needing no infrastructure.

**Limits of the unit tests.** The parallel "no overselling" test uses a mock that simulates an atomic decrement, so it proves the service logic and not that MongoDB behaves atomically. That is why a separate end-to-end check exists.

**End-to-end check.** `scripts/e2e-check.mjs` runs against the real stack: every status code, N+40 parallel holds on N units, 20 parallel releases of one hold, sweeper expiry, Redis invalidation, and RabbitMQ event contents.

The script passes against the real stack, including 43 parallel holds on 3 units (exactly 3 won, stock reached 0 and never went negative) and 20 parallel releases (exactly one succeeded, stock restored once).

**Failure testing.** Containers were stopped by hand while the API ran. With Redis down, reads, creates and releases all succeeded and the cache reconnected on its own afterwards. With RabbitMQ down, creating a hold succeeded in about 4 seconds, that event was lost as documented, and publishing resumed when the broker returned. Restarting the API kept live stock rather than re-seeding it.

**Frontend.** Driven in a browser against the real stack: over-requesting (the API's message appears), a multi-product hold, release with confirmation, and inventory updating with no refresh. The countdown reaching zero was driven against a throwaway mock API with short holds, so that part verified UI behaviour only; real expiry was verified through the API.

> **YOUR INPUT:** What did you check yourself? For example, which code you read line by line, or tests you broke on purpose to confirm they fail.
