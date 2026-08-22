using YO4X.Runtime.Contracts;

namespace YO4X.Trading.Abstractions;

public sealed record ExecutionLeaseTrustVerification(
    bool IsTrusted,
    string ReasonCode,
    string? SignatureAlgorithm,
    string? SigningKeyId,
    string? TrustedVerificationKeySha256);

public interface IExecutionLeaseTrustVerifier
{
    ExecutionLeaseTrustVerification Verify(SignedExecutionLease lease);
}
