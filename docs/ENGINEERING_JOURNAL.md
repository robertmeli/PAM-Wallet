# ENGINEERING_JOURNAL.md — AI-Assisted Development Process

## 1) Approach & Tooling

I used a mix of **ChatGPT (planning) + Cascade agent mode (implementation)**.

At the beginning and throughout the task, I intentionally used AI to improve my own prompts (making them more structured, constraint-driven, and validation-oriented) so output quality and consistency improved across iterations.

At several points, I also manually debugged runtime issues and pasted full error stacks/log output into Windsurf IDE so the AI could assist with targeted root-cause analysis and faster fix validation.

I also used screenshots captured with the Snipping Tool to provide visual context (terminal output, benchmark summaries, and IDE state) when text alone was less clear.

At the beginning of the task, I used ChatGPT to draft a PRD-style working outline and compare candidate technologies (Orleans, PostgreSQL, Kafka/outbox, NBomber). This helped bootstrap scope, architecture boundaries, and an execution plan before coding.

Why this worked well:

- Early planning reduced churn during implementation.
- Agentic edits kept changes consistent across files.
- Fast build/test loops made it easy to catch and correct regressions quickly.

## 2) Key Prompts & Iterations

### Prompt A — Worked well on first pass

- **Goal:** Remove unused consumer components without affecting API/runtime behavior.
- **Prompt:** "Decommission `Wallet.Consumer` end-to-end: remove app host wiring, project references, and solution entries, then delete dead consumer files. Keep changes scoped and verify with `dotnet build`."
- **Result:** Completed in one pass:
  - Removed consumer wiring from `PAM-Wallet.AppHost/AppHost.cs`.
  - Removed project reference from `PAM-Wallet.AppHost/PAM-Wallet.AppHost.csproj`.
  - Removed consumer entries from `PAM-Wallet.slnx`.
  - Deleted `src/Wallet.Consumer`.
- **Why effective:** explicit scope + concrete acceptance criteria minimized ambiguity.

### Prompt B — AI/tool issue, then corrected

- **Goal:** Find duplicate/redundant artifacts after refactors.
- **Prompt:** "Run a repository-wide redundancy sweep and list duplicate/dead files and overlap by module; prioritize safe removals first."
- **What went wrong:** broad search touched IDE-managed `.vs` index files and produced lock/read errors.
- **Diagnosis:** error output pointed to `.vs\PAM-Wallet.slnx\FileContentIndex\*.vsidx` lock conflicts.
- **Correction:** narrowed scope to `src`, `tests`, and `docs`, then used targeted reads/searches.
- **Outcome:** redundancy review completed safely, without interacting with IDE internals.

### Prompt C — Iteration after imperfect initial output

- **Goal:** Enforce separation of concerns in Orleans orchestration.
- **Prompt:** "Refactor `WalletGrain` for strict SoC: business rules in `AddFundsHandler`/`DeductFundsHandler`/`GetBalanceHandler`; grain only handles actor orchestration, retry policy, and state sync. Preserve contracts and verify with build + tests + perf sanity check."
- **Follow-up prompt (using structured risk format):** "After the refactor, list the main risks with this optimization in sections: (1) main risks, (2) why this may still be acceptable, and (3) recommended mitigations. Be specific to `WalletGrain` state drift, version accuracy, timestamp fidelity, and retry behavior."
- **What AI got wrong (first attempt):** introduced persistence refresh on each successful iteration (`repository.Get` after write), and briefly inferred version/state from partial snapshots.
- **Diagnosis:** load tests showed write-path tail regression; code review confirmed added hot-path DB round-trips (`RefreshStateAsync` per success and extra cold-path read).
- **Correction:** retained handler-based business flow, removed per-success refresh when state is already loaded, and kept repository reconciliation for cold/not-loaded paths.
- **Outcome:** SoC preserved with lower DB pressure and better write-path latency behavior.

### Prompt D — Cleanup follow-up

