# Assumptions

The brief deliberately leaves gaps ("where the brief is not explicit, decide what is
reasonable and write down why"). This file lists every decision I made, and — where it matters
— the alternative I rejected.

---

## 1. Domain model

**A "class" means one class *and* one shift.** The brief says a class can have several shifts and
that each shift is a separate class with its own seats, so `school_classes` is the only grouping
and `totalSeats` always belongs to a single shift. Storing a separate `shifts` table would have
added nothing the API contract asks for.

**Seat counts are derived, never stored.** `heldSeats` / `confirmedSeats` are always computed from
the `holds` table. A denormalised counter is faster but can drift (double increment on a retried
request, a missed decrement on expiry), and the brief explicitly asks for `confirmedSeats` to grow
only once even if confirm is replayed. Derived counts make that impossible to get wrong.

## 2. Identity (`X-Student-Id`)

* Format is validated as **exactly 17 ASCII digits** → otherwise `400 INVALID_STUDENT_ID`
  (`400 STUDENT_ID_REQUIRED` when the header is missing entirely). The header is required for
  every endpoint the brief marks with a header; `GET /classes` and `GET /holds/{id}` work without
  it.
* The UI strips non-digits while typing, keeps the number in `localStorage` (so a refresh or a
  browser restart keeps the identity) and sends it as the header.
* There is **no authentication** — the number is self-declared, exactly as the brief describes
  ("no login; the UI uses this as the student's identity"). Consequently, `POST /classes` is also
  unauthenticated: the "school admin" screen is just another part of the same UI. In a real
  deployment it would sit behind an admin login; the brief explicitly rules out logins.

## 3. Time and the hold window

* `HOLD_DURATION_SECONDS` comes from the environment (default `600`), validated as a positive
  integer (anything else falls back to 600).
* `expiresAt` is computed **by the database** (`now() + make_interval(...)`), so several backend
  processes can never disagree about a deadline because of clock skew, and the value that is
  stored is exactly the value the response returns.
* Timestamps are stored as `timestamptz` and returned as ISO-8601 UTC with a `Z` suffix, e.g.
  `2026-11-01T04:10:00Z`.

## 4. Releasing seats (expiry)

Two independent mechanisms, on purpose:

1. **A background worker** flips every overdue `HELD` row to `EXPIRED` once per second. This is
   what satisfies "the seat comes back on its own, nobody has to call the API".
2. **Every read and write is expiry-aware**: a `HELD` row that is past its deadline is already
   treated as expired when counting (`expires_at > now()`) and is expired inside the transaction
   that allocates a seat. So a seat is available immediately, even in the sub-second gap before
   the sweeper runs, and `GET /holds/{id}` never reports a stale `HELD` for a dead hold.

The UI does the same locally: the countdown reaching `00:00` immediately shows `Expired` and
disables "pay", without waiting for the next poll.

## 5. Application rules

| Situation | Answer | Why |
|---|---|---|
| Student already has `HELD` or `CONFIRMED` in the class, applies again | `409 DUPLICATE_ACTIVE_HOLD` | Brief: at most one active application / confirmed seat per class |
| Re-applying after `EXPIRED` / `CANCELLED` | allowed, creates a **new** hold | The old row is history; the student lost nothing by waiting |
| Class full | `409 NO_SEATS_AVAILABLE` | Brief specifies 409 |
| Confirm an expired hold | `409 HOLD_EXPIRED` (and the row is marked `EXPIRED`) | Brief: only an unexpired `HELD` may be confirmed |
| Confirm a cancelled hold | `409 HOLD_NOT_ACTIVE` | Same reason |
| Confirm twice / 20 times in parallel | `200 CONFIRMED` every time, one confirmed seat | Brief: idempotent |
| Confirm someone else's hold | `404` | Not `403`: don't leak which hold ids exist |
| Cancel an active hold | `204`, seat free immediately | Brief: "Cancel" button on the application screen |
| Cancel an already cancelled hold | `204` | Idempotent DELETE is the least surprising behaviour |
| Cancel an already expired hold | `409 HOLD_EXPIRED` | There is nothing left to cancel; the client should refresh |
| Cancel a `CONFIRMED` admission | `409 ALREADY_CONFIRMED` | Cancelling would mean "refund and leave school". The brief only offers cancel while the seat is held, and a confirmed seat must not be silently given away |
| Cancel someone else's hold | `404` | Same reason as confirm |

Statuses are the four the brief names: `HELD`, `CONFIRMED`, `EXPIRED`, `CANCELLED`.

## 6. Validation and errors

* `name`: trimmed, 1–200 characters, required → `400 INVALID_CLASS_NAME`.
* `totalSeats`: integer, 1–100000 → `400 INVALID_TOTAL_SEATS`. (Upper bound only to catch typos;
  it is far above any real school.)
* Every error response is JSON with a human-readable message, so the UI never has to show a blank
  page: `{ "error": "CODE", "code": "CODE", "message": "..." }`. Unexpected server failures are
  also caught centrally and returned as `500 INTERNAL_ERROR` with a generic message (the details
  stay in the server log).

## 7. How the "never oversell" guarantee works — and why it also holds for two processes

The invariant only involves rows of **one** class plus the holds that belong to it. So the
allocation of a seat runs in a single transaction that first takes a row lock on that class
(`SELECT ... FROM school_classes WHERE id = $1 FOR UPDATE`). Everything that follows — releasing
expired holds, counting `HELD`/`CONFIRMED`, checking the student's existing application and
inserting the new hold — happens while holding that lock. A second request for the same class
blocks on the lock until the first transaction commits, and then sees its result.

* **Within one process**: requests are serialised by the database lock, not by an in-process
  `lock`/semaphore. Application-level locks would have been useless here.
* **Across two processes sharing one database** (the case the brief asks about): the lock is held
  by PostgreSQL, keyed on the class row, so a second backend container/process waits exactly like
  a second thread would. Both processes execute the same code path, so the invariant holds no
  matter which one answers. This is exercised by
  `ConcurrencyTests.Two_backend_instances_sharing_one_database_still_respect_the_seat_limit`,
  which runs two real API hosts against one database.
* **Second line of defence**: a partial unique index
  `(class_id, student_id) WHERE status IN ('HELD','CONFIRMED')` makes "two active seats for one
  student in one class" impossible at the schema level, even for a future code path (or a manual
  `INSERT`) that forgets the check.
