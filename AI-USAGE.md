# AI Usage

## AI strategy

**Tool:** Claude Code (Claude Opus 5.5) in the Claude desktop app, for planning, code, tests, debugging and a first draft of the docs.

**Context given to the AI:**
- The assignment PDF, as the single source of requirements.
- The local toolchain, checked before planning. This showed Docker was missing, which changed how verification had to be planned.

**How the work was structured:**
- **Plan before code.** The AI ran in plan mode, read the brief and wrote a plan covering the data model, concurrency, expiry, caching, messaging and HTTP semantics. I reviewed and approved it before any code was written.
- **Architecture from the brief.** The plan fixed the required `src/` layering up front, so later steps had to fit it.
- **Inside-out build, one commit per layer:** contracts, domain and tests first, then infrastructure, API, Docker, frontend and docs. The domain was tested against mocks before any infrastructure existed, which kept the interfaces honest.

### How I steered the work

The brief set the starting point, and my follow-up instructions changed the result:

- **Inventory domain.** I asked the AI to act as an inventory expert and add approximate ship date, serial number, part number and related fields, then a last-updated date and a list of the individual items behind each product.
- **Scope control.** I told it to add these with no new API endpoints, and to re-check the API against the PDF.
- **Security.** I raised that security had been missed and suggested HMAC.
- **User interface.** I asked for a modern open-source React theme in place of the plain first version.
- **Documentation.** I asked for architecture diagrams in a `doc` folder.
- **Delivery pipeline.** I asked for a deployment pipeline that exports artifacts, with instructions in the README.
- **Final review.** I asked for a review of the finished work against every requirement in the PDF.

## Human audit

### Decisions made by me

- **Verification:** with Docker missing, the AI offered to skip Docker verification. I chose to install Docker so the stack could be tested for real, because `docker-compose up --build` is a stated requirement and an unrun compose file proves nothing.
- **Delivery:** I chose local commits and pushing myself, over giving the AI access to publish to GitHub.
- **Item details:** I asked for approximate ship date, serial number, part number and related fields, with no new endpoints. The AI put them inside the existing responses and modelled serials per physical unit.
- **Item list and interface:** I asked for a last-updated date, for a click on a product to show every item with its serial number and details, and for a modern open-source React theme. The AI chose MUI, and put the item list inside `GET /api/inventory` to respect my no-new-endpoints decision, documenting when that would stop being suitable.
- **Security:** I raised that security had been missed and suggested HMAC. The AI pointed out that a browser can't hold a secret, that HMAC doesn't identify people, and that reviewers' plain `curl` calls could fail if it was always on. I chose to keep it off under docker-compose behind one switch, while the code defaults to on.
- **API scope:** I had the AI compare the running API against the PDF. That showed one endpoint the brief doesn't list, `GET /api/holds`. The AI first offered to replace it by storing hold IDs in the browser; I questioned that and asked about Redis. The AI explained a browser can't reach Redis directly, so it would still need an endpoint. I decided to keep the status list, giving 5 endpoints.

### Suggestions I rejected or changed

