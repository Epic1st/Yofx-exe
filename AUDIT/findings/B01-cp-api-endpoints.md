---
agent_id: B01
lane: cp-api-endpoints
scope:
  - src/Apps/YO4X.ControlPlane.Api/FrontendProjectionEndpoints.cs
  - src/Apps/YO4X.ControlPlane.Api/BrokerAccountDiscoveryEndpoints.cs
  - src/Apps/YO4X.ControlPlane.Api/BrokerAccountRegistrationBody.cs
status: COMPLETE
generated: 2026-08-29T08:55:00Z
counts: { P0: 0, P1: 0, P2: 1, P3: 1 }
---

# B01 — cp-api-endpoints

## Scope audited
- `src/Apps/YO4X.ControlPlane.Api/FrontendProjectionEndpoints.cs` (308 lines)
- `src/Apps/YO4X.ControlPlane.Api/BrokerAccountDiscoveryEndpoints.cs` (54 lines)
- `src/Apps/YO4X.ControlPlane.Api/BrokerAccountRegistrationBody.cs` (207 lines)

## Verdict
The endpoint routing, authorization scoping, and model validation across this lane are sound. Route parameters are constrained with `:guid`, every handler derives a typed `UserActor` from authenticated claims, and all projection and discovery queries enforce tenant and user isolation without IDOR exposure. Secret deserialization in `CreateBrokerAccountBody` strictly zeroes in-memory password bytes upon disposal and validates broker credential parameters in constant time. Two issues were identified: creation mutation endpoints (`POST /bots` and `POST /backtests`) lack idempotency filter guards, permitting duplicate entity generation on network retry; and identity claim parsing logic is copy-pasted across multiple endpoint definitions.

## Findings

### [P2] Mutation creation endpoints in FrontendProjectionEndpoints lack idempotency precondition filters
- **Where:** `src/Apps/YO4X.ControlPlane.Api/FrontendProjectionEndpoints.cs:155`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  user.MapPost("/bots", async (
      CreateBot request,
      HttpContext context,
      IFrontendProjectionApplication application,
      CancellationToken cancellationToken) =>
  {
      BotView view = await application.CreateBotAsync(
          ToUserActor(context.User), request, cancellationToken);
      return Results.Created($"/v1/bots/{view.Id:D}", view);
  });
  ```
- **Failure:** When a client issues `POST /v1/bots` or `POST /v1/backtests` and encounters a network timeout or proxy replay, the client retries the request. Because `MutationPreconditionFilter` is not attached to these routes (unlike mutation routes in `Program.cs`), the server creates multiple distinct records with new version 7 GUIDs in `bots.bots` and `simulation.backtests`, resulting in unwanted duplicate bot configurations and redundant backtest executions.
- **Fix:** Apply `.AddEndpointFilter(new MutationPreconditionFilter())` to `POST /bots` and `POST /backtests`, and plumb request metadata through to the underlying application to enforce idempotency key deduplication.

### [P3] Duplicated UserActor claims extraction and assurance parsing across endpoint definitions
- **Where:** `src/Apps/YO4X.ControlPlane.Api/FrontendProjectionEndpoints.cs:290`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  private static UserActor ToUserActor(ClaimsPrincipal principal)
  {
      string assuranceValue = principal.FindFirstValue("assurance") ?? "password";
      AuthenticationAssurance assurance = assuranceValue.ToLowerInvariant() switch
      {
          "hardware_key" => AuthenticationAssurance.HardwareKey,
          "webauthn" => AuthenticationAssurance.WebAuthn,
          "totp" => AuthenticationAssurance.Totp,
          _ => AuthenticationAssurance.Password
      };

      return new UserActor(
          ClaimReader.RequiredGuid(principal, "tenant_id"),
          ClaimReader.RequiredGuid(principal, "sub"),
          ClaimReader.RequiredGuid(principal, "session_id"),
          assurance);
  }
  ```
- **Failure:** The identical `ToUserActor(ClaimsPrincipal)` method is copy-pasted in `FrontendProjectionEndpoints.cs:290-306`, `BrokerAccountDiscoveryEndpoints.cs:36-52`, and `Program.cs:541-557`. Future modifications to claim schemas, default assurance handling, or MFA policy mapping in one location risk leaving other endpoints running divergent authentication and tenant extraction logic.
- **Fix:** Extract `ToUserActor` into a centralized helper or extension method on `ClaimsPrincipal` in `YO4X.Identity` or `YO4X.ControlPlane.Api` and reference it consistently across all route definitions.

## Referrals
None.

## Coverage gaps
None.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 182.3s | 393063 tok | id=1a570e7f-2ce7-452b-b3fd-53e49c9992df