* **Trade-off**: allocation for one class is serialised, so throughput per class is bounded by the
  length of a very short transaction (a few indexed statements). That is the right trade for
  correctness. If it ever became a bottleneck, the lock could be narrowed to a counter row updated
  with a conditional `UPDATE ... WHERE held + confirmed < total_seats`, but then the counters
  become the source of truth and must be maintained on every expiry — more moving parts for the
  same guarantee.
* **Ordering**: because allocation is serialised, the order is the order in which requests acquire
  the lock, which is effectively arrival order. A strict FIFO queue was not required (the brief
  only requires that no more than `totalSeats` are handed out), and a queue would have meant an
  extra component.
* **Connection pressure**: waiting requests keep a database connection, so if hundreds of
  applicants hit one class at once, PostgreSQL could run out of connections. The pool is therefore
  capped per process (`DB_MAX_POOL_SIZE`, default 20 in Compose); extra requests wait for a pool
  slot instead of failing, and `docker-compose.yml` raises `max_connections` to 200 for the
  database. Without that cap the concurrency test produced `53300: sorry, too many clients
  already` — see `AI_USAGE.md`.

## 8. Restart safety

All state is in PostgreSQL (`holds`, `school_classes`), nothing is kept in memory, and the schema
is created idempotently on startup (guarded by a PostgreSQL advisory lock so simultaneous starts
are safe). `expiresAt` is an absolute timestamp, so holds that were running when the process died
simply expire on time once it is back — `docker compose down` followed by `docker compose up`
loses nothing.

## 9. Frontend decisions

* **Polling instead of websockets/SSE**: class counts refresh every 3 s and the student's
  applications every 2 s; the countdown ticks locally every second. Polling is boring, needs no
  extra infrastructure, and satisfies "seat numbers update every few seconds without a reload".
* **Server clock alignment**: the browser asks `GET /health` once for the server time and offsets
  its countdown by the difference, so a laptop with a wrong clock still shows the right remaining
  time.
* **Errors are always visible** in a coloured notice: seat full, expired, invalid input, backend
  down ("Cannot reach the admission server…"). Buttons stay disabled with a tooltip that explains
  why, instead of failing after the click.
* **`VITE_API_BASE_URL`**: unset means "same origin" and the dev server / nginx proxy
  `/classes`, `/holds`, `/me`, `/health` to the backend — no CORS anywhere, and one image works on
  any host. The variable is still the documented way to point the UI at a different API.
* No routing library: two screens ("Classes", "My applications") are tabs, which keeps the bundle
  small and avoids deep-link state that the brief does not ask for.

## 10. Smaller assumptions

* **Ports**: frontend `3000`, backend `8080` (fixed by the brief), PostgreSQL `5432` published to
  the host only so the backend tests can run against it.
* **No pagination** on `GET /classes` and `GET /me/holds`: a school has tens of classes and a
  student a handful of applications. A cap (200 rows) guards against pathological data.
* **`GET /me/holds` returns the full history** (including expired/cancelled), newest first — the
  brief says "all applications", and a parent wants to see what happened.
* **Extra response fields** (`className`, `createdAt`, `confirmedAt`, `code`) are included because
  the brief allows additions and the UI needs them; no required field is renamed or dropped.
* **Payment is simulated** exactly as instructed: pressing the button calls
  `POST /holds/{holdId}/confirm` and nothing else.
* **CORS is open** (no cookies/credentials are used), so the API can be called by any UI origin,
  which also makes manual API checks easy.
