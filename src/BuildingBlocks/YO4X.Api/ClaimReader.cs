using System.Security.Claims;

namespace YO4X.Api;

public static class ClaimReader
{
    public static Guid RequiredGuid(ClaimsPrincipal principal, string claimType)
    {
        ArgumentNullException.ThrowIfNull(principal);
        string? value = principal.FindFirstValue(claimType);
        if (!Guid.TryParse(value, out Guid parsed) || parsed == Guid.Empty)
        {
            throw new UnauthorizedAccessException($"The required '{claimType}' identity claim is missing or invalid.");
        }

        return parsed;
    }

    public static string Required(ClaimsPrincipal principal, string claimType)
    {
        ArgumentNullException.ThrowIfNull(principal);
        string? value = principal.FindFirstValue(claimType);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new UnauthorizedAccessException($"The required '{claimType}' identity claim is missing.");
        }

        return value;
    }
}
