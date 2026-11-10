# AI usage

**Tool used:** an agentic AI assistant (Arena.ai agent mode, which routes to different underlying
models) with a sandbox that could install .NET 8, PostgreSQL and Node, so every change below was
compiled, run and tested — not just generated.

## Where AI was used

| Area | How |
|---|---|
| Project scaffolding | Initial folder layout, `.csproj`/`package.json` skeletons, Dockerfiles, `docker-compose.yml` |
| Backend | Draft of the minimal-API endpoints, the Dapper data access and the locking transaction in `AdmissionService`, the expiry background worker, the xUnit fixtures |
| Frontend | Component structure, `api.ts` client, the polling hook and the CSS |
| Tests | Backend concurrency tests and the Vitest component tests |
| Documents | First drafts of `README.md` and `ASSUMPTIONS.md` |

I designed the solution, decided the rules (lock strategy, idempotency, which HTTP code each
edge case returns), reviewed every file, ran everything locally and fixed what did not work.

## Where the AI was wrong — and how I caught it

1. **Connection-pool exhaustion under load (found by the concurrency test).**
   The first version opened an `NpgsqlConnection` per request with the default pool size of 100 per
   host. With 60 simultaneous applications for one class the waiting transactions each held a
   connection, and PostgreSQL answered `53300: sorry, too many clients already` — 20 of the 60
   requests came back `500`. *How I caught it:* the concurrency test asserted the **sum** of
   statuses, so the missing 20 requests showed up immediately. *Fix:* cap the pool per process
   (`DB_MAX_POOL_SIZE`, default 10 in code / 20 in Compose), raise `max_connections` for the
   database container, and share one test fixture instead of starting a host per test.

2. **A test that "proved" a two-process guarantee in one process.**
   The AI's first concurrency test called a single `WebApplicationFactory`. That only exercises
   threads in one process. *Fix:* the fixture now boots **two** hosts against **one** database
   (`Two_backend_instances_sharing_one_database_still_respect_the_seat_limit`), which is what the
   brief actually asks about.

3. **Wrong type for the locked class row.** Dapper was asked to materialise a `ValueTuple` from a
   `SELECT ... FOR UPDATE`, plus a nullable dereference the compiler flagged (`CS8602`). *Fix:*
   small records (`ClassRow`, `SeatCounts`) and a proper null check.

4. **Fragile transaction cleanup.** The first drafts tried to roll back by sniffing
   `tx.Connection is not null`, which throws once a transaction is committed. *Fix:* an explicit
   `completed` flag, so a business exception after `COMMIT` (e.g. "hold expired") is not rolled
   back and does not hide the real error.

5. **Frontend tests that failed for the wrong reason.** Two tests reused one mocked `Response`
   object (`Body is unusable`) and one matched `/(cancelled)/i` twice in the same card, so the
   assertion was ambiguous. *Fix:* fresh response per call, exact-text matchers. Left as a
   reminder that a failing test is not automatically a product bug.

6. **Build-config details:** `allowedHosts` is a Vite 6 option (this project pins Vite 5),
   `process` types were missing for `vite.config.ts`, and a `useRef` import was left unused.
   All caught by `tsc -b` and removed.

## What I checked by hand (not trusted to the AI)

* Every endpoint with `curl`: contract fields, status codes (200/201/204/400/404/409), error
  bodies, expiry behaviour with a short `HOLD_DURATION_SECONDS`.
* The whole UI in a browser: apply → countdown → confirm/cancel, expiry without reload, seat
  numbers updating by themselves, error messages when the backend is stopped, identity surviving a
  refresh.
* The concurrency behaviour with an independent load (120 simultaneous requests for 5 seats →
  exactly 5 × `201`, 115 × `409`, counts consistent).
* `docker compose up --build` from a clean state. *(Add the date you verified this.)*
