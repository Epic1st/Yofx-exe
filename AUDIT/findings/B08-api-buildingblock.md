---
agent_id: B08
lane: api-buildingblock
scope:
  - src/BuildingBlocks/YO4X.Api/ApiFoundation.cs
  - src/BuildingBlocks/YO4X.Api/ApiHeaders.cs
  - src/BuildingBlocks/YO4X.Api/ApiProblem.cs
  - src/BuildingBlocks/YO4X.Api/AuthenticationExtensions.cs
  - src/BuildingBlocks/YO4X.Api/ClaimReader.cs
  - src/BuildingBlocks/YO4X.Api/ClientCertificateFilter.cs
  - src/BuildingBlocks/YO4X.Api/CorrelationIdMiddleware.cs
  - src/BuildingBlocks/YO4X.Api/HttpsOnlyMiddleware.cs
  - src/BuildingBlocks/YO4X.Api/MutationPreconditionFilter.cs
  - src/BuildingBlocks/YO4X.Api/ProblemStatusCodeExtensions.cs
  - src/BuildingBlocks/YO4X.Api/YO4X.Api.csproj
status: COMPLETE
generated: 2026-08-29T11:27:00Z
counts: { P0: 0, P1: 0, P2: 4, P3: 1 }
---

# B08 — api-buildingblock

## Scope audited
- `src/BuildingBlocks/YO4X.Api/ApiFoundation.cs` (207 lines)
- `src/BuildingBlocks/YO4X.Api/ApiHeaders.cs` (10 lines)
- `src/BuildingBlocks/YO4X.Api/ApiProblem.cs` (41 lines)
- `src/BuildingBlocks/YO4X.Api/AuthenticationExtensions.cs` (230 lines)
- `src/BuildingBlocks/YO4X.Api/ClaimReader.cs` (31 lines)
- `src/BuildingBlocks/YO4X.Api/ClientCertificateFilter.cs` (51 lines)
- `src/BuildingBlocks/YO4X.Api/CorrelationIdMiddleware.cs` (40 lines)
- `src/BuildingBlocks/YO4X.Api/HttpsOnlyMiddleware.cs` (33 lines)
- `src/BuildingBlocks/YO4X.Api/MutationPreconditionFilter.cs` (72 lines)
- `src/BuildingBlocks/YO4X.Api/ProblemStatusCodeExtensions.cs` (42 lines)
- `src/BuildingBlocks/YO4X.Api/YO4X.Api.csproj` (22 lines)

## Verdict
The shared API building block exhibits strong security defaults across authentication policy construction, constant-time certificate hash verification, and redacted RFC 7807/9457 problem detail mapping without leaking stack traces or database queries. However, middleware ordering in `UseYo4xApiFoundation` causes ASP.NET Core's exception handler to clear response headers, stripping all security headers (`Cache-Control`, `Content-Security-Policy`, etc.) and `X-Correlation-Id` from error responses. Additionally, `AddYo4xEmergencyAuthentication` lacks environment injection needed for development certificate pinning, `ClientCertificateFilter` uses unnormalized local `DateTime` certificate properties, and unhandled client aborts produce noisy 500 error logs.

## Findings

### [P2] Middleware ordering in `UseYo4xApiFoundation` strips security headers and `X-Correlation-Id` on exception responses
- **Where:** `src/BuildingBlocks/YO4X.Api/ApiFoundation.cs:67`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
      public static WebApplication UseYo4xApiFoundation(this WebApplication app)
      {
          app.UseMiddleware<CorrelationIdMiddleware>();
          app.UseExceptionHandler();
          app.Use(async (context, next) =>
          {
              context.Response.Headers.CacheControl = "no-store";
              context.Response.Headers.XContentTypeOptions = "nosniff";
              context.Response.Headers.XFrameOptions = "DENY";
              context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
              await next(context).ConfigureAwait(false);
          });

          return app;
      }
  ```
- **Failure:** When an endpoint throws an unhandled exception or a mapped domain/not-found/conflict exception, execution unwinds to `app.UseExceptionHandler()`. In ASP.NET Core, `ExceptionHandlerMiddleware` invokes `HttpResponse.Clear()`, which resets the response and wipes all headers set prior to the exception (including `X-Correlation-Id` added by `CorrelationIdMiddleware`). Because the security headers middleware is registered *after* `app.UseExceptionHandler()`, it is bypassed on the error path. As a result, problem responses (HTTP 404/409/422/500/503) are returned to clients without `Cache-Control: no-store`, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Content-Security-Policy`, and the `X-Correlation-Id` HTTP header.
- **Fix:** Register security headers and correlation ID using `HttpResponse.OnStarting` or place the security headers middleware before `app.UseExceptionHandler()` and re-apply the correlation header inside `Yo4xExceptionHandler`.

