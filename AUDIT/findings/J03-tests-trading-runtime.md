---
agent_id: J03
lane: Money-path coverage & Runtime/Domain test verification
scope:
  - tests/YO4X.Trading.Application.Tests/BrokerCommandCoordinatorTests.cs
  - tests/YO4X.Trading.Application.Tests/BrokerCommandLifecycleEvidenceTests.cs
  - tests/YO4X.Trading.Application.Tests/BrokerCommandReconciliationValidatorTests.cs
  - tests/YO4X.Trading.Application.Tests/BrokerCommandTestFixture.cs
  - tests/YO4X.Runtime.Tests/BrokerCommandLifecycleTests.cs
  - tests/YO4X.Runtime.Tests/Mt5ProofOnlyGatewayTests.cs
  - tests/YO4X.Runtime.Tests/OwnershipLeaseAndReadinessTests.cs
  - tests/YO4X.Runtime.Tests/RuntimeEnvelopeCursorTests.cs
  - tests/YO4X.Runtime.Tests/StrategyContractTests.cs
  - tests/YO4X.Runtime.Tests/UserOperationInvocationContractTests.cs
  - tests/YO4X.Runtime.Tests/UserOperationResultV5StrictContractTests.cs
  - tests/YO4X.Runtime.Application.Tests/StrategyEventEvidenceTests.cs
  - tests/YO4X.Runtime.Application.Tests/StrategyEventProcessingCoordinatorTests.cs
  - tests/YO4X.Runtime.Application.Tests/StrategyRuntimeFixture.cs
  - tests/YO4X.Domain.Tests/ApprovalDomainTests.cs
  - tests/YO4X.Domain.Tests/AuthorizationDomainTests.cs
  - tests/YO4X.Domain.Tests/CommandDomainTests.cs
  - tests/YO4X.Domain.Tests/CredentialIngestionDomainTests.cs
  - tests/YO4X.Domain.Tests/Mql5AbstractMemberTests.cs
  - tests/YO4X.Domain.Tests/Mql5AliasMacroTests.cs
  - tests/YO4X.Domain.Tests/Mql5BinderTemplateTests.cs
  - tests/YO4X.Domain.Tests/Mql5BuiltinCatalogArityTests.cs
  - tests/YO4X.Domain.Tests/Mql5CompilePackageDossierPlannerTests.cs
  - tests/YO4X.Domain.Tests/Mql5CorpusPath.cs
  - tests/YO4X.Domain.Tests/Mql5IsolatedCompileOrchestratorTests.cs
  - tests/YO4X.Domain.Tests/Mql5RestrictedSubsetCompilerTests.cs
  - tests/YO4X.Domain.Tests/Mql5SemanticEquivalenceVerifierTests.cs
  - tests/YO4X.Domain.Tests/Mql5SizeOfOperandTests.cs
  - tests/YO4X.Domain.Tests/Mql5TemplateLoweringTests.cs
  - tests/YO4X.Domain.Tests/Mql5TypeNameOperatorTests.cs
  - tests/YO4X.Domain.Tests/NumericRiskEvaluatorTests.cs
  - tests/YO4X.Domain.Tests/NumericRiskPolicyTests.cs
  - tests/YO4X.Domain.Tests/PolicyDomainTests.cs
status: COMPLETE
generated: 2026-08-29T11:35:00Z
counts: { P0: 0, P1: 0, P2: 0, P3: 0 }
---

# J03 — Money-path coverage & Runtime/Domain test verification

