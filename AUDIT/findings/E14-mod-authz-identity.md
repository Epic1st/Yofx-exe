---
agent_id: E14
lane: Authorization, Identity, AdminIdentity, Tenancy
scope:
  - src/Modules/Authorization/YO4X.Authorization/AuthorizationDecisionEngine.cs
  - src/Modules/Authorization/YO4X.Authorization/AuthorizationModels.cs
  - src/Modules/Authorization/YO4X.Authorization/YO4X.Authorization.csproj
  - src/Modules/Identity/YO4X.Identity/UserIdentity.cs
  - src/Modules/Identity/YO4X.Identity/SessionFamily.cs
  - src/Modules/Identity/YO4X.Identity/YO4X.Identity.csproj
  - src/Modules/AdminIdentity/YO4X.AdminIdentity/AdminSession.cs
  - src/Modules/AdminIdentity/YO4X.AdminIdentity/YO4X.AdminIdentity.csproj
  - src/Modules/Tenancy/YO4X.Tenancy/TenantExecutionContext.cs
  - src/Modules/Tenancy/YO4X.Tenancy/YO4X.Tenancy.csproj
status: COMPLETE
generated: 2026-08-29T11:28:00Z
counts: { P0: 0, P1: 1, P2: 1, P3: 1 }
---

# E14 — Authorization, Identity, AdminIdentity, Tenancy

## Scope audited
- `src/Modules/Authorization/YO4X.Authorization/AuthorizationDecisionEngine.cs` (228 lines)
- `src/Modules/Authorization/YO4X.Authorization/AuthorizationModels.cs` (392 lines)
- `src/Modules/Authorization/YO4X.Authorization/YO4X.Authorization.csproj` (14 lines)
- `src/Modules/Identity/YO4X.Identity/UserIdentity.cs` (129 lines)
- `src/Modules/Identity/YO4X.Identity/SessionFamily.cs` (150 lines)
- `src/Modules/Identity/YO4X.Identity/YO4X.Identity.csproj` (14 lines)
- `src/Modules/AdminIdentity/YO4X.AdminIdentity/AdminSession.cs` (131 lines)
- `src/Modules/AdminIdentity/YO4X.AdminIdentity/YO4X.AdminIdentity.csproj` (14 lines)
- `src/Modules/Tenancy/YO4X.Tenancy/TenantExecutionContext.cs` (87 lines)
- `src/Modules/Tenancy/YO4X.Tenancy/YO4X.Tenancy.csproj` (14 lines)

## Verdict
The core authorization evaluation model and tenancy context primitives are robust, defaulting strictly to deny and enforcing invariant checks across actor types, assurance levels, purpose tokens, and tenant scoping. `TenantExecutionContext` guarantees non-null, validated tenant identifiers that are verified end-to-end at the database session level. However, lifecycle edge cases in identity aggregate state transitions permit state bypasses during email verification on locked accounts and suppress replay-attack compromise detection on expired session families.

## Findings

### [P1] UserIdentity.VerifyEmail bypasses lock and recovery states when email is unverified
- **Where:** `src/Modules/Identity/YO4X.Identity/UserIdentity.cs:58`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        if (EmailVerifiedAt is not null)
        {
            return;
        }

        EmailVerifiedAt = occurredAt.ToUniversalTime();
        SecurityState = UserSecurityState.Active;
        RecordChange(occurredAt);
  ```
- **Failure:** An invited user aggregate is initialized with `SecurityState = UserSecurityState.Invited` and `EmailVerifiedAt = null`. If the identity is subsequently locked via `Lock(t1)` due to security abuse, `SecurityState` transitions to `UserSecurityState.Locked` with `LockedAt = t1`. When a delayed or replayed email verification action triggers `VerifyEmail(t2)`, `EnsureNotDisabled()` succeeds, and line 64 unconditionally mutates `SecurityState` to `UserSecurityState.Active`. This bypasses `CompleteVerifiedRecovery()` without clearing `LockedAt`, resulting in an active account in an inconsistent aggregate state.
- **Fix:** Restrict `VerifyEmail` to only transition `SecurityState` from `UserSecurityState.Invited` to `UserSecurityState.Active`, rejecting verification or throwing a `DomainException` if `SecurityState` is `Locked` or `RecoveryRequired`.

### [P2] SessionFamily.Rotate prioritizes expiration over compromised token detection
- **Where:** `src/Modules/Identity/YO4X.Identity/SessionFamily.cs:91`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        if (State != SessionState.Active || occurredAt >= ExpiresAt)
        {
            State = occurredAt >= ExpiresAt ? SessionState.Expired : State;
            return new RefreshRotationResult(false, State == SessionState.Compromised, Generation);
        }

        if (_invalidatedTokenHashes.Contains(presentedTokenHash))
        {
            State = SessionState.Compromised;
  ```
- **Failure:** When a session expires (`occurredAt >= ExpiresAt`), line 91 returns early before checking `_invalidatedTokenHashes.Contains(presentedTokenHash)` at line 97. If an adversary attempts to rotate a previously invalidated refresh token from a compromised session after expiry, the replay detection is bypassed, returning `RefreshRotationResult(Accepted: false, FamilyCompromised: false, Generation)`. The session family is never marked `Compromised` and `RevokedAt` is not recorded.
- **Fix:** Check `_invalidatedTokenHashes.Contains(presentedTokenHash)` before checking expiration in `Rotate()`, transitioning the state to `Compromised` whenever an invalidated token hash is presented.

### [P3] AdminSession.RequireFreshStepUp does not reject future authentication timestamps
- **Where:** `src/Modules/AdminIdentity/YO4X.AdminIdentity/AdminSession.cs:92`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
    public void RequireFreshStepUp(DateTimeOffset now, TimeSpan maximumAge)
    {
        EnsureActive(now);
        if (now - AuthenticatedAt > maximumAge)
        {
            throw new DomainException("STEP_UP_REQUIRED", "The admin session assurance is too old for this action.");
        }
    }
  ```
- **Failure:** If clock skew or an unaligned timestamp results in `AuthenticatedAt > now`, `now - AuthenticatedAt` produces a negative `TimeSpan`, which evaluates as strictly less than `maximumAge`. As a result, `RequireFreshStepUp` succeeds without raising `STEP_UP_REQUIRED`, unlike `AuthorizationDecisionEngine.Evaluate` which explicitly validates `actor.AuthenticatedAt > now`.
- **Fix:** Update `RequireFreshStepUp` to validate `if (AuthenticatedAt > now || now - AuthenticatedAt > maximumAge)`.

## Referrals
None.

## Coverage gaps
- `src/Modules/Identity/YO4X.Identity/UserIdentity.cs:55-66` — Untested branch when `VerifyEmail` is invoked on an identity in `UserSecurityState.Locked` or `UserSecurityState.RecoveryRequired` state while `EmailVerifiedAt` is null.
- `src/Modules/Identity/YO4X.Identity/SessionFamily.cs:91-103` — Untested branch in `SessionFamily.Rotate` when an invalidated refresh token is presented after `occurredAt >= ExpiresAt`.
- `src/Modules/AdminIdentity/YO4X.AdminIdentity/AdminSession.cs:92-99` — Untested branch in `AdminSession.RequireFreshStepUp` when `AuthenticatedAt > now`.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 95.7s | 246584 tok | id=158d63c6-b784-44cc-ab20-4eccb164c2e2
