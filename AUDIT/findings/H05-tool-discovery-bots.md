---
agent_id: H05
lane: Tools (EndpointDiscovery, SymbolImport, LiveBots)
scope:
  - src/Tools/YO4X.Mt5.EndpointDiscovery/**
  - src/Tools/YO4X.Mt5.SymbolImport/**
  - src/Tools/YO4X.LiveBots/**
status: COMPLETE
generated: 2026-08-29T11:32:00Z
counts: { P0: 0, P1: 3, P2: 2, P3: 0 }
---

# H05 — Tools (EndpointDiscovery, SymbolImport, LiveBots)

## Scope audited
- `src/Tools/YO4X.Mt5.EndpointDiscovery/Program.cs` (262 lines)
- `src/Tools/YO4X.Mt5.EndpointDiscovery/YO4X.Mt5.EndpointDiscovery.csproj` (15 lines)
- `src/Tools/YO4X.Mt5.SymbolImport/Program.cs` (153 lines)
- `src/Tools/YO4X.Mt5.SymbolImport/YO4X.Mt5.SymbolImport.csproj` (19 lines)
- `src/Tools/YO4X.LiveBots/Program.cs` (294 lines)
- `src/Tools/YO4X.LiveBots/YO4X.LiveBots.csproj` (20 lines)

## Verdict
`YO4X.Mt5.EndpointDiscovery` is clean, operating strictly as an offline parser for pinned vendor artifacts with rigorous SHA-256 validation, server name pinning, process isolation, and fail-closed timeout guards. However, `YO4X.Mt5.SymbolImport` and `YO4X.LiveBots` contain defects: `SymbolImport` specifies an invalid PostgreSQL parameter type (`NpgsqlDbType.Char`) for 3-letter currency strings that breaks symbol imports, lacks description string truncation, and falls back to a hardcoded tenant GUID; `LiveBots` lacks tenant scoping when selecting profitable backtests (querying globally across all tenants) and uses a crude ternary heuristic that hardcodes symbol price precision to 5 decimals for all non-XAU instruments.

## Findings

### [P1] `YO4X.LiveBots` queries backtests without tenant isolation, selecting and running other tenants' strategies
- **Where:** `src/Tools/YO4X.LiveBots/Program.cs:164-179`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        await using var command = new NpgsqlCommand(
            """
            select strategy.id, strategy.name, backtest.net_profit_amount,
                   backtest.max_drawdown_percent, backtest.profit_factor,
                   backtest.trade_count, coalesce(backtest.data_quality_percent, 0),
                   backtest.tenant_id, backtest.user_id
            from simulation.backtests as backtest
            join catalog.strategies as strategy
              on strategy.tenant_id = backtest.tenant_id
             and strategy.id = backtest.strategy_id
            where backtest.status = 'COMPLETE'
              and backtest.net_profit_amount > 0
              and backtest.trade_count > 0
            order by backtest.net_profit_amount desc
            """,
            connection);
  ```
- **Failure:** In a multi-tenant database, `SelectProfitableAsync` does not filter by tenant (`tenant_id`), nor does the CLI accept a `--tenant-id` option. The tool selects the highest-profit completed backtest globally across the entire database (`chosen[0]`), loads that strategy's source code, and writes a bot record into `bots.bots` using the foreign tenant's `tenant_id` and `user_id` (lines 219–220), executing another tenant's strategy on the local operator's account.
- **Fix:** Add a required `--tenant-id` command-line argument and filter the query with `and backtest.tenant_id = @tenant_id`.

### [P1] `YO4X.LiveBots` hardcodes price precision to 2 or 5 decimals, causing incorrect point calculations on non-forex and JPY pairs
- **Where:** `src/Tools/YO4X.LiveBots/Program.cs:136-142`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        LiveRunOutcome outcome = await runner.RunAsync(
            new Mql5SourceDocument(run.Name, File.ReadAllBytes(sourcePath)),
            broker,
            seed,
            minutes,
            symbol.Contains("XAU", StringComparison.OrdinalIgnoreCase) ? 2 : 5,
            stop.Token).ConfigureAwait(false);
  ```
- **Failure:** Precision is inferred solely via `symbol.Contains("XAU") ? 2 : 5`. When trading any symbol with other precision (e.g. `USDJPY` with 3 digits, `US30` or `SPX500` with 1–2 digits, or `BTCUSD`), `LiveBrokerContext` receives `digits = 5`. Consequently, `Point` (`1 / 10^digits`) is computed as `0.00001` instead of `0.001` (off by 100×), distorting stop-loss, take-profit, spread, and point calculations and causing invalid order prices or immediate stop-outs.
- **Fix:** Obtain the actual symbol digits from the broker connection or database catalogue rather than using a hardcoded string heuristic.

### [P1] `YO4X.Mt5.SymbolImport` passes `NpgsqlDbType.Char` for 3-letter currency codes, causing database insert errors
- **Where:** `src/Tools/YO4X.Mt5.SymbolImport/Program.cs:119-120`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
            insert.Parameters.AddWithValue("currency", NpgsqlDbType.Char,
                Absent(symbol.Currency));
  ```
- **Failure:** In Npgsql, `NpgsqlDbType.Char` specifies PostgreSQL's single 1-byte `"char"` data type, while `bots.broker_symbols.currency` is `char(3)` with `check (currency ~ '^[A-Z]{3}$')`. Passing a 3-character string like `"USD"` causes Npgsql to throw an `InvalidCastException` for multi-byte strings or sends 1 byte (`'U'`), which fails the regex check constraint, aborting the entire symbol import transaction.
- **Fix:** Change `NpgsqlDbType.Char` to `NpgsqlDbType.Text` or `NpgsqlDbType.Varchar`.

### [P2] `YO4X.Mt5.SymbolImport` does not truncate broker descriptions, causing import failure on long descriptions
- **Where:** `src/Tools/YO4X.Mt5.SymbolImport/Program.cs:113-114`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
            insert.Parameters.AddWithValue("description", NpgsqlDbType.Text,
                Absent(symbol.Description));
  ```
- **Failure:** The database table `bots.broker_symbols` enforces `description text check (length(btrim(description)) between 1 and 500)`. If a broker provides an instrument description exceeding 500 characters, the insert fails with a PostgreSQL check constraint violation, rolling back the transaction and leaving the catalogue empty.
- **Fix:** Clamp `symbol.Description` to a maximum of 500 characters using `symbol.Description[..Math.Min(symbol.Description.Length, 500)]` before parameter binding.

### [P2] `YO4X.Mt5.SymbolImport` silently defaults to hardcoded development tenant GUID when `--tenant-id` is omitted
- **Where:** `src/Tools/YO4X.Mt5.SymbolImport/Program.cs:49`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        Guid tenantId = Guid.Parse(Option(arguments, "--tenant-id") ?? "019c8d27-763d-7000-8000-000000000001");
  ```
- **Failure:** When `--tenant-id` is omitted in a production run, the tool silently falls back to the development tenant ID (`019c8d27-763d-7000-8000-000000000001`), deleting and re-inserting symbol catalogue entries under the development tenant rather than failing fast.
- **Fix:** Require `--tenant-id` explicitly via `Required(arguments, "--tenant-id")` and remove the default fallback.

## Referrals
- `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5BrokerSymbol.cs` — Does not parse or expose `VolumeMin`, `VolumeMax`, `VolumeStep`, or `Path` from MT5 API symbol metadata, leaving these columns null in `bots.broker_symbols`.
- `src/Runtime/YO4X.Mql5.Live/LiveBrokerContext.cs:111` — `AccountInfoDouble` unconditionally returns `0.0`, breaking strategy lot-sizing calculations that depend on account balance or equity.

## Coverage gaps
- `src/Tools/YO4X.Mt5.EndpointDiscovery/Program.cs:58-64` — Process timeout and tree termination branch when worker execution exceeds 10 seconds.
- `src/Tools/YO4X.Mt5.SymbolImport/Program.cs:74-78` — Exit branch when broker reports 0 symbols (`symbols.Count == 0`), aborting without modifying the database.
- `src/Tools/YO4X.LiveBots/Program.cs:65-69` — Exit branch when no completed backtests with positive net profit exist in the database.
- `src/Tools/YO4X.LiveBots/Program.cs:88-92` — Error handling branch when seed historical CSV market feed file does not exist on disk.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 166.4s | 215574 tok | id=cca0c626-dd00-49c4-87f8-920efc5dfe9a
