# PAM-Wallet

Player Wallet microservice built with .NET 10, ASP.NET Core Minimal API, Orleans, PostgreSQL, and Kafka.

## Overview

This service exposes wallet operations per player:

- Add funds: `POST /wallets/{playerId}/funds/add`
- Deduct funds: `POST /wallets/{playerId}/funds/deduct`
- Get balance: `GET /wallets/{playerId}/balance`
- Create wallet: `POST /wallets/{playerId}`

Core architecture follows a hexagonal style:

- `src/Wallet.Domain` — Wallet aggregate and domain rules
- `src/Wallet.Application` — Use cases and ports
- `src/Wallet.Api` — HTTP API + Orleans hosting
- `src/Wallet.Infrastructure/Persistence.Postgres` — PostgreSQL repository and outbox persistence
- `src/Wallet.Infrastructure/Messaging.Kafka` — Kafka publisher
- `src/Wallet.Infrastructure/Orchestration.Orleans` — Grain orchestration

## Architecture

Request and processing flow:

```text
HTTP Request
  -> Wallet.Api (Minimal API endpoints)
  -> Orleans IWalletGrain (single-writer per playerId)
  -> Application Use Cases (Create/Add/Deduct/Get)
  -> Domain Aggregate (Wallet invariants and events)
  -> Persistence Adapter (PostgreSQL repository + outbox write)

Background flow:
  wallet_outbox table
  -> WalletOutboxRelayService
  -> KafkaWalletEventPublisher
  -> Kafka topic (wallet-events)
```

Key architecture characteristics:

- Hexagonal boundaries: domain and use cases are isolated from frameworks.
- Consistency model: wallet state and outbox event are persisted atomically.
- Delivery model: event publish is asynchronous via relay (eventual consistency).
- Concurrency model: Orleans grain provides per-player single-writer behavior.

## Directory Map

```text
PAM-Wallet/
  src/
    Wallet.Api/                                  # ASP.NET Core API, endpoint mapping, relay service
    Wallet.Application/                          # Use cases and application ports
    Wallet.Domain/                               # Wallet aggregate, domain errors, enums, events
    Wallet.Infrastructure/
      Persistence.Postgres/                      # EF Core DbContext, repository, outbox persistence
      Messaging.Kafka/                           # Kafka producer and settings/options
      Orchestration.Orleans/                     # Grain interfaces, contracts, grain implementation
  tests/
    Wallet.UnitTests/                            # Domain + use case unit tests
    Wallet.ComponentTests/                       # In-memory component tests
    Wallet.IntegrationTests/                     # PostgreSQL/Testcontainers integration tests
    Wallet.PerformanceTests/                     # NBomber performance runner
  PAM-Wallet.AppHost/                            # Aspire host wiring local dependencies
  PAM-Wallet.ServiceDefaults/                    # Shared Aspire service defaults
  docs/
    PRD.md
    ENGINEERING_JOURNAL.md
  docker-compose.yml                             # Local Postgres/Kafka tooling stack
  PAM-Wallet.slnx
```

## Prerequisites

- .NET SDK 10
- Docker Desktop (for PostgreSQL and Kafka)

You can run locally in two ways:

1. `docker-compose` + direct API run (`src/Wallet.Api`)
2. `.NET Aspire` via `PAM-Wallet.AppHost` (recommended for full local orchestration and dashboard)

## Getting Started

### 1) Start infrastructure

```bash
docker compose up -d
```

### 2) Build

```bash
dotnet build PAM-Wallet.slnx
```

### 3) Run API

```bash
dotnet run --project src/Wallet.Api
```

### Alternative: Run with .NET Aspire (AppHost)

```bash
dotnet run --project PAM-Wallet.AppHost
```

This starts the local dependency graph via Aspire and provides the Aspire dashboard for service health/log visibility.

By default (dev profile), the API is typically available at `http://localhost:42792`.

## Tests

Run all tests:

```bash
dotnet test PAM-Wallet.slnx
```

Run specific suites:

```bash
dotnet test tests/Wallet.UnitTests/Wallet.UnitTests.csproj
dotnet test tests/Wallet.ComponentTests/Wallet.ComponentTests.csproj
dotnet test tests/Wallet.IntegrationTests/Wallet.IntegrationTests.csproj
```

## Performance Tests

NBomber runner:

```bash
dotnet run --project tests/Wallet.PerformanceTests -- http://localhost:42792 300 1000
```

Arguments:

1. Base URL
2. Duration seconds (e.g. `300`)
3. Target RPS (e.g. `1000`)
4. Optional endpoint (`add`, `deduct`, `balance`, `all`)

## Performance Results

Sample benchmark runs used during implementation (5 minutes, target `1000 req/s`):

```bash
dotnet run --project tests/Wallet.PerformanceTests -- http://localhost:42792 300 1000 balance 5000 diag timeout=10
dotnet run --project tests/Wallet.PerformanceTests -- http://localhost:42792 300 1000 add 5000 diag timeout=30
dotnet run --project tests/Wallet.PerformanceTests -- http://localhost:42792 300 1000 deduct 5000 diag timeout=10
```

Observed snapshots:

| Scenario | Success | Failed | p95 (ms) | p99 (ms) | Key Notes |
|---|---:|---:|---:|---:|---|
| `balance` | `300000` | `0` | `1.16` | `3.67` | `>=5s: 0`, exceptions: `none` |
| `add` | `300000` | `0` | `617.47` | `1179.65` | write-heavy path, long-tail pressure under sustained load |
| `deduct` | `299837` | `162` | `281.86` | `1972.22` | failures were `TaskCanceledException` at `timeout=10` |

Additional context:

- `add` run showed high slow-counter pressure (`>=5s: 278042`) with no failed requests.
- `deduct` run maintained near-target throughput (~`999.5 RPS`) with `99.946%` success rate.
- Timeout settings materially affect failure profile for write-heavy scenarios.

Full process notes and context are documented in `docs/ENGINEERING_JOURNAL.md`.

## API Notes

- Outbox pattern is used for wallet event persistence and async relay to Kafka.
- Orleans grain enforces single-writer behavior per `playerId`.
- JSON request models support flexible `expiresAt` formats (ISO/date/epoch/null).

## Engineering Journal

AI-assisted process notes are documented in:

- `docs/ENGINEERING_JOURNAL.md`

Acknowledgement of oversight

- I overlooked using the AAA structure in the tests. It wasn’t a technical limitation, but more of an oversight. I corrected it afterwards and will submit a PR if permitted. I'm also using it as a reminder to pay more attention to clarity and maintainability.