### [P2] `AddYo4xEmergencyAuthentication` omits `IHostEnvironment`, breaking development authority certificate pinning
- **Where:** `src/BuildingBlocks/YO4X.Api/AuthenticationExtensions.cs:111`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
      public static IServiceCollection AddYo4xEmergencyAuthentication(
          this IServiceCollection services,
          IConfiguration configuration)
      {
          services.AddAuthentication(options =>
          {
              options.DefaultAuthenticateScheme = AuthenticationSchemes.Emergency;
              options.DefaultChallengeScheme = AuthenticationSchemes.Emergency;
          })
          .AddJwtBearer(AuthenticationSchemes.Emergency, options => ConfigureJwt(options, configuration, "Emergency"));
  ```
- **Failure:** `AddYo4xEmergencyAuthentication` does not accept `IHostEnvironment? environment` (unlike `AddYo4xUserAndWorkloadAuthentication`) and invokes `ConfigureJwt` with `environment: null`. In `ConfigureJwt` (lines 166–174), when `DevelopmentAuthorityCertificateSha256` is configured in `appsettings.Development.json` for local emergency testing, `environment?.IsDevelopment() != true` evaluates to `true`, causing host startup to unconditionally throw `InvalidOperationException: A development authority certificate pin is valid only for an HTTPS loopback authority in Development.`.
- **Fix:** Add `IHostEnvironment? environment = null` to `AddYo4xEmergencyAuthentication` and forward it into `ConfigureJwt(options, configuration, "Emergency", environment)`.

### [P2] `ClientCertificateFilter` compares local `DateTime` certificate expiration timestamps against `DateTimeOffset.UtcNow`
- **Where:** `src/BuildingBlocks/YO4X.Api/ClientCertificateFilter.cs:15`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
          if (certificate is null
              || DateTimeOffset.UtcNow < certificate.NotBefore
              || DateTimeOffset.UtcNow >= certificate.NotAfter
              || !MatchesCertificate(certificate.RawData, confirmation))
          {
              return ApiProblems.Create(
                  context.HttpContext,
                  StatusCodes.Status401Unauthorized,
                  "WORKLOAD_CERTIFICATE_REQUIRED",
                  "A valid workload client certificate is required.");
          }
  ```
- **Failure:** In .NET, `X509Certificate2.NotBefore` and `NotAfter` return `DateTime` in the local system time zone (or with `DateTimeKind.Unspecified`). Direct relational comparisons against `DateTimeOffset.UtcNow` implicitly convert `DateTime` to `DateTimeOffset` using the host machine's local time zone offset. On hosts running with non-UTC local time offsets, newly generated valid certificates are falsely rejected as not yet active, or expired certificates remain accepted until the local offset margin passes.
- **Fix:** Use `certificate.GetNotBefore()` and `certificate.GetNotAfter()` (or `certificate.NotBefore.ToUniversalTime()` and `certificate.NotAfter.ToUniversalTime()`).

### [P2] `Yo4xExceptionHandler` classifies client-aborted requests as 500 `INTERNAL_ERROR` and logs unhandled exceptions
- **Where:** `src/BuildingBlocks/YO4X.Api/ApiFoundation.cs:134`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
          int status = exception switch
          {
              BackendCapabilityUnavailableException => StatusCodes.Status503ServiceUnavailable,
              ResourceNotFoundException => StatusCodes.Status404NotFound,
              ResourceConflictException => StatusCodes.Status409Conflict,
              AuthorizationDeniedException => StatusCodes.Status403Forbidden,
              UnauthorizedAccessException => StatusCodes.Status401Unauthorized,
              DomainException => StatusCodes.Status422UnprocessableEntity,
              BadHttpRequestException badRequest => badRequest.StatusCode,
              _ => StatusCodes.Status500InternalServerError
          };
  ```
- **Failure:** When a client aborts a connection during request execution, an `OperationCanceledException` is thrown. Because `Yo4xExceptionHandler` does not check `httpContext.RequestAborted.IsCancellationRequested` or handle `OperationCanceledException`, it falls through to the wildcard `_ => StatusCodes.Status500InternalServerError`. This logs an unhandled error log on normal client cancellations and attempts to write a 500 problem response to an aborted HTTP connection.
- **Fix:** Check `if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested) return false;` or return early without logging when the request is aborted.

### [P3] `CorrelationIdMiddleware.Get` generates unpersisted ephemeral IDs when invoked on uninitialized contexts
- **Where:** `src/BuildingBlocks/YO4X.Api/CorrelationIdMiddleware.cs:25`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
      public static string Get(HttpContext context) =>
          context.Items.TryGetValue(ItemKey, out object? value) && value is string correlationId
              ? correlationId
              : CreateCorrelationId();
  ```
- **Failure:** If `CorrelationIdMiddleware.Get(context)` is called on an `HttpContext` before `CorrelationIdMiddleware` has run (or outside the standard pipeline), it creates a new ID but does not write it back to `context.Items[ItemKey]`. Subsequent calls within the same request lifecycle (e.g. logging vs. problem details serialization) each generate different GUIDs, causing logged correlation IDs to mismatch response payload correlation IDs.
- **Fix:** Store the newly created correlation ID in `context.Items[ItemKey]` before returning it: `string generated = CreateCorrelationId(); context.Items[ItemKey] = generated; return generated;`.

## Referrals
- `src/Apps/YO4X.EmergencySafety.Api/Program.cs:35` — Calls `app.UseProblemStatusCodes()` rather than `app.UseYo4xProblemStatusCodes()`, diverging from building-block status code mapping.
- `src/Apps/YO4X.EmergencySafety.Api/Program.cs:18` — Calls `AddYo4xEmergencyAuthentication(builder.Configuration)` without passing `builder.Environment`.

## Coverage gaps
- `src/BuildingBlocks/YO4X.Api/ApiFoundation.cs:67` — No integration test verifies that `X-Correlation-Id` and security response headers (`Cache-Control`, `X-Content-Type-Options`) persist on responses generated via `app.UseExceptionHandler()`.
- `src/BuildingBlocks/YO4X.Api/ClientCertificateFilter.cs:15` — No unit test evaluates `ClientCertificateFilter` against certificates under non-UTC system timezone configurations.
- `src/BuildingBlocks/YO4X.Api/AuthenticationExtensions.cs:163` — No test exercises `AddYo4xEmergencyAuthentication` with `DevelopmentAuthorityCertificateSha256` configured in a development environment.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 129.6s | 292151 tok | id=118f5233-7fa7-47bc-9146-e3e355b35472