## Scope audited
All 33 test files in the assigned scope directories were read in full:
- `tests/YO4X.Trading.Application.Tests/BrokerCommandCoordinatorTests.cs` (1,212 lines)
- `tests/YO4X.Trading.Application.Tests/BrokerCommandLifecycleEvidenceTests.cs` (115 lines)
- `tests/YO4X.Trading.Application.Tests/BrokerCommandReconciliationValidatorTests.cs` (688 lines)
- `tests/YO4X.Trading.Application.Tests/BrokerCommandTestFixture.cs` (321 lines)
- `tests/YO4X.Runtime.Tests/BrokerCommandLifecycleTests.cs` (133 lines)
- `tests/YO4X.Runtime.Tests/Mt5ProofOnlyGatewayTests.cs` (270 lines)
- `tests/YO4X.Runtime.Tests/OwnershipLeaseAndReadinessTests.cs` (292 lines)
- `tests/YO4X.Runtime.Tests/RuntimeEnvelopeCursorTests.cs` (110 lines)
- `tests/YO4X.Runtime.Tests/StrategyContractTests.cs` (527 lines)
- `tests/YO4X.Runtime.Tests/UserOperationInvocationContractTests.cs` (406 lines)
- `tests/YO4X.Runtime.Tests/UserOperationResultV5StrictContractTests.cs` (232 lines)
- `tests/YO4X.Runtime.Application.Tests/StrategyEventEvidenceTests.cs` (1,263 lines)
- `tests/YO4X.Runtime.Application.Tests/StrategyEventProcessingCoordinatorTests.cs` (861 lines)
- `tests/YO4X.Runtime.Application.Tests/StrategyRuntimeFixture.cs` (199 lines)
- `tests/YO4X.Domain.Tests/ApprovalDomainTests.cs` (302 lines)
- `tests/YO4X.Domain.Tests/AuthorizationDomainTests.cs` (309 lines)
- `tests/YO4X.Domain.Tests/CommandDomainTests.cs` (371 lines)
- `tests/YO4X.Domain.Tests/CredentialIngestionDomainTests.cs` (309 lines)
- `tests/YO4X.Domain.Tests/Mql5AbstractMemberTests.cs` (136 lines)
- `tests/YO4X.Domain.Tests/Mql5AliasMacroTests.cs` (200 lines)
- `tests/YO4X.Domain.Tests/Mql5BinderTemplateTests.cs` (129 lines)
- `tests/YO4X.Domain.Tests/Mql5BuiltinCatalogArityTests.cs` (179 lines)
- `tests/YO4X.Domain.Tests/Mql5CompilePackageDossierPlannerTests.cs` (759 lines)
- `tests/YO4X.Domain.Tests/Mql5CorpusPath.cs` (14 lines)
- `tests/YO4X.Domain.Tests/Mql5IsolatedCompileOrchestratorTests.cs` (1,519 lines)
- `tests/YO4X.Domain.Tests/Mql5RestrictedSubsetCompilerTests.cs` (154 lines)
- `tests/YO4X.Domain.Tests/Mql5SemanticEquivalenceVerifierTests.cs` (626 lines)
- `tests/YO4X.Domain.Tests/Mql5SizeOfOperandTests.cs` (173 lines)
- `tests/YO4X.Domain.Tests/Mql5TemplateLoweringTests.cs` (178 lines)
- `tests/YO4X.Domain.Tests/Mql5TypeNameOperatorTests.cs` (125 lines)
- `tests/YO4X.Domain.Tests/NumericRiskEvaluatorTests.cs` (489 lines)
- `tests/YO4X.Domain.Tests/NumericRiskPolicyTests.cs` (306 lines)
- `tests/YO4X.Domain.Tests/PolicyDomainTests.cs` (260 lines)

Total audited: 33 files, 12,268 lines.

## Verdict
The audited test suite is exceptionally robust, mathematically rigorous, and structurally disciplined. Critical money-path invariants—including duplicate order prevention under retry, broker timeouts with ambiguous outcomes, exact boundary arithmetic on exposure and risk limits, hostile reconciliation payload handling, and illegal domain state transition rejection—are tested and asserted with real cryptographic signatures and concrete state assertions. Collaborator testing relies on stateful recording fixtures rather than shallow mocks, validating durable states, exact method call counts, and canonical SHA-256 payload digests. Two coverage omissions were identified around partial fills: reconciliation snapshot derivation for partially filled orders is untested in `BrokerCommandReconciliationValidatorTests`, and secondary transitions from the `PartiallyFilled` state (such as cancellation of remaining volume or multiple sequential fills) are untested in `BrokerCommandLifecycleTests`.

## Findings
None. The test files in this scope are free of defective test logic, mock pass-through fallacies, and disabled assertions.