- **Goal:** Remove dead artifacts after refactors.
- **Prompt:** "Perform a safe cleanup pass for redundant artifacts introduced by recent refactors. Remove namespace-only and unreferenced legacy files, update any affected wiring/references, and ensure no runtime paths are broken. Keep changes minimal and scoped (no unrelated refactors). After cleanup, run `dotnet build` and targeted tests to verify there are no regressions."
- **Result:** removed stale namespace-only files, cleaned obsolete references, and validated with successful build/test runs.
- **Why effective:** explicit cleanup scope plus verification criteria reduced the risk of over-deletion and made the output reviewer-safe.

### Prompt E — Consolidating multiple high-risk issues into one fix direction

- **Goal:** Resolve linked correctness risks with one coherent design decision.
- **Prompt:** "`AddFunds` bypasses domain aggregate (high), events are not atomic with DB write (high), and events may be silently dropped under load (high). How can we fix these together in one direction?"
- **AI response shape:** proposed a single-direction architecture: domain-first command handling + transactional outbox + durable relay behavior.
- **Key actions extracted from the response:**
  - Route add-funds logic through aggregate/domain methods (not persistence shortcuts).
  - Persist wallet change and outbox event in one DB transaction.
  - Keep publish asynchronous from outbox with retry semantics (avoid drop-on-overload behavior).
- **Outcome:** this prompt helped convert scattered issues into an integrated implementation strategy.
## 3) Architectural Decisions

### Decision 1: Keep Orleans grain as orchestration, move business logic to handlers
- **What was decided:** `WalletGrain` delegates add/deduct/get behavior to `AddFundsHandler`, `DeductFundsHandler`, and `GetBalanceHandler`.
- **Alternatives considered:**
  1. Keep rich logic directly in grain (fewer call hops).
  2. Move all orchestration to handlers and make grain ultra-thin pass-through.
- **Why this approach:**
  - Better separation of concerns and test symmetry across layers.
  - Keeps grain-specific concerns (single-writer boundary, retry, runtime state handling) where they belong.
  - AI provided initial refactor recommendations; I then analyzed outcomes, re-prompted with explicit risk/trade-off questions, and applied my own engineering judgment before finalizing.

### Decision 2: Persist wallet state and outbox atomically; publish asynchronously
- **What was decided:** `SaveWithOutbox` writes wallet + outbox in one DB transaction; `WalletOutboxRelayService` publishes to Kafka in background.
- **Persistence strategy (implemented):**
  - Command handlers (`AddFundsHandler`/`DeductFundsHandler`) execute domain logic first, then persist via repository.
  - Repository uses `SaveWithOutbox` so wallet row changes and outbox event insert commit atomically in PostgreSQL.
  - Request path does not publish directly to Kafka; publishing is handled asynchronously by outbox relay.
  - Relay publishes events in batches and removes outbox rows after successful publish.
- **Alternatives considered:**
  1. Publish directly in request path.
  2. Full distributed transaction with Kafka.
- **Why this approach:**
  - Preserves write integrity without coupling API latency to broker latency.
  - Aligns with PRD non-goal of distributed transactions / exactly-once semantics.
  - AI recommendations helped refine and simplify this path; I validated trade-offs and retained it based on my own judgment for reliability vs complexity.

### Decision 3: Authoritative versioning from repository aggregate for grain state
- **What was decided:** Grain state refresh reads aggregate from repository after successful operations.
- **Alternatives considered:**
  1. Infer/increment grain version locally from operation result.
  2. Return richer version metadata from handlers and avoid read-back.
- **Why this approach:**
  - Avoids subtle version drift and keeps state aligned with persisted truth.
  - Lower risk change than broad handler contract redesign.
  - AI surfaced candidate options; I re-prompted around version-drift and latency risks, then selected the lowest-risk path using my own judgment.

#### Grain state structure

- **State model (`WalletGrainState`):**
  - `IsLoaded`: indicates whether grain cache is hydrated from authoritative source.
  - `Balance`, `Version`, `UpdatedAtUtc`: operational balance/version/timestamp tracking.
  - `WalletType`, `CurrencyType`, `CreatedAt`, `ExpiresAt`: wallet metadata needed for reads/responses.
