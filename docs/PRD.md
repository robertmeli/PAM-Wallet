# Player Wallet Service — Product Requirements Document

See parent request for full PRD. This file is the canonical reference copy.

## Quick Reference

| Endpoint | Method | Description |
|---|---|---|
| `/wallets/{playerId}/funds/add` | POST | Add funds to wallet |
| `/wallets/{playerId}/funds/deduct` | POST | Deduct funds from wallet |
| `/wallets/{playerId}/balance` | GET | Get current balance |

## Architecture Layers

```
API Adapter (ASP.NET Core 10 Minimal API)
    ↓
Orleans Grain (WalletGrain — single-writer concurrency)
    ↓
Application Use Cases (AddFundsHandler / DeductFundsHandler / GetBalanceHandler)
    ↓
Domain Aggregate (Wallet)
    ↓                    ↓
PostgreSQL Adapter    Kafka Adapter
(IWalletRepository)   (IWalletEventPublisher)
```

## Non-Goals

- Authentication / authorization
- Multi-currency wallets
- Transaction reversals
- Distributed transactions / transactional outbox
- Full event sourcing
- Fraud detection
- Kubernetes deployment
- Exactly-once Kafka guarantees
