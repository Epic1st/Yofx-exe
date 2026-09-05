using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using YO4X.Api;
using YO4X.ControlPlane.Application;
using YO4X.Persistence.Postgres;
using YO4X.StrategyGovernance.Licensing;
using YO4X.StrategyGovernance.Packaging;
using YO4X.Tenancy;

namespace YO4X.ControlPlane.Api;

internal sealed record MarketplacePurchaseRequest(Guid StrategyId);
internal sealed record LocalBotRunStateRequest(string Token, string State, string? Error);

internal static class MarketplaceUserEndpoints
{
    private static readonly TimeSpan RunLifetime = TimeSpan.FromMinutes(10);

    internal static void MapMarketplaceUserEndpoints(this RouteGroupBuilder user)
    {
        user.MapGet("/marketplace/purchases", GetPurchasesAsync);
        user.MapPost("/marketplace/purchases", PurchaseFreeStrategyAsync);
        user.MapPost("/bots/{botId:guid}/local-execution-bundles", IssueLocalBundleAsync);
        user.MapPost("/local-executions/{runId:guid}/state", ReportLocalStateAsync);
    }

    private static async Task<IResult> GetPurchasesAsync(
        HttpContext http,
        PostgresDatabase database,
        CancellationToken cancellationToken)
    {
        UserActor actor = ToUserActor(http.User);
        await using TenantPostgresTransaction transaction = await BeginAsync(database, actor, cancellationToken);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select purchase.id, purchase.strategy_id,
                   coalesce(listing.title, strategy.name), purchase.status,
                   purchase.price_cents, purchase.currency, purchase.purchased_at
            from marketplace.purchases as purchase
            join catalog.strategies as strategy
              on strategy.tenant_id = purchase.tenant_id and strategy.id = purchase.strategy_id
            left join marketplace.listings as listing
              on listing.tenant_id = purchase.tenant_id and listing.id = purchase.listing_id
            where purchase.tenant_id = @tenant and purchase.buyer_user_id = @user
            order by purchase.purchased_at desc, purchase.id desc
            """);
        Add(command, "tenant", actor.TenantId);
        Add(command, "user", actor.UserId);
        var rows = new List<object>();
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new
            {
                id = reader.GetGuid(0),
                strategyId = reader.GetGuid(1),
                strategyName = reader.GetString(2),
                status = reader.GetString(3).ToUpperInvariant(),
                priceCents = reader.GetInt32(4),
                currency = reader.GetString(5),
                purchasedAt = reader.GetFieldValue<DateTimeOffset>(6)
            });
        }
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(rows);
    }

    private static async Task<IResult> PurchaseFreeStrategyAsync(
        MarketplacePurchaseRequest request,
        HttpContext http,
        PostgresDatabase database,
        CancellationToken cancellationToken)
    {
        if (request.StrategyId == Guid.Empty)
            return Results.BadRequest();
        UserActor actor = ToUserActor(http.User);
        await using TenantPostgresTransaction transaction = await BeginAsync(database, actor, cancellationToken);
        await using (NpgsqlCommand existing = transaction.CreateCommand(
            """
            select purchase.id, coalesce(listing.title, strategy.name), purchase.price_cents,
                   purchase.currency, purchase.purchased_at
            from marketplace.purchases as purchase
            join catalog.strategies as strategy
              on strategy.tenant_id = purchase.tenant_id and strategy.id = purchase.strategy_id
            left join marketplace.listings as listing
              on listing.tenant_id = purchase.tenant_id and listing.id = purchase.listing_id
            where purchase.tenant_id = @tenant and purchase.buyer_user_id = @user
              and purchase.strategy_id = @strategy and purchase.status = 'paid'
            """))
        {
            Add(existing, "tenant", actor.TenantId);
            Add(existing, "user", actor.UserId);
            Add(existing, "strategy", request.StrategyId);
            await using NpgsqlDataReader reader = await existing.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                object response = new
                {
                    id = reader.GetGuid(0),
                    strategyId = request.StrategyId,
                    strategyName = reader.GetString(1),
                    status = "PAID",
                    priceCents = reader.GetInt32(2),
                    currency = reader.GetString(3),
                    purchasedAt = reader.GetFieldValue<DateTimeOffset>(4)
                };
                await reader.DisposeAsync();
                await transaction.CommitAsync(cancellationToken);
                return Results.Ok(response);
            }
        }
        Guid? listingId;
        string title;
        await using (NpgsqlCommand listing = transaction.CreateCommand(
            """
            select listing.id, coalesce(listing.title, strategy.name)
            from catalog.strategies as strategy
            left join marketplace.listings as listing
              on listing.tenant_id = strategy.tenant_id
             and listing.strategy_id = strategy.id
             and listing.state = 'listed'
            where strategy.tenant_id = @tenant and strategy.id = @strategy
              and strategy.is_free
              and strategy.cloud_price_monthly_cents = 0
              and strategy.cloud_price_yearly_cents = 0
              and
              (
                  listing.id is null
                  or (listing.price_monthly_cents = 0 and listing.price_yearly_cents = 0)
              )
            """))
        {
            Add(listing, "tenant", actor.TenantId);
            Add(listing, "strategy", request.StrategyId);
            await using NpgsqlDataReader reader = await listing.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status402PaymentRequired,
                    title: "This strategy requires a completed checkout before it can be acquired.");
            }
            listingId = reader.IsDBNull(0) ? null : reader.GetGuid(0);
            title = reader.GetString(1);
        }

        Guid purchaseId = Guid.CreateVersion7();
        await using (NpgsqlCommand insert = transaction.CreateCommand(
            """
            insert into marketplace.purchases
                (id, tenant_id, buyer_user_id, listing_id, strategy_id,
                 status, price_cents, currency)
            values
                (@id, @tenant, @user, @listing, @strategy, 'paid', 0, 'USD')
            on conflict (tenant_id, buyer_user_id, strategy_id) where status = 'paid'
            do update set updated_at = clock_timestamp()
            returning id, purchased_at
            """))
        {
            Add(insert, "id", purchaseId);
            Add(insert, "tenant", actor.TenantId);
            Add(insert, "user", actor.UserId);
            insert.Parameters.AddWithValue(
                "listing",
                NpgsqlDbType.Uuid,
                listingId is null ? DBNull.Value : listingId.Value);
            Add(insert, "strategy", request.StrategyId);
            await using NpgsqlDataReader reader = await insert.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            purchaseId = reader.GetGuid(0);
            DateTimeOffset purchasedAt = reader.GetFieldValue<DateTimeOffset>(1);
            await reader.DisposeAsync();
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(new
            {
                id = purchaseId,
                strategyId = request.StrategyId,
                strategyName = title,
                status = "PAID",
                priceCents = 0,
                currency = "USD",
                purchasedAt
            });
        }
    }

    private static async Task<IResult> IssueLocalBundleAsync(
        Guid botId,
        HttpContext http,
        IConfiguration configuration,
        PostgresDatabase database,
        CancellationToken cancellationToken)
    {
        MarketplacePublicationOptions? options = MarketplacePublicationOptions.Load(configuration);
        if (options is null)
            return Results.Problem(statusCode: 503, title: "Local package delivery is not configured.");

        UserActor actor = ToUserActor(http.User);
        await using TenantPostgresTransaction transaction = await BeginAsync(database, actor, cancellationToken);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select bot.id, bot.name, bot.strategy_id, strategy.name,
                   bot.broker_account_id, account.masked_login, account.login_number,
                   account.server, account.binding_fingerprint,
                   coalesce(
                       (
                           select instrument.symbol
                           from bots.broker_symbols as instrument
                           where instrument.tenant_id = bot.tenant_id
                             and instrument.server = account.server
                             and (lower(instrument.symbol) = lower(bot.symbol)
                                  or lower(instrument.symbol) like lower(bot.symbol) || '%')
                           order by
                             instrument.observed_at desc,
                             (lower(instrument.symbol) = lower(bot.symbol)) desc,
                             length(instrument.symbol)
                           limit 1
                       ),
                       bot.symbol),
                   bot.risk_label,
                   strategy.package_strategy_id, strategy.version, strategy.assembly_sha256,
                   strategy.package_sha256, artifact.package_bytes
            from bots.bots as bot
            join catalog.strategies as strategy
              on strategy.tenant_id = bot.tenant_id and strategy.id = bot.strategy_id
            join operations.broker_accounts as account
              on account.tenant_id = bot.tenant_id and account.id = bot.broker_account_id
            join catalog.strategy_artifacts as artifact
              on artifact.tenant_id = strategy.tenant_id
             and artifact.strategy_id = strategy.id
             and artifact.package_sha256 = strategy.package_sha256
            where bot.tenant_id = @tenant and bot.user_id = @user and bot.id = @bot
              and bot.host = 'LOCAL' and account.user_id = bot.user_id
              and account.state <> 'deleted'
              and exists
              (
                  select 1 from marketplace.purchases as purchase
                  where purchase.tenant_id = bot.tenant_id
                    and purchase.buyer_user_id = bot.user_id
                    and purchase.strategy_id = bot.strategy_id
                    and purchase.status = 'paid'
              )
            for update of bot
            """);
        Add(command, "tenant", actor.TenantId);
        Add(command, "user", actor.UserId);
        Add(command, "bot", botId);