- **Lifecycle usage:**
  - On activation/cold path, state is reconciled from repository aggregate.
  - On hot success path, grain updates cached state from operation result to reduce extra DB reads.
  - On uncertainty/not-loaded paths, repository refresh remains the authoritative fallback.

#### Trade-off update: grain fast-path state optimization

To reduce hot-path latency at 1000 RPS, post-write refresh reads were removed for successful operations when grain state is already loaded. The grain now updates in-memory state from operation results and falls back to repository refresh when state is not loaded.

- **Benefit gained:**
  - Fewer DB round-trips on add/deduct success path.
  - Lower contention and better latency under sustained load.
- **Risks accepted:**
  - In-memory grain state can temporarily drift from DB truth if persistence/versioning behavior changes.
  - Local version increment assumes one successful operation equals one persisted version increment.
  - `UpdatedAtUtc` in fast path reflects application time, not always exact DB timestamp.
- **Mitigations in place / planned:**
  - Keep repository refresh path for cold/not-loaded state.
  - Continue using DB as source of truth on activation and reconciliation paths.
  - Add/maintain integration tests that validate final balance + version correctness under contention.

### Decision 4: Outbox relay over CDC for event propagation
- **What was decided:** Kept application-managed transactional outbox writes in the repository and a polling relay service (`WalletOutboxRelayService`) for Kafka publishing.
- **Persistence strategy impact:**
  - Persistence consistency is guaranteed at DB commit time (wallet + outbox together).
  - Event delivery is eventually consistent and decoupled from API latency.
  - Failures in broker publish do not roll back committed wallet state; relay retry loop handles redelivery attempts.
- **Alternatives considered:**
  1. CDC (e.g., Debezium) from PostgreSQL WAL to Kafka.
  2. Direct publish from request path without outbox.
- **Why this approach:**
  - Minimal platform overhead for this challenge scope (no extra Kafka Connect/Debezium infrastructure).
  - Clear write atomicity: wallet state + outbox row persisted in a single DB transaction.
  - Simpler debugging and local reproducibility inside the codebase.
  - AI suggested outbox; I analyzed operational trade-offs over CDC/ Kafka connect and chose outbox for this phase using my own judgment.

#### CDC vs Outbox trade-offs observed
- **Outbox advantages in current implementation:**
  - Strong control in application code over what becomes an event payload and when.
  - Easy local operation and tests without external CDC stack.
  - Explicit retry/poll controls (`batchSize`, poll intervals, max batches) for predictable tuning.
- **Outbox costs in current implementation:**
  - Polling introduces publish latency and potential DB read overhead.
  - Requires relay lifecycle management and dead-letter/retry strategy in app layer.
  - Backlog management is now an application concern.
- **CDC advantages (if adopted later):**
  - Lower coupling of relay mechanics to service runtime.
  - Near-real-time DB-log-driven event extraction at scale.
  - Standardized streaming pipeline when multiple services need change events.
- **CDC costs (for this project stage):**
  - Higher operational complexity (Debezium/Kafka Connect deployment, monitoring, upgrades).
  - More moving parts for local development and CI.
  - Schema evolution and topic contract governance become broader platform concerns.

Current conclusion: **outbox relay is the better fit for scope and delivery speed now**; CDC remains a valid future evolution path once operational maturity and cross-service event volume increase.

### Decision 5: No Elasticsearch logging stack in this phase
- **What was decided:** Did not add Elasticsearch/Logstash/Kibana (ELK) for centralized logging.
- **Alternatives considered:**
  1. Add ELK stack and structured shipping from application logs.
  2. Use existing console/default logging with environment-level aggregation later.
- **Why this approach:**
  - Logging observability requirements for this challenge are satisfied without introducing a full search cluster.
  - ELK would add significant infrastructure and operational overhead (deployment, storage, retention, index lifecycle, dashboard maintenance).
  - The current priority was correctness, concurrency safety, and performance under load; ELK would dilute effort from core deliverables.
  - The service already emits structured logs that can be forwarded to centralized platforms later with minimal code changes.
  - AI recommended optional observability expansions; I re-prompted on cost/benefit and accepted this trade-off based on my own delivery priorities and judgment.

