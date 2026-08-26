using YO4X.Trading.Abstractions;

namespace YO4X.Trading.ProcessIsolation;

internal static class BrokerWorkerContractValidator
{
    internal const int MaximumCommandIds = 64;
    private const int MaximumCollectionEntries = 2048;
    private const int MaximumTextLength = 256;

    internal static void ValidateRequest(BrokerWorkerRequest request, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ContractVersion != BrokerWorkerProtocolContract.Version
            || request.RequestId == Guid.Empty
            || request.DeadlineUtc.Offset != TimeSpan.Zero
            || request.DeadlineUtc <= nowUtc
            || request.DeadlineUtc > nowUtc.AddMinutes(2))
        {
            throw InvalidContract();
        }

        switch (request.Operation)
        {
            case BrokerWorkerProtocolContract.SendOperation:
                if (request.Send is null
                    || request.Reconcile is not null
                    || request.ConnectProbe is not null)
                {
                    throw InvalidContract();
                }

                ValidateSendRequest(request.Send);
                break;
            case BrokerWorkerProtocolContract.ReconcileOperation:
                if (request.Reconcile is null
                    || request.Send is not null
                    || request.ConnectProbe is not null)
                {
                    throw InvalidContract();
                }

                ValidateReconcileRequest(request.Reconcile);
                break;
            case BrokerWorkerProtocolContract.ConnectProbeOperation:
                if (request.ConnectProbe is null
                    || request.Send is not null
                    || request.Reconcile is not null)
                {
                    throw InvalidContract();
                }

                ValidateConnectProbeRequest(request.ConnectProbe, nowUtc, request.DeadlineUtc);
                break;
            default:
                throw InvalidContract();
        }
    }

    internal static void ValidateResponse(
        BrokerWorkerResponse response,
        BrokerWorkerRequest request)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(request);
        if (response.ContractVersion != BrokerWorkerProtocolContract.Version
            || response.RequestId != request.RequestId
            || !string.Equals(response.Operation, request.Operation, StringComparison.Ordinal)
            || !IsCode(response.Code))
        {
            throw InvalidContract();
        }

        switch (response.Operation)
        {
            case BrokerWorkerProtocolContract.SendOperation:
                if (response.SendResult is null
                    || response.ReconciliationSnapshot is not null
                    || response.ConnectProbeObservation is not null)
                {
                    throw InvalidContract();
                }

                ValidateSendResult(response.SendResult);
                if (!string.Equals(
                        response.Code,
                        response.SendResult.Code,
                        StringComparison.Ordinal)
                    || response.IsSuccess
                    != (response.SendResult.Disposition == GatewayCommandDisposition.Accepted))
                {
                    throw InvalidContract();
                }

                break;
            case BrokerWorkerProtocolContract.ReconcileOperation:
                if (response.SendResult is not null
                    || response.ConnectProbeObservation is not null
                    || response.IsSuccess != (response.ReconciliationSnapshot is not null))
                {
                    throw InvalidContract();
                }

                if (response.ReconciliationSnapshot is not null)
                {
                    ValidateSnapshot(response.ReconciliationSnapshot, request.Reconcile!);
                }

                break;
            case BrokerWorkerProtocolContract.ConnectProbeOperation:
                ValidateConnectProbeResponse(
                    response,
                    request.ConnectProbe!,
                    request.DeadlineUtc);
                break;
            default:
                throw InvalidContract();
        }
    }

    private static void ValidateSendRequest(BrokerWorkerSendRequest request)
    {
        if (request.BrokerAccountId == Guid.Empty
            || request.GatewayArtifactId == Guid.Empty
            || !IsSha256(request.GatewayArtifactSha256)
            || !IsSha256(request.AuthorizationSha256))
        {
            throw InvalidContract();
        }

        NormalizedBrokerCommand command = request.Command
            ?? throw InvalidContract();
        if (command.ContractVersion <= 0
            || command.CommandId == Guid.Empty
            || command.IntentId == Guid.Empty
            || command.DeploymentId == Guid.Empty
            || command.Generation <= 0
            || !IsText(command.IdempotencyKey, 200)
            || !Enum.IsDefined(command.Action)
            || !IsText(command.Symbol, 64)
            || !Enum.IsDefined(command.Side)
            || !Enum.IsDefined(command.OrderType)
            || command.Volume <= decimal.Zero
            || command.RequestedPrice is <= decimal.Zero
            || command.StopLoss is <= decimal.Zero
            || command.TakeProfit is <= decimal.Zero
            || command.MaximumDeviationPoints is < 0 or > 1_000_000
            || !IsText(command.OwnershipTag, 200)
            || (command.TargetKind is not null && !Enum.IsDefined(command.TargetKind.Value))
            || !IsOptionalText(command.TargetBrokerId, 200)
            || command.ExpectedTargetVolume is <= decimal.Zero
            || !IsOptionalText(command.ExpectedTargetStatus, 100)
            || command.ExpectedTargetStopLoss is <= decimal.Zero
            || command.ExpectedTargetTakeProfit is <= decimal.Zero
            || command.CreatedAtUtc.Offset != TimeSpan.Zero)
        {
            throw InvalidContract();
        }
    }

    private static void ValidateReconcileRequest(BrokerWorkerReconcileRequest request)
    {
        if (request.CommandIds is null
            || request.CommandIds.Count is < 1 or > MaximumCommandIds
            || request.CommandIds.Any(id => id == Guid.Empty)
            || request.CommandIds.Distinct().Count() != request.CommandIds.Count)
        {
            throw InvalidContract();
        }
    }

    private static void ValidateConnectProbeRequest(
        BrokerWorkerConnectProbeRequest request,
        DateTimeOffset nowUtc,
        DateTimeOffset deadlineUtc)
    {
        if (request.BrokerAccountId == Guid.Empty
            || request.GatewayArtifactId == Guid.Empty
            || !IsSha256(request.GatewayArtifactSha256)
            || !IsSha256(request.CredentialKey)
            || !IsSha256(request.CredentialVaultIdentitySha256)
            || request.Server is null
            || !IsText(request.Server.BrokerCompany, MaximumTextLength)
            || !IsText(request.Server.ServerName, MaximumTextLength)
            || request.ExpectedEnvironment != BrokerEnvironment.Demo
            || request.ProbeNotBeforeUtc.Offset != TimeSpan.Zero
            || request.ProbeNotBeforeUtc > nowUtc
            || request.ProbeNotBeforeUtc < nowUtc.AddMinutes(-2)
            || request.ProbeNotBeforeUtc >= deadlineUtc)
        {
            throw InvalidContract();
        }
    }

    private static void ValidateConnectProbeResponse(
        BrokerWorkerResponse response,
        BrokerWorkerConnectProbeRequest request,
        DateTimeOffset deadlineUtc)
    {
        if (response.SendResult is not null || response.ReconciliationSnapshot is not null)
        {
            throw InvalidContract();
        }

        if (!response.IsSuccess)
        {
            if (response.ConnectProbeObservation is not null
                || response.Code is not (
                    BrokerWorkerProtocolContract.ConnectProbeUnavailableCode or
                    BrokerWorkerProtocolContract.ConnectProbeRejectedCode or
                    BrokerWorkerProtocolContract.ConnectProbeFailedCode))
            {
                throw InvalidContract();
            }

            return;
        }

        BrokerConnectionProbeObservation observation = response.ConnectProbeObservation
            ?? throw InvalidContract();
        if (response.Code != BrokerWorkerProtocolContract.ConnectProbeSucceededCode
            || observation.ContractVersion
                != BrokerWorkerProtocolContract.ConnectProbeObservationVersion
            || observation.BrokerAccountId != request.BrokerAccountId
            || observation.GatewayArtifactId != request.GatewayArtifactId
            || !string.Equals(
                observation.GatewayArtifactSha256,
                request.GatewayArtifactSha256,
                StringComparison.Ordinal)
            || !IsMaskedLogin(observation.MaskedLogin)
            || !string.Equals(
                observation.BrokerCompany,
                request.Server.BrokerCompany,
                StringComparison.Ordinal)
            || !string.Equals(
                observation.ServerName,
                request.Server.ServerName,
                StringComparison.Ordinal)
            || !Enum.IsDefined(observation.AccountMode)
            || observation.Environment != BrokerEnvironment.Demo
            || !Enum.IsDefined(observation.TradingAccess)
            || !IsCurrency(observation.Currency)
            || !observation.DisconnectConfirmed
            || observation.ObservedAtUtc == default
            || observation.ObservedAtUtc.Offset != TimeSpan.Zero
            || observation.ObservedAtUtc < request.ProbeNotBeforeUtc
            || observation.ObservedAtUtc > deadlineUtc)
        {
            throw InvalidContract();
        }
    }

    private static void ValidateSendResult(GatewaySendResult result)
    {
        if (!Enum.IsDefined(result.Disposition)
            || !IsCode(result.Code)
            || !IsOptionalText(result.BrokerRequestId, 200)
            || !IsOptionalText(result.OrderId, 200)
            || !IsOptionalText(result.DealId, 200)
            || result.ObservedAtUtc.Offset != TimeSpan.Zero
            || (result.PreInvocationNotSentProven
                && result.Disposition is GatewayCommandDisposition.Accepted
                    or GatewayCommandDisposition.Unknown))
        {
            throw InvalidContract();
        }
    }

    private static void ValidateSnapshot(
        BrokerReconciliationSnapshot snapshot,
        BrokerWorkerReconcileRequest request)
    {
        if (snapshot.ContractVersion <= 0
            || snapshot.SourceSequence < 0
            || snapshot.BrokerAccountId == Guid.Empty
            || snapshot.DeploymentId == Guid.Empty
            || snapshot.Generation <= 0
            || snapshot.GatewayArtifactId == Guid.Empty
            || !IsSha256(snapshot.GatewayArtifactSha256)
            || !IsUtcOrder(snapshot.QueryWindowStartUtc, snapshot.QueryWindowEndUtc)
            || snapshot.CompletedAtUtc.Offset != TimeSpan.Zero
            || snapshot.CompletedAtUtc < snapshot.QueryWindowEndUtc
            || snapshot.Account is null
            || snapshot.Positions is null
            || snapshot.Orders is null
            || snapshot.Deals is null
            || snapshot.CommandResults is null
            || snapshot.Positions.Count > MaximumCollectionEntries
            || snapshot.Orders.Count > MaximumCollectionEntries
            || snapshot.Deals.Count > MaximumCollectionEntries
            || snapshot.CommandResults.Count > MaximumCommandIds)
        {
            throw InvalidContract();
        }

        ValidateAccount(snapshot.Account);
        foreach (BrokerPositionSnapshot position in snapshot.Positions)
        {
            if (!IsText(position.PositionId, MaximumTextLength)
                || !IsText(position.Symbol, 64)
                || !Enum.IsDefined(position.Side)
                || position.Volume <= decimal.Zero
                || position.OpenPrice <= decimal.Zero
                || position.StopLoss is <= decimal.Zero
                || position.TakeProfit is <= decimal.Zero
                || !IsText(position.OwnershipTag, 200)
                || position.ObservedAtUtc.Offset != TimeSpan.Zero)
            {
                throw InvalidContract();
            }
        }

        foreach (BrokerOrderSnapshot order in snapshot.Orders)
        {
            if (!IsText(order.OrderId, MaximumTextLength)
                || !IsText(order.Symbol, 64)
                || !Enum.IsDefined(order.Side)
                || !Enum.IsDefined(order.OrderType)
                || order.RequestedVolume <= decimal.Zero
                || order.RemainingVolume < decimal.Zero
                || order.RemainingVolume > order.RequestedVolume
                || order.RequestedPrice is <= decimal.Zero
                || order.StopLoss is <= decimal.Zero
                || order.TakeProfit is <= decimal.Zero
                || !IsText(order.Status, 100)
                || !IsText(order.OwnershipTag, 200)
                || order.ObservedAtUtc.Offset != TimeSpan.Zero)
            {
                throw InvalidContract();
            }
        }

        foreach (BrokerDealSnapshot deal in snapshot.Deals)
        {
            if (!IsText(deal.DealId, MaximumTextLength)
                || !IsText(deal.OrderId, MaximumTextLength)
                || !IsText(deal.Symbol, 64)
                || !Enum.IsDefined(deal.Side)
                || deal.Volume <= decimal.Zero
                || deal.Price <= decimal.Zero
                || deal.BrokerTimestampUtc.Offset != TimeSpan.Zero)
            {
                throw InvalidContract();
            }
        }

        HashSet<Guid> requestedIds = request.CommandIds.ToHashSet();
        foreach (BrokerCommandReconciliation result in snapshot.CommandResults)
        {
            if (!requestedIds.Contains(result.CommandId)
                || !Enum.IsDefined(result.Match)
                || !IsCode(result.ReasonCode)
                || !IsOptionalText(result.OrderId, MaximumTextLength)
                || !IsOptionalText(result.DealId, MaximumTextLength)
                || result.ReconciledAtUtc.Offset != TimeSpan.Zero)
            {
                throw InvalidContract();
            }
        }
    }

    private static void ValidateAccount(BrokerAccountSnapshot account)
    {
        if (account.Sequence < 0
            || !IsText(account.MaskedLogin, 64)
            || !IsText(account.BrokerCompany, MaximumTextLength)
            || !IsText(account.ServerName, MaximumTextLength)
            || !Enum.IsDefined(account.AccountMode)
            || !Enum.IsDefined(account.Environment)
            || !Enum.IsDefined(account.TradingAccess)
            || !IsText(account.Currency, 16)
            || account.ObservedAtUtc.Offset != TimeSpan.Zero)
        {
            throw InvalidContract();
        }
    }

    private static bool IsUtcOrder(DateTimeOffset start, DateTimeOffset end) =>
        start.Offset == TimeSpan.Zero && end.Offset == TimeSpan.Zero && start <= end;

    private static bool IsCode(string? value) =>
        IsText(value, 128)
        && value!.All(character => char.IsAsciiLetterOrDigit(character)
            || character is '_' or '-' or '.');

    private static bool IsOptionalText(string? value, int maximumLength) =>
        value is null || IsText(value, maximumLength);

    private static bool IsCurrency(string? value) =>
        value is { Length: >= 3 and <= 16 }
        && value.All(character => character is >= 'A' and <= 'Z');

    private static bool IsMaskedLogin(string? value)
    {
        if (value is not { Length: >= 2 and <= 64 })
        {
            return false;
        }

        int firstDigit = value.IndexOfAnyInRange('0', '9');
        if (firstDigit < 1 || value.Length - firstDigit is < 1 or > 4)
        {
            return false;
        }

        return value.AsSpan(0, firstDigit).IndexOfAnyExcept('*') < 0
            && value.AsSpan(firstDigit).IndexOfAnyExceptInRange('0', '9') < 0;
    }

    private static bool IsText(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= maximumLength
        && string.Equals(value, value.Trim(), StringComparison.Ordinal)
        && !value.Any(char.IsControl);

    private static bool IsSha256(string? value)
    {
        if (value is null || value.Length != 64)
        {
            return false;
        }

        foreach (char character in value)
        {
            if (!((character is >= '0' and <= '9')
                || (character is >= 'a' and <= 'f')))
            {
                return false;
            }
        }

        return true;
    }

    private static InvalidDataException InvalidContract() =>
        new("The broker worker message contract is invalid.");
}
