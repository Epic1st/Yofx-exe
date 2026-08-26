using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace YO4X.DevelopmentIdentity;

public sealed class DevelopmentUser : IdentityUser<Guid>
{
    public Guid TenantId { get; set; } = LocalIdentityContract.TenantId;

    public Guid SessionId { get; set; }
}

public sealed class DevelopmentIdentityDbContext(DbContextOptions<DevelopmentIdentityDbContext> options)
    : IdentityDbContext<DevelopmentUser, IdentityRole<Guid>, Guid>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.UseOpenIddict();
        builder.Entity<DevelopmentUser>().Property(user => user.TenantId).IsRequired();
        builder.Entity<DevelopmentUser>().Property(user => user.SessionId).IsRequired();
    }
}