Trade-off accepted: lower immediate log search/analytics capability in exchange for a simpler, faster-to-deliver, and easier-to-operate baseline.

### Decision 6: NBomber over k6 for performance testing in this phase
- **What was decided:** Used `NBomber` (`tests/Wallet.PerformanceTests`) as the primary load-testing tool instead of `k6`.
- **Why this approach:**
  - Tight integration with the existing .NET codebase and CI workflow.
  - Reuse of C# DTO/request-building patterns reduced setup friction.
  - Faster iteration for endpoint-specific scenarios (`add`, `deduct`, `balance`) and preflight diagnostics.
  - Lower context-switch cost for a single-language implementation team.
  - AI provided tooling alternatives; I reviewed trade-offs and selected NBomber for this stage using my own judgment.

#### NBomber vs k6 trade-offs observed
- **NBomber advantages (current project):**
  - Native C# ecosystem fit, easier reuse of app-side conventions.
  - Strong scenario composition and diagnostics in one runner.
  - Simpler maintenance for this repository's current contributor profile.
- **NBomber costs:**
  - Smaller ecosystem footprint than k6 for broader performance engineering workflows.
  - Less straightforward for non-.NET contributors compared with JS-based scripts.
- **k6 advantages (if added later):**
  - Broad adoption, rich cloud/reporting ecosystem, and easy script sharing.
  - Good fit for cross-language/platform teams.
- **k6 costs (for this stage):**
  - Additional toolchain and scripting paradigm to maintain.
  - Duplicate scenario maintenance risk while requirements are still changing quickly.

Current conclusion: **NBomber is the pragmatic choice for rapid, .NET-centric delivery now**; adding `k6` later remains a valid extension when cross-team portability/reporting needs increase.

### Performance evidence snapshot (NBomber)

Captured benchmark command used:

```bash
dotnet run --project tests/Wallet.PerformanceTests -- http://localhost:42792 300 1000 balance 5000 diag timeout=10
```

Observed run summary from the captured output:

- Scenario: `get_balance_scenario`
- Duration: `00:05:00`
- Inject rate: `1000 req/sec`
- Successful requests: `300000`
- Failed requests: `0`
- HTTP status diagnostics: `200 -> 329908` (includes warmup/diag sampling flow)
- Exception diagnostics: `none`
- Slow request counters (`diag`):
  - `>=5s: 0`
  - `>=30s: 0`

Latency stats shown in the run output:

- Mean: `0.15`
- Max: `32.56`
- p50: `0.70`
- p75: `0.81`
- p95: `1.16`
- p99: `3.67`

Interpretation: this run demonstrates stable throughput at target load with zero request failures and no slow-request alerts for the selected endpoint profile.

### Performance evidence snapshot (NBomber) — add_funds slow-tail case

Captured benchmark command used:

```bash
dotnet run --project tests/Wallet.PerformanceTests -- http://localhost:42792 300 1000 add 5000 diag timeout=30
```

Observed run summary from the captured output:

- Scenario: `add_funds_scenario`
- Duration: `00:05:00`
- Inject rate: `1000 req/sec`
- Successful requests: `300000`
- Failed requests: `0`
- HTTP status diagnostics: `200 -> 358603`
- Exception diagnostics: `none`
- Slow request counters (`diag`):
  - `>=5s: 278042`
  - `>=30s: 0`

Successful outcomes in the same run:

- Very high completion ratio: `300000 / 300000` requests succeeded.
- Success rate: `100%`.
- Sustained throughput remained at target (`1000 RPS`).
- Successful request latency stats: `min 8.3ms`, `mean 117.04ms`, `max 6392.11ms`, `StdDev 284.71ms`.
- Successful request percentiles: `p50 24.05ms`, `p75 78.14ms`, `p95 617.47ms`, `p99 1179.65ms`.
- No extreme 30s tail events were observed (`>=30s: 0`).