        Guid strategyId;
        Guid accountId;
        ulong login;
        string server;
        string packageStrategyId;
        string strategyDisplayName;
        string strategyVersion;
        string assemblySha;
        string packageSha;
        byte[] package;
        object botPayload;
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "The bot is not entitled to a local execution package.");
            }
            strategyId = reader.GetGuid(2);
            strategyDisplayName = reader.GetString(3);
            accountId = reader.GetGuid(4);
            login = checked((ulong)reader.GetInt64(6));
            server = reader.GetString(7);
            packageStrategyId = reader.GetString(11);
            strategyVersion = reader.GetString(12);
            assemblySha = reader.GetString(13);
            packageSha = reader.GetString(14);
            package = (byte[])reader[15];
            botPayload = new
            {
                id = reader.GetGuid(0),
                name = reader.GetString(1),
                strategyId,
                strategyName = strategyDisplayName,
                brokerAccountId = accountId,
                maskedLogin = reader.GetString(5),
                login = login.ToString(System.Globalization.CultureInfo.InvariantCulture),
                server,
                bindingFingerprint = reader.GetString(8),
                symbol = reader.GetString(9),
                riskLabel = reader.GetString(10),
                packageSha256 = packageSha
            };
        }

        byte[] tokenBytes = RandomNumberGenerator.GetBytes(32);
        string token = Base64Url(tokenBytes);
        string tokenSha = Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(token)));
        CryptographicOperations.ZeroMemory(tokenBytes);
        DateTimeOffset issued = DateTimeOffset.UtcNow;
        DateTimeOffset expires = issued.Add(RunLifetime);
        Guid runId = Guid.CreateVersion7();

        using LocalMarketplacePackageKeys keys =
            new LocalMarketplacePackageKeyProvider(options.PackageKeyDocumentFile).Open();
        Guid licenseId = Guid.CreateVersion7();
        StrategyLicenseToken license = LicenseAuthority.IssueLicenseToken(
            new StrategyLicenseClaims(
                licenseId, actor.TenantId, actor.UserId, packageStrategyId,
                Path.GetFileNameWithoutExtension(strategyDisplayName),
                LicenseType.Lifetime, [login], [server], issued, null, 1,
                issued, strategyVersion, assemblySha, keys.SigningKeyId),
            keys.PrivateKeyPem);

        await using (NpgsqlCommand expire = transaction.CreateCommand(
            """
            update operations.local_bot_runs
            set state = 'EXPIRED',
                stopped_at = coalesce(stopped_at, clock_timestamp()),
                updated_at = clock_timestamp()
            where tenant_id = @tenant and bot_id = @bot
              and state in ('ISSUED', 'RUNNING')
            """))
        {
            Add(expire, "tenant", actor.TenantId);
            Add(expire, "bot", botId);
            await expire.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (NpgsqlCommand insert = transaction.CreateCommand(
            """
            insert into operations.local_bot_runs
                (id, tenant_id, user_id, bot_id, broker_account_id, strategy_id,
                 package_sha256, token_sha256, state, issued_at, expires_at)
            values
                (@id, @tenant, @user, @bot, @account, @strategy,
                 @package, @token, 'ISSUED', @issued, @expires)
            """))
        {
            Add(insert, "id", runId);
            Add(insert, "tenant", actor.TenantId);
            Add(insert, "user", actor.UserId);
            Add(insert, "bot", botId);
            Add(insert, "account", accountId);
            Add(insert, "strategy", strategyId);
            insert.Parameters.AddWithValue("package", NpgsqlDbType.Text, packageSha);
            insert.Parameters.AddWithValue("token", NpgsqlDbType.Text, tokenSha);
            insert.Parameters.AddWithValue("issued", NpgsqlDbType.TimestampTz, issued);
            insert.Parameters.AddWithValue("expires", NpgsqlDbType.TimestampTz, expires);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (NpgsqlCommand status = transaction.CreateCommand(
            "update bots.bots set status = 'STARTING', updated_at = clock_timestamp() where tenant_id = @tenant and id = @bot"))
        {
            Add(status, "tenant", actor.TenantId);
            Add(status, "bot", botId);
            await status.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new
        {
            executionId = runId,
            executionToken = token,
            expiresAt = expires,
            bot = botPayload,
            packageBase64 = Convert.ToBase64String(package),
            packageSha256 = packageSha,
            aesKeyBase64 = Convert.ToBase64String(keys.AesKey),
            hmacKeyBase64 = Convert.ToBase64String(keys.HmacKey),
            publicationPublicKeyPem = keys.PublicKeyPem,
            licensePublicKeyPem = keys.PublicKeyPem,
            license
        });
    }

    private static async Task<IResult> ReportLocalStateAsync(
        Guid runId,
        LocalBotRunStateRequest request,
        HttpContext http,
        PostgresDatabase database,
        CancellationToken cancellationToken)
    {
        string state = request.State?.Trim().ToUpperInvariant() ?? string.Empty;
        if (state is not ("RUNNING" or "STOPPED" or "FAULTED") || string.IsNullOrWhiteSpace(request.Token))
            return Results.BadRequest();
        UserActor actor = ToUserActor(http.User);
        string tokenSha = Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(request.Token)));
        await using TenantPostgresTransaction transaction = await BeginAsync(database, actor, cancellationToken);
        Guid? botId;
        await using (NpgsqlCommand update = transaction.CreateCommand(
            """
            update operations.local_bot_runs
            set state = @state,
                last_heartbeat_at = case when @state = 'RUNNING' then clock_timestamp() else last_heartbeat_at end,
                expires_at = case when @state = 'RUNNING' then clock_timestamp() + interval '10 minutes' else expires_at end,
                stopped_at = case when @state in ('STOPPED', 'FAULTED') then clock_timestamp() else stopped_at end,
                updated_at = clock_timestamp()
            where tenant_id = @tenant and user_id = @user and id = @id
              and token_sha256 = @token and expires_at > clock_timestamp()
              and state in ('ISSUED', 'RUNNING')
            returning bot_id
            """))
        {
            update.Parameters.AddWithValue("state", NpgsqlDbType.Text, state);
            Add(update, "tenant", actor.TenantId);
            Add(update, "user", actor.UserId);
            Add(update, "id", runId);
            update.Parameters.AddWithValue("token", NpgsqlDbType.Text, tokenSha);
            botId = (Guid?)await update.ExecuteScalarAsync(cancellationToken);
        }
        if (botId is null)
            return Results.Unauthorized();
        await using (NpgsqlCommand updateBot = transaction.CreateCommand(
            "update bots.bots set status = @state, last_error_message = @error, updated_at = clock_timestamp() where tenant_id = @tenant and user_id = @user and id = @bot"))
        {
            updateBot.Parameters.AddWithValue("state", NpgsqlDbType.Text, state);
            string? error = string.IsNullOrWhiteSpace(request.Error)
                ? null
                : request.Error.Trim()[..Math.Min(request.Error.Trim().Length, 500)];
            updateBot.Parameters.AddWithValue("error", NpgsqlDbType.Text, (object?)error ?? DBNull.Value);
            Add(updateBot, "tenant", actor.TenantId);
            Add(updateBot, "user", actor.UserId);
            Add(updateBot, "bot", botId.Value);
            await updateBot.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    private static ValueTask<TenantPostgresTransaction> BeginAsync(
        PostgresDatabase database,
        UserActor actor,
        CancellationToken cancellationToken) => database.BeginTenantTransactionAsync(
            new TenantExecutionContext(actor.TenantId, actor.UserId, Guid.CreateVersion7(), actor.SessionId),
            cancellationToken);

    private static UserActor ToUserActor(ClaimsPrincipal principal)
    {
        string assuranceValue = principal.FindFirstValue("assurance") ?? "password";
        YO4X.Identity.AuthenticationAssurance assurance = assuranceValue.ToLowerInvariant() switch
        {
            "hardware_key" => YO4X.Identity.AuthenticationAssurance.HardwareKey,
            "webauthn" => YO4X.Identity.AuthenticationAssurance.WebAuthn,
            "totp" => YO4X.Identity.AuthenticationAssurance.Totp,
            _ => YO4X.Identity.AuthenticationAssurance.Password
        };

        return new UserActor(
            ClaimReader.RequiredGuid(principal, "tenant_id"),
            ClaimReader.RequiredGuid(principal, "sub"),
            ClaimReader.RequiredGuid(principal, "session_id"),
            assurance);
    }

    private static void Add(NpgsqlCommand command, string name, Guid value) =>
        command.Parameters.AddWithValue(name, NpgsqlDbType.Uuid, value);

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