Money-path item verification details:
1. **Duplicate order submission under retry**: Thoroughly asserted. `BrokerCommandCoordinatorTests.cs:166` (`ReplayedClaimNeverCallsGatewayAndBecomesUnknown`) proves that replayed dispatch claims never invoke `IMt5Gateway.SendAsync` (`Assert.Equal(0, gateway.SendCalls)`) and transition to `Unknown` requiring reconciliation. `BrokerCommandLifecycleTests.cs:12` (`UnknownCommandCannotBeDispatchedAgain`) verifies that attempting `BeginSend` on an `Unknown` command throws `DomainException`. `StrategyEventProcessingCoordinatorTests.cs:134` (`AlreadyCommittedClaimNeverReevaluatesEvent`) verifies event dispatch idempotency under replay.
2. **Broker response that times out with an unknown outcome**: Thoroughly asserted. `BrokerCommandCoordinatorTests.cs:320` (`SynchronousGatewayOverrunIsPersistedUnknownEvenWhenItReturnsAccepted`) and `361` (`AsynchronousGatewayOverrunIsPersistedUnknownEvenWhenItReturnsAccepted`) assert that whenever the gateway send exceeds the deadline, the coordinator forces durable disposition `Unknown` with reason code `broker_command_gateway_timeout_unknown` and outcome `ReconciliationRequired`, ignoring any late success returned by the gateway.
3. **Risk limits at their exact boundary**: Thoroughly asserted. `NumericRiskEvaluatorTests.cs:24` (`NumericExposureCapsAllowEqualityAndRejectAnyExcess`) proves that exact limit equality passes while an excess of `+0.00000001m` or `+0.01m` fails. `NumericRiskEvaluatorTests.cs:56` and `78` test exact inclusive boundaries for cashflow-adjusted daily loss and high-water drawdown. `NumericRiskEvaluatorTests.cs:130` tests snapshot freshness at the exact millisecond and verifies that `+1ms` over fails closed across all market data entities.
4. **Broker rejection and resulting local state**: Thoroughly asserted. In `BrokerCommandCoordinatorTests.cs:57` (`ReturnedRejectedIsUnknownAndRequiresReconciliation`), post-invocation broker rejections are downgraded to `Unknown` (`broker_command_gateway_outcome_unproven`) requiring reconciliation, because transport-layer reject messages cannot prove non-execution on the matching engine. Pre-invocation rejections (`BrokerCommandCoordinatorTests.cs:99` and `133`) assert terminal `submission_disabled` state without calling the gateway.
5. **Partial fills**: Partially asserted. `BrokerCommandLifecycleTests.cs:103` (`AcknowledgedCommandCanProgressThroughPartialFillToFill`) tests linear progression `Accepted -> RecordPartialFill -> RecordFilled`. (See Coverage gaps below for missing branches in reconciliation and lifecycle cancellation).
6. **Reconciliation of local state against broker state**: Thoroughly asserted. `BrokerCommandCoordinatorTests.cs:454` tests end-to-end reconciliation flow, and `BrokerCommandReconciliationValidatorTests.cs` (688 lines) exhausts 14 hostile attack scenarios, snapshot account/position/deal mismatches, and list allocation bomb vectors.
7. **Illegal state transitions being rejected**: Thoroughly asserted. `BrokerCommandLifecycleTests.cs:12-198` exhaustively verifies that invalid transitions from `ReadyToSend`, `Unknown`, `ReconciliationPending`, and terminal states (`Filled`, `Cancelled`, `Rejected`, `Reconciled`) throw `DomainException`. Governed policy, approval, and authorization transitions are similarly enforced in `PolicyDomainTests.cs`, `ApprovalDomainTests.cs`, and `AuthorizationDomainTests.cs`.
8. **Over-mocking analysis**: Collaborator test doubles (`RecordingStore`, `RecordingGateway`, `RecordingStrategyHost`) maintain real in-memory state machines and enforce canonical SHA-256 digest validation (`CanonicalJson.Sha256(...)`), normalized timestamp formatting, and exact call count tracking. No test asserts solely that a mock was invoked.

## Referrals
None.

## Coverage gaps
- **Untested `BrokerReconciliationMatch.PartiallyFilled` branch in `BrokerCommandReconciliationValidator`**:
  `src/Application/YO4X.Trading.Application/BrokerCommandReconciliationValidator.cs:315-322` contains logic deriving `BrokerReconciliationMatch.PartiallyFilled` when `0 < filled < command.Volume` and `order.RemainingVolume == command.Volume - filled` and `reported.DealId is not null`. In `tests/YO4X.Trading.Application.Tests/BrokerCommandReconciliationValidatorTests.cs`, this branch is never exercised or asserted with an order/deal snapshot, leaving partial fill reconciliation validation untested.
- **Untested non-linear `PartiallyFilled` lifecycle transitions in `BrokerCommandLifecycle`**:
  `src/Runtime/YO4X.Trading.Abstractions/BrokerCommandLifecycle.cs:96, 108, 116` permits `RecordPartialFill` (sequential partial fills), `RecordCancelled` (cancelling the unfilled balance of a partially filled order), and `MarkUnknownAfterInterruptedSend` from the `BrokerCommandState.PartiallyFilled` state. `tests/YO4X.Runtime.Tests/BrokerCommandLifecycleTests.cs` only tests `Acknowledged -> PartiallyFilled -> Filled` (lines 103–124) and does not assert multiple partial fills, partial fill cancellation, or partial fill timeout/interruption.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 290.9s | 607599 tok | id=5a4f1cc6-c68f-4280-88b7-edf54b30ca46