Why the `>=5s` counter can be very high in this run:

- `add_funds` is a write-heavy path (wallet write + outbox write + Orleans orchestration), so queueing effects are much stronger than in `balance`.
- The run used a high sustained load (`1000 rps`) and `timeout=30`, so many delayed requests still complete as `200` instead of failing quickly.
- The diagnostic slow counter is a raw threshold counter over all sampled calls in the diagnostic flow (including warmup/extended run activity), so it can highlight tail pressure more aggressively than percentile summaries.

Interpretation: this run indicates a **long-tail latency problem under sustained write load**, not functional correctness failure (still `0` failed requests). It is useful as a stress signal for throughput/latency tuning rather than pass/fail correctness.

#### Future improvements to combat long-tail latency

- Return authoritative persistence metadata (`Version`, `UpdatedAtUtc`) from application/persistence layer to avoid inference and reduce reconciliation reads.
- Add periodic grain reconciliation (e.g., every N successful writes) instead of per-request refresh, with a configurable interval.
- Batch outbox relay work more aggressively (tune `OutboxBatchSize`, busy poll interval, max batches per cycle) and measure DB lock time.
- Introduce adaptive backpressure/rate shaping for write-heavy scenarios when queue depth or DB latency crosses thresholds.
- Add dedicated high-load integration/performance assertions for p95/p99 and `>=5s` counters to prevent regressions.
- Evaluate partitioning and index optimization for `wallet_outbox` and `wallets` hot paths under sustained 1000 RPS write traffic.
- If write volume grows further, consider CDC-based emission to reduce app-side polling overhead and relay contention.

### Performance evidence snapshot (NBomber) — deduct_funds timeout case

Captured benchmark command used:

```bash
dotnet run --project tests/Wallet.PerformanceTests -- http://localhost:42792 300 1000 deduct 5000 diag timeout=10
```

Observed run summary:

- Scenario: `deduct_funds_scenario`
- Duration: `00:05:00`
- Request count: `299999`
- Successful requests: `299837`
- Failed requests: `162`
- Failure type: `EX_TaskCanceledException`
- Failure latency band: ~`10000ms` (`min 9999.88`, `mean 10016.17`, `max 10057.17`)
- Latency percentiles (ok): `p50 17.82ms`, `p95 281.86ms`, `p99 1972.22ms`
- Diagnostics:
  - HTTP 200: `327887`
  - Exceptions: `1498x TaskCanceledException`
  - Slow counters: `>=5s: 11955`, `>=30s: 0`

Why failures occurred:

- These are **client timeout failures**, not business logic rejections.
- The configured `timeout=10` means any request exceeding 10 seconds is canceled by the client and recorded as `TaskCanceledException`.
- `deduct_funds` is write-heavy and sensitive to queueing/backpressure under sustained `1000 rps`, so tail requests can exceed 10s during spikes.
- Diagnostic exception counts are higher than scenario fail count because diagnostics include additional warmup/sampled calls beyond the strict measured scenario window.

Successful outcomes in the same run:

- Very high completion ratio: `299837 / 299999` requests succeeded.
- Success rate: `99.946%`.
- Sustained throughput remained near target (`~999.5 RPS`).
- Successful request latency stats: `min 7.93ms`, `mean 105.77ms`, `max 10016.05ms`, `StdDev 476.67ms`.
- Successful request percentiles: `p50 17.82ms`, `p75 25.31ms`, `p95 281.86ms`, `p99 1972.22ms`.
- No extreme 30s tail events were observed (`>=30s: 0`).

Recommended reruns for clearer capacity interpretation:

- Keep endpoint/cardinality high enough to reduce hot-key amplification effects.
- Compare p95/p99 and `>=5s` counters across `deduct` runs before/after each optimization.

## Notes on Verification

- Repeatedly validated changes with `dotnet build PAM-Wallet.slnx`.
- Ran targeted test suites (notably unit + integration checks) after structural refactors.
- When a generated change introduced a compile issue, the loop was: inspect error → minimal patch → rebuild.