| AI suggestion | What I did | Why |
|---|---|---|
| Remove `GET /api/holds` and have the browser remember hold IDs, so the API exactly matched the PDF's four endpoints | Rejected it and kept the status list, making five endpoints | The Active Holds List needs a real source of data. I questioned storing IDs in the browser, and the list would otherwise hide holds placed in any other browser |
| Put the new item fields on a feature branch | Rejected it and kept the work on `main` | I wanted it included in the same line of work |
| Show inventory at product level only | Changed it, so that selecting a product lists every item with its serial number and details | A product total doesn't show which physical items are available or on hold |
| Plain CSS for the interface | Changed it to MUI with light and dark themes | I wanted a more modern interface |
| No security, because the brief doesn't expect authentication | Changed it by adding optional HMAC request signing | I didn't want the API left completely open. I accepted the AI's advice to keep it off under docker-compose, so that plain calls still work as the brief describes |
| Add the AI as a co-author on every commit | Rejected it, and the commits are under my name | I own this submission, and this document already discloses how AI was used |

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
8. **Old-format cache entries after a redeploy.** Adding `units` to the inventory response broke a read straight after restarting the API: Redis still held the previous format, so `units` came back null until the 30-second expiry. Unit tests couldn't see this because the mocks have no old data. Cache keys now carry a schema version.
9. **A check that could never fail.** An end-to-end check compared held counts with assigned units, but queried `holdId` when the stored field is `HoldId`, so it always counted zero. It passed only because nothing was held at the time. It failed once a real hold was active, which led to reading the database directly: the data was correct and the check was wrong. It now reconciles every product against both units and active holds.
10. **A helper that broke on Windows.** Git Bash rewrites `/api/inventory` into a Windows file path, so the first version of `signed-request.mjs` failed. Paths are now given without the leading slash.
11. **An invented API name.** The AI imported an icon, `DeleteOutline`, that the installed MUI version doesn't have. The type check failed the build, and the real name was confirmed by listing the package's files.
12. **A pipeline step that could hide failures.** The first draft piped the end-to-end run through `tee` under GitHub's default shell, which has no `pipefail`, so failing tests would have shown as a success. The linter didn't flag it; it was found by reviewing the file. The workflow now names `bash` as its shell, which turns `pipefail` on.
13. **A failure only a real pipeline run could show.** The first GitHub Actions run failed even though the same stack worked locally, and I reported the error. MongoDB's log said it refuses to start on Linux kernels 6.19 to 7.0.13, which GitHub's newer `ubuntu-latest` runner has. The fix pins the runner to Ubuntu 24.04. Moving to `mongo:8.2` was rejected, because it lacks the startup check and that doesn't show the underlying problem is fixed; an unofficial `GLIBC_TUNABLES` workaround was rejected as unverified. The limitation is documented in the README.

## Verification

**Unit tests.** The AI wrote the suite alongside the domain code, with cases chosen from the brief's edge cases: non-existent, already-released and expired holds, partial failures, concurrency, serial number handling, ship dates and request signing. 88 NUnit tests, all mocked and needing no infrastructure.

**Limits of the unit tests.** The parallel "no overselling" test uses a mock that simulates an atomic decrement, so it proves the service logic and not that MongoDB behaves atomically. That is why a separate end-to-end check exists.

**End-to-end check.** `scripts/e2e-check.mjs` runs against the real stack: every status code, N+40 parallel holds on N units, 20 parallel releases of one hold, sweeper expiry, Redis invalidation, and RabbitMQ event contents.

The script passes against the real stack, including 43 parallel holds on 3 units (exactly 3 won, stock reached 0 and never went negative) and 20 parallel releases (exactly one succeeded, stock restored once). For serial-tracked items, parallel holds on 10 units gave exactly 5 winning pairs, no serial was given to two holds, and the units assigned always matched the held counts.

**Security testing.** With signing on, the script confirms that unsigned, wrongly signed, tampered, stale, replayed and oversized requests are each rejected, and that of 20 parallel copies of one signed request exactly one succeeds. A signature test vector computed separately in Node matches the C# code, and real traffic through nginx proves its JavaScript signer agrees as well. The API refuses to start with signing on and no secrets.

**Failure testing.** Containers were stopped by hand while the API ran. With Redis down, reads, creates and releases all succeeded and the cache reconnected on its own afterwards. With RabbitMQ down, creating a hold succeeded in about 4 seconds, that event was lost as documented, and publishing resumed when the broker returned. Restarting the API kept live stock rather than re-seeding it.

**Deployment testing.** The deployment script was run locally against images built on this machine: a first deployment, an upgrade that kept an active hold, and a deliberately broken version that rolled back to the previous one with the hold intact. The workflow file passed `actionlint`.

**Frontend.** Driven in a browser against the real stack: placing a hold, over-requesting (the API's message appears), release through the confirmation dialog, inventory and its last-updated time changing with no refresh, the item list with search, the dark theme, and phone width. The countdown reaching zero and stock returning were also watched on the real app, with signing on. An earlier version of the page was tested with a multi-product hold.

### What I checked myself

- **I used the running app.** When `http://localhost:3000` failed for me, I reported it. The cause was that the stack had been stopped after testing, so I learned the app only runs while Docker and its containers are up.
- **I looked for the signature.** After HMAC was added, I checked the browser's requests and couldn't see it. Following that up showed two things: signing was off, as I had chosen, and even when on it's added by nginx, after the request leaves the browser. I then had it turned on and confirmed that an unsigned API call gets a 401.
- **I compared the work with the brief.** Asking for the API to be checked against the PDF exposed the extra endpoint. Asking for a final review against every requirement exposed unfinished documentation and a typo in my git email.
