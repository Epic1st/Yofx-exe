---
agent_id: C06
lane: YO4X.BuildingBlocks
scope:
  - src/BuildingBlocks/YO4X.BuildingBlocks/AuthorizationDeniedException.cs
  - src/BuildingBlocks/YO4X.BuildingBlocks/BackendCapabilityUnavailableException.cs
  - src/BuildingBlocks/YO4X.BuildingBlocks/BoundedBooleanProbe.cs
  - src/BuildingBlocks/YO4X.BuildingBlocks/CanonicalBase64Url.cs
  - src/BuildingBlocks/YO4X.BuildingBlocks/CanonicalJson.cs
  - src/BuildingBlocks/YO4X.BuildingBlocks/DomainException.cs
  - src/BuildingBlocks/YO4X.BuildingBlocks/Identifiers.cs
  - src/BuildingBlocks/YO4X.BuildingBlocks/OperationResult.cs
  - src/BuildingBlocks/YO4X.BuildingBlocks/ResourceConflictException.cs
  - src/BuildingBlocks/YO4X.BuildingBlocks/ResourceNotFoundException.cs
  - src/BuildingBlocks/YO4X.BuildingBlocks/Time.cs
  - src/BuildingBlocks/YO4X.BuildingBlocks/VersionedAggregate.cs
  - src/BuildingBlocks/YO4X.BuildingBlocks/YO4X.BuildingBlocks.csproj
status: COMPLETE
generated: 2026-08-29T11:28:00Z
counts: { P0: 0, P1: 1, P2: 1, P3: 1 }
---

# C06 — YO4X.BuildingBlocks

## Scope audited
- `src/BuildingBlocks/YO4X.BuildingBlocks/AuthorizationDeniedException.cs` (15 lines)
- `src/BuildingBlocks/YO4X.BuildingBlocks/BackendCapabilityUnavailableException.cs` (14 lines)
- `src/BuildingBlocks/YO4X.BuildingBlocks/BoundedBooleanProbe.cs` (157 lines)
- `src/BuildingBlocks/YO4X.BuildingBlocks/CanonicalBase64Url.cs` (51 lines)
- `src/BuildingBlocks/YO4X.BuildingBlocks/CanonicalJson.cs` (64 lines)
- `src/BuildingBlocks/YO4X.BuildingBlocks/DomainException.cs` (14 lines)
- `src/BuildingBlocks/YO4X.BuildingBlocks/Identifiers.cs` (7 lines)
- `src/BuildingBlocks/YO4X.BuildingBlocks/OperationResult.cs` (16 lines)
- `src/BuildingBlocks/YO4X.BuildingBlocks/ResourceConflictException.cs` (14 lines)
- `src/BuildingBlocks/YO4X.BuildingBlocks/ResourceNotFoundException.cs` (10 lines)
- `src/BuildingBlocks/YO4X.BuildingBlocks/Time.cs` (23 lines)
- `src/BuildingBlocks/YO4X.BuildingBlocks/VersionedAggregate.cs` (49 lines)
- `src/BuildingBlocks/YO4X.BuildingBlocks/YO4X.BuildingBlocks.csproj` (10 lines)

## Verdict
The core primitives are mostly well-crafted and robust: `BoundedBooleanProbe` provides resilient single-flight probe coalescing with defensive exception redaction and timeout containment, `CanonicalBase64Url` rigorously enforces canonical unpadded encoding and zeroes cryptographic buffers, and `CanonicalJson` guarantees deterministic key sorting. However, `OperationResult` contains a severe flaw where calling `Failure<T>()` with an empty collection or no arguments constructs an object reporting `IsSuccess == true`, and `FixedClock` permits non-UTC timestamps via property mutation.

## Findings

### [P1] OperationResult.Failure with empty params evaluates to IsSuccess == true
- **Where:** `src/BuildingBlocks/YO4X.BuildingBlocks/OperationResult.cs:14`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
      public static OperationResult<T> Failure<T>(params OperationError[] errors) => new(default, errors);
  ```
- **Failure:** Invoking `OperationResult.Failure<T>()` without arguments or passing an empty array `OperationResult.Failure<T>([])` creates an `OperationResult<T>` with `Errors.Count == 0`. `IsSuccess` evaluates `Errors.Count == 0` to `true`, causing callers checking `if (result.IsSuccess)` to treat an explicitly failed operation as successful with an uninitialized `default(T)` value.
- **Fix:** Enforce at least one error either by validating `if (errors.Length == 0) throw new ArgumentException(...)` or by changing the signature to `Failure<T>(OperationError error, params OperationError[] additionalErrors)`.

### [P2] FixedClock.UtcNow property setter does not normalize non-UTC offsets to UTC
- **Where:** `src/BuildingBlocks/YO4X.BuildingBlocks/Time.cs:21`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  public sealed class FixedClock(DateTimeOffset utcNow) : IClock
  {
      public DateTimeOffset UtcNow { get; set; } = utcNow.ToUniversalTime();
  }
  ```
- **Failure:** While the constructor calls `.ToUniversalTime()`, mutating `fixedClock.UtcNow = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.FromHours(5))` assigns the value directly to the auto-property without normalization. Subsequent reads from `IClock.UtcNow` return a timestamp with `Offset == +05:00` instead of `TimeSpan.Zero`, violating the UTC contract expected by domain and time-series consumers.
- **Fix:** Back `UtcNow` with a private field and call `value.ToUniversalTime()` in the property setter.

### [P3] OperationResult<T> exposes unguarded Value getter on failure results
- **Where:** `src/BuildingBlocks/YO4X.BuildingBlocks/OperationResult.cs:5-8`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  public sealed record OperationResult<T>(T? Value, IReadOnlyList<OperationError> Errors)
  {
      public bool IsSuccess => Errors.Count == 0;
  }
  ```
- **Failure:** `OperationResult<T>` is a positional record exposing `Value` directly. When an operation fails, `Value` returns `default(T)` without throwing. For value types such as `int`, `bool`, or `double`, callers that omit the `IsSuccess` check can silently read `0` or `false` as valid business values rather than failing fast.
- **Fix:** Implement an explicit getter for `Value` that throws `InvalidOperationException("Cannot access Value on a failed OperationResult.")` when `!IsSuccess`.

## Referrals
None.

## Coverage gaps
- `src/BuildingBlocks/YO4X.BuildingBlocks/OperationResult.cs`: No unit tests exist for `OperationResult<T>` to verify `IsSuccess` invariants, empty/null error arrays, or behavior on failed instances.
- `src/BuildingBlocks/YO4X.BuildingBlocks/Time.cs:21`: `FixedClock` lacks test coverage for mutation via `UtcNow` setter with non-zero timezone offsets.
- `src/BuildingBlocks/YO4X.BuildingBlocks/VersionedAggregate.cs:39-43`: `RestorePersistenceState` lacks direct unit tests verifying exception throwing when `updatedAt < CreatedAt` or `version < 0`.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 128.8s | 219601 tok | id=b0a08827-fd01-45d6-9bfd-98f38bb1bcb8
