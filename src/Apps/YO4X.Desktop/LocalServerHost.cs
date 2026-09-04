#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using YO4X.StrategyGovernance.Licensing;
using YO4X.StrategyGovernance.Packaging;

namespace YO4X.Desktop;

public sealed class LocalServerHost
{
    private IHost? host;
    private readonly int port;
    private readonly string rootDirectory;

    public int Port => port;
    public string BaseUrl => $"http://127.0.0.1:{port}";

    public LocalServerHost(string rootDirectory, int preferredPort = 4173)
    {
        this.rootDirectory = rootDirectory;
        this.port = IsPortAvailable(preferredPort) ? preferredPort : GetRandomAvailablePort();
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = rootDirectory,
            WebRootPath = Path.Combine(rootDirectory, "wwwroot")
        });

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, port);
        });

        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
        });

        var app = builder.Build();

        app.UseCors();

        // Serve pre-built React frontend assets
        string wwwroot = Path.Combine(rootDirectory, "wwwroot");
        if (Directory.Exists(wwwroot))
        {
            app.UseDefaultFiles();
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(wwwroot),
                RequestPath = ""
            });
        }

        // Map Embedded Local REST Endpoints
        MapApiEndpoints(app);

        // Client-side SPA routing fallback
        if (Directory.Exists(wwwroot) && File.Exists(Path.Combine(wwwroot, "index.html")))
        {
            app.MapFallbackToFile("index.html", new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(wwwroot)
            });
        }

        this.host = app;
        await app.StartAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await DesktopLiveBotHost.Instance.StopAllAsync().ConfigureAwait(false);
        }
        catch
        {
        }

        if (host != null)
        {
            await host.StopAsync(cancellationToken);
            host.Dispose();
            host = null;
        }
    }

    private static void MapApiEndpoints(WebApplication app)
    {
        // 1. Health View
        app.MapGet("/health", () => Results.Ok(new
        {
            status = "HEALTHY"
        }));

        // 2. Authentication Endpoints
        app.MapPost("/v1/auth/login", async (HttpContext ctx) =>
        {
            using var reader = new StreamReader(ctx.Request.Body);
            string body = await reader.ReadToEndAsync();
            string email = "user@gmail.com";
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("email", out var eProp))
                {
                    string? str = eProp.GetString();
                    if (!string.IsNullOrWhiteSpace(str)) email = str.Trim();
                }
            }
            catch { }

            LocalTradingEngine.Instance.SetCurrentUser(email);
            return Results.Ok(new { success = true, email });
        });

        app.MapPost("/v1/auth/logout", () =>
        {
            LocalTradingEngine.Instance.SetCurrentUser(null);
            return Results.Ok(new { success = true });
        });

        // 3. User View (Contract compliant)
        app.MapGet("/v1/me", () =>
        {
            var user = LocalTradingEngine.Instance.GetCurrentUser();
            if (user == null)
            {
                return Results.Unauthorized();
            }
            return Results.Ok(user);
        });

        // 3. Dashboard Summary (Contract compliant)
        app.MapGet("/v1/dashboard/summary", () =>
        {
            var live = LocalTradingEngine.Instance.GetLiveAccountState();
            var bots = LocalTradingEngine.Instance.GetBots().ToList();
            var runningBots = bots.Where(b => b.Status == "RUNNING").Select(b => FormatBotView(b)).ToList();
            var (workingSetMb, _, _) = LocalTradingEngine.GetProcessMemoryStats();

            string equityVal = $"${live.Equity.ToString("N2", CultureInfo.InvariantCulture)}";
            string equityDelta = live.FloatingPnL >= 0 ? $"+${live.FloatingPnL.ToString("N2", CultureInfo.InvariantCulture)}" : $"-${Math.Abs(live.FloatingPnL).ToString("N2", CultureInfo.InvariantCulture)}";
            string equityDir = live.FloatingPnL > 0 ? "UP" : live.FloatingPnL < 0 ? "DOWN" : "FLAT";

            string balanceVal = $"${live.Balance.ToString("N2", CultureInfo.InvariantCulture)}";

            string floatingVal = live.FloatingPnL >= 0 ? $"+${live.FloatingPnL.ToString("N2", CultureInfo.InvariantCulture)}" : $"-${Math.Abs(live.FloatingPnL).ToString("N2", CultureInfo.InvariantCulture)}";
            double pnlPercent = live.Balance > 0 ? (live.FloatingPnL / live.Balance) * 100.0 : 0.0;
            string floatingDelta = live.FloatingPnL >= 0 ? $"+{pnlPercent.ToString("F2", CultureInfo.InvariantCulture)}%" : $"{pnlPercent.ToString("F2", CultureInfo.InvariantCulture)}%";
            string floatingDir = live.FloatingPnL > 0 ? "UP" : live.FloatingPnL < 0 ? "DOWN" : "FLAT";

            string ramVal = $"{workingSetMb.ToString("F1", CultureInfo.InvariantCulture)} MB";
            string ramDelta = runningBots.Count > 0 ? $"{runningBots.Count} Active Bot{(runningBots.Count > 1 ? "s" : "")}" : "Host CPU";

            var stats = new[]
            {
                new { id = "equity", label = "Account Equity", value = equityVal, delta = equityDelta, direction = equityDir },
                new { id = "balance", label = "Balance", value = balanceVal, delta = "$0.00", direction = "FLAT" },
                new { id = "floating", label = "Floating PnL", value = floatingVal, delta = floatingDelta, direction = floatingDir },
                new { id = "ram", label = "Local RAM Usage", value = ramVal, delta = ramDelta, direction = "FLAT" }
            };

            return Results.Ok(new
            {
                stats,
                runningBots,
                liveBotCount = runningBots.Count,
                cloudRunnerCount = 0
            });
        });

        // 4. Strategy Catalog (Contract compliant)
        var getCatalog = (HttpContext ctx) =>
        {
            int page = int.TryParse(ctx.Request.Query["page"], out int p) && p > 0 ? p : 1;
            int pageSize = int.TryParse(ctx.Request.Query["pageSize"], out int ps) && ps > 0 ? Math.Clamp(ps, 1, 200) : 50;
            string? cat = ctx.Request.Query["category"];
            string? sym = ctx.Request.Query["symbol"];
            string? q = ctx.Request.Query["query"];

            var allStrategies = LocalTradingEngine.Instance.GetStrategies().ToList();

            var allItems = allStrategies.Select(s => new
            {
                id = s.Id,
                slug = s.Slug,
                name = s.Name,
                authorName = s.AuthorName,
                authorInitials = string.IsNullOrWhiteSpace(s.AuthorInitials) ? "YO" : s.AuthorInitials.Trim(),
                category = string.IsNullOrWhiteSpace(s.Category) ? "Proprietary Algorithm" : s.Category.Trim(),
                symbol = string.IsNullOrWhiteSpace(s.Symbol) ? "XAUUSDm" : s.Symbol.Trim(),
                timeframe = string.IsNullOrWhiteSpace(s.Timeframe) ? "M1" : s.Timeframe.Trim(),
                version = string.IsNullOrWhiteSpace(s.Version) ? "1.0.0" : s.Version.Trim(),
                ratingAverage = Math.Clamp(s.RatingAverage, 0.0, 5.0),
                ratingCount = Math.Max(0, s.RatingCount),
                activeUsers = Math.Max(0, s.ActiveUsers),
                isFree = s.IsFree,
                cloudPriceMonthlyCents = Math.Max(0, s.CloudPriceMonthlyCents),
                cloudPriceYearlyCents = Math.Max(0, s.CloudPriceYearlyCents),
                currency = "USD",
                updatedAt = s.UpdatedAt.ToString("O")
            }).DistinctBy(x => x.id).ToList();

            var categories = allItems.Select(i => i.category).Distinct().ToList();
            if (categories.Count == 0) categories.Add("Proprietary Algorithm");

            var symbols = allItems.Select(i => i.symbol).Distinct().ToList();
            if (symbols.Count == 0) symbols.Add("XAUUSDm");

            var filtered = allItems.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(cat))
                filtered = filtered.Where(x => string.Equals(x.category, cat.Trim(), StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(sym))
                filtered = filtered.Where(x => string.Equals(x.symbol, sym.Trim(), StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(q))
                filtered = filtered.Where(x => x.name.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase));

            var filteredList = filtered
                .OrderByDescending(x => x.name.Contains("Private EA", StringComparison.OrdinalIgnoreCase)
                    || x.name.Contains("Straddle", StringComparison.OrdinalIgnoreCase)
                    || x.name.Contains("Bambibabo", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(x => x.activeUsers)
                .ThenBy(x => x.name)
                .ToList();
            int totalCount = filteredList.Count;
            int totalPages = totalCount > 0 ? (int)Math.Ceiling(totalCount / (double)pageSize) : 1;

            var pagedItems = filteredList
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return Results.Ok(new
            {
                page,
                pageSize,
                totalCount,
                totalPages,
                categories,
                symbols,
                items = pagedItems
            });
        };

        app.MapGet("/v1/catalog/strategies", getCatalog);
        app.MapGet("/v1/strategies", getCatalog);

        app.MapGet("/v1/catalog/strategies/{id}", (string id) =>
        {
            var s = LocalTradingEngine.Instance.GetStrategies().FirstOrDefault(x => x.Id == id || x.Slug == id);
            if (s == null) return Results.NotFound();

            return Results.Ok(new
            {
                item = new
                {
                    id = s.Id,
                    slug = s.Slug,
                    name = s.Name,
                    authorName = s.AuthorName,
                    authorInitials = string.IsNullOrWhiteSpace(s.AuthorInitials) ? "YO" : s.AuthorInitials.Trim(),
                    category = string.IsNullOrWhiteSpace(s.Category) ? "Proprietary Algorithm" : s.Category.Trim(),
                    symbol = string.IsNullOrWhiteSpace(s.Symbol) ? "XAUUSDm" : s.Symbol.Trim(),
                    timeframe = string.IsNullOrWhiteSpace(s.Timeframe) ? "M1" : s.Timeframe.Trim(),
                    version = string.IsNullOrWhiteSpace(s.Version) ? "1.0.0" : s.Version.Trim(),
                    ratingAverage = Math.Clamp(s.RatingAverage, 0.0, 5.0),
                    ratingCount = Math.Max(0, s.RatingCount),
                    activeUsers = Math.Max(0, s.ActiveUsers),
                    isFree = s.IsFree,
                    cloudPriceMonthlyCents = Math.Max(0, s.CloudPriceMonthlyCents),
                    cloudPriceYearlyCents = Math.Max(0, s.CloudPriceYearlyCents),
                    currency = "USD",
                    updatedAt = s.UpdatedAt.ToString("O")
                },
                author = new
                {
                    name = s.AuthorName,
                    initials = string.IsNullOrWhiteSpace(s.AuthorInitials) ? "YO" : s.AuthorInitials.Trim(),
                    strategyCount = 3,
                    ratingAverage = 4.9
                },
                summary = s.Description,
                description = s.Description,
                performance = new[]
                {
                    new { ordinal = 0, label = "Win Rate", value = "78.4%" },
                    new { ordinal = 1, label = "Profit Factor", value = "2.14" },
                    new { ordinal = 2, label = "Max Drawdown", value = "4.2%" }
                },
                equityCurve = new[]
                {
                    new { ordinal = 0, periodLabel = "Start", equity = 10000.0 },
                    new { ordinal = 1, periodLabel = "Current", equity = 10034.2 }
                },
                reviewCount = 12
            });
        });

        app.MapGet("/v1/catalog/strategies/{id}/inputs", (string id) =>
        {
            var s = LocalTradingEngine.Instance.GetStrategies().FirstOrDefault(x => x.Id == id || x.Slug == id);
            if (s == null) return Results.NotFound();

            var inputsList = LocalTradingEngine.FormatDeclaredInputs(s.Inputs);

            return Results.Ok(new
            {
                strategyId = s.Id,
                strategyName = s.Name,
                inputs = inputsList
            });
        });

        app.MapGet("/v1/catalog/strategies/{id}/reviews", (string id) =>
        {
            return Results.Ok(Array.Empty<object>());
        });

        // 5. Broker Accounts (Contract compliant)
        app.MapGet("/v1/broker-accounts", () =>
        {
            var accounts = LocalTradingEngine.Instance.GetAccounts()
                .OrderByDescending(a => a.MaskedLogin.Contains("4289"))
                .Select(a => FormatAccountView(a))
                .ToList();
            return Results.Ok(accounts);
        });

        app.MapGet("/v1/broker-accounts/{id}", (string id) =>
        {
            var acc = LocalTradingEngine.Instance.GetAccounts().FirstOrDefault(a => a.Id == id);
            if (acc == null) return Results.NotFound();
            return Results.Ok(FormatAccountView(acc));
        });

        app.MapGet("/v1/broker-accounts/{id}/credential-state", (string id) =>
        {
            var acc = LocalTradingEngine.Instance.GetAccounts().FirstOrDefault(a => a.Id == id);
            string masked = acc?.MaskedLogin ?? "****4289";
            return Results.Ok(new
            {
                exists = true,
                state = "READY",
                lastAuthorizedWorkerUse = DateTimeOffset.UtcNow.ToString("O"),
                maskedAccountBinding = masked
            });
        });

        app.MapPost("/v1/broker-accounts/{id}/cloud-connection-tests", (string id) =>
        {
            string cmdId = Guid.NewGuid().ToString();
            return Results.Accepted($"/v1/operations/{cmdId}", new
            {
                commandId = cmdId,
                statusUrl = $"/v1/operations/{cmdId}",
                submittedAggregateVersion = 1,
                correlationId = Guid.NewGuid().ToString()
            });
        });

        app.MapGet("/v1/operations/{id}", (string id) =>
        {
            return Results.Ok(new
            {
                id,
                status = "COMPLETED",
                progressPercent = 100,
                errorMessage = (string?)null,
                updatedAt = DateTimeOffset.UtcNow.ToString("O")
            });
        });

        app.MapGet("/v1/broker-accounts/registration-options", () =>
        {
            return Results.Ok(new[]
            {
                new { id = "019c8d27-763d-7000-8000-000000000011", name = "Exness (MT5 Direct)", server = "Exness-MT5Trial7", isSupported = true },
                new { id = "019c8d27-763d-7000-8000-000000000012", name = "Vantage Markets (MT5)", server = "VantageInternational-Live", isSupported = true },
                new { id = "019c8d27-763d-7000-8000-000000000013", name = "MetaQuotes Software", server = "MetaQuotes-Demo", isSupported = true }
            });
        });

        app.MapGet("/v1/bridge/status", () =>
        {
            var live = LocalTradingEngine.Instance.GetLiveAccountState();
            return Results.Ok(new
            {
                connected = true,
                version = "5.00 (build 4450)",
                roundTripMs = 1.2,
                ordersToday = live.OpenTradesCount,
                rejections = 0
            });
        });

        app.MapGet("/v1/broker-symbols", () =>
        {
            return Results.Ok(new[]
            {
                new
                {
                    symbol = "XAUUSDm",
                    description = "Gold vs US Dollar (Mini)",
                    digits = 2,
                    contractSize = 100.0,
                    currency = "USD",
                    volumeMin = 0.01,
                    volumeMax = 100.0,
                    volumeStep = 0.01
                },
                new
                {
                    symbol = "EURUSD",
                    description = "Euro vs US Dollar",
                    digits = 5,
                    contractSize = 100000.0,
                    currency = "USD",
                    volumeMin = 0.01,
                    volumeMax = 100.0,
                    volumeStep = 0.01
                }
            });
        });

        app.MapPost("/v1/broker-accounts", async (HttpContext ctx) =>
        {
            using var reader = new StreamReader(ctx.Request.Body);
            string body = await reader.ReadToEndAsync();
            var model = JsonSerializer.Deserialize<JsonElement>(body);
            var saved = LocalTradingEngine.Instance.SaveAccount(model);
            return Results.Ok(FormatAccountView(saved));
        });

        // 6. Bots (Contract compliant)
        app.MapGet("/v1/bots", () =>
        {
            var bots = LocalTradingEngine.Instance.GetBots().Select(b => FormatBotView(b)).ToList();
            return Results.Ok(bots);
        });

        app.MapGet("/v1/bots/{id}", (string id) =>
        {
            var bot = LocalTradingEngine.Instance.GetBot(id);
            if (bot == null) return Results.NotFound();
            return Results.Ok(FormatBotView(bot));
        });

        app.MapPost("/v1/bots", async (HttpContext ctx) =>
        {
            using var reader = new StreamReader(ctx.Request.Body);
            string body = await reader.ReadToEndAsync();
            var model = JsonSerializer.Deserialize<JsonElement>(body);
            var bot = LocalTradingEngine.Instance.CreateBot(model);
            return Results.Ok(FormatBotView(bot));
        });

        app.MapPost("/v1/bots/{id}/status", async (string id, HttpContext ctx) =>
        {
            using var reader = new StreamReader(ctx.Request.Body);
            string body = await reader.ReadToEndAsync();
            string status = "STOPPED";
            try
            {
                var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("status", out var sProp))
                {
                    status = sProp.GetString() ?? "STOPPED";
                }
            }
            catch { }

            try
            {
                var updated = await LocalTradingEngine.Instance
                    .ApplyBotStatusAsync(id, status, ctx.RequestAborted)
                    .ConfigureAwait(false);
                if (updated == null) return Results.NotFound();
                return Results.Ok(FormatBotView(updated));
            }
            catch (Exception exception) when (exception is InvalidOperationException or TimeoutException or InvalidDataException)
            {
                string message = exception.Message.Replace('\r', ' ').Replace('\n', ' ').Trim();
                if (message.Length == 0)
                {
                    message = "The bot could not change status.";
                }

                if (message.Length > 500)
                {
                    message = message[..500];
                }

                return Results.Problem(statusCode: 422, title: message, detail: message);
            }
        });

        app.MapGet("/v1/bots/{id}/settings", (string id) =>
        {
            var settings = LocalTradingEngine.Instance.GetBotSettings(id);
            if (settings == null) return Results.NotFound();
            return Results.Ok(settings);
        });

        app.MapPut("/v1/bots/{id}/settings", async (string id, HttpContext ctx) =>
        {
            using var reader = new StreamReader(ctx.Request.Body);
            string body = await reader.ReadToEndAsync();
            try
            {
                var updated = LocalTradingEngine.Instance.UpdateBotSettings(id, body);
                if (updated == null) return Results.NotFound();
                return Results.NoContent();
            }
            catch (Exception exception) when (exception is InvalidOperationException or JsonException)
            {
                string message = exception.Message.Replace('\r', ' ').Replace('\n', ' ').Trim();
                if (message.Length == 0) message = "The bot settings could not be saved.";
                if (message.Length > 500) message = message[..500];
                return Results.Problem(statusCode: 422, title: message, detail: message);
            }
        });

        app.MapGet("/v1/bots/uptime", (HttpContext ctx) =>
        {
            int days = 28;
            if (ctx.Request.Query.TryGetValue("days", out var dVal) && int.TryParse(dVal, out int dParsed))
            {
                days = dParsed;
            }
            var uptime = LocalTradingEngine.GetBotUptime(days);
            return Results.Ok(uptime);
        });

        app.MapPost("/v1/bots/{id}/start", async (string id, HttpContext ctx) =>
        {
            var updated = await LocalTradingEngine.Instance
                .ApplyBotStatusAsync(id, "RUNNING", ctx.RequestAborted)
                .ConfigureAwait(false);
            return Results.Ok(new { botId = id, running = updated?.Status == "RUNNING" });
        });

        app.MapPost("/v1/bots/{id}/stop", async (string id, HttpContext ctx) =>
        {
            var updated = await LocalTradingEngine.Instance
                .ApplyBotStatusAsync(id, "STOPPED", ctx.RequestAborted)
                .ConfigureAwait(false);
            return Results.Ok(new { botId = id, running = false, status = updated?.Status });
        });

        // 7. Telemetry & Real-Time Monitoring
        app.MapGet("/v1/telemetry", () =>
        {
            var telemetry = LocalTradingEngine.Instance.GetTelemetry();
            return Results.Ok(telemetry);
        });

        // 8. Trade Journal (Executed Orders & Live Placements)
        app.MapGet("/v1/journal", (HttpContext ctx) =>
        {
            int limit = 50;
            if (ctx.Request.Query.TryGetValue("limit", out var lVal) && int.TryParse(lVal, out int lParsed))
            {
                limit = Math.Clamp(lParsed, 1, 500);
            }

            string? botId = ctx.Request.Query["botId"];
            string? fromStr = ctx.Request.Query["from"];
            string? toStr = ctx.Request.Query["to"];

            var trades = LocalTradingEngine.Instance.GetJournalTrades().ToList();
            if (!string.IsNullOrWhiteSpace(botId))
            {
                trades = trades.Where(t => t.BotId == botId).ToList();
            }
            if (!string.IsNullOrWhiteSpace(fromStr) && DateTimeOffset.TryParse(fromStr, out var fromDate))
            {
                trades = trades.Where(t => t.OpenedAt >= fromDate).ToList();
            }
            if (!string.IsNullOrWhiteSpace(toStr) && DateTimeOffset.TryParse(toStr, out var toDate))
            {
                trades = trades.Where(t => t.OpenedAt <= toDate).ToList();
            }

            var paged = trades.OrderByDescending(t => t.OpenedAt).Take(limit).Select(t => new
            {
                id = t.Id,
                botId = t.BotId,
                botName = t.BotName,
                symbol = t.Symbol,
                side = t.Side,
                volume = t.Volume,
                entryPrice = t.EntryPrice,
                exitPrice = t.ExitPrice,
                resultAmount = t.ResultAmount,
                currency = t.Currency,
                openedAt = t.OpenedAt.ToString("O"),
                closedAt = t.ClosedAt?.ToString("O")
            }).ToList();

            return Results.Ok(new
            {
                items = paged,
                nextCursor = (string?)null
            });
        });

        // 8. Admin Portal Endpoints
        app.MapGet("/v1/admin/overview", () =>
        {
            var overview = LocalTradingEngine.Instance.GetAdminOverview();
            return Results.Ok(overview);
        });

        app.MapPost("/v1/admin/strategies/upload-mq5", async (HttpContext ctx) =>
        {
            try
            {
                using var reader = new StreamReader(ctx.Request.Body);
                string body = await reader.ReadToEndAsync();
                var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                string name = root.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? "Custom EA" : "Custom EA";
                string mq5Source = root.TryGetProperty("mq5Source", out var sProp) ? sProp.GetString() ?? "" : "";
                string symbol = root.TryGetProperty("symbol", out var symProp) ? symProp.GetString() ?? "XAUUSDm" : "XAUUSDm";
                string timeframe = root.TryGetProperty("timeframe", out var tfProp) ? tfProp.GetString() ?? "M1" : "M1";
                string version = root.TryGetProperty("version", out var vProp) ? vProp.GetString() ?? "1.0.0" : "1.0.0";
                string category = root.TryGetProperty("category", out var cProp) ? cProp.GetString() ?? "Proprietary Algorithm" : "Proprietary Algorithm";
                string author = root.TryGetProperty("author", out var aProp) ? aProp.GetString() ?? "YO4X Admin" : "YO4X Admin";
                string description = root.TryGetProperty("description", out var dProp) ? dProp.GetString() ?? "" : "";

                if (string.IsNullOrWhiteSpace(mq5Source))
                {
                    return Results.BadRequest(new { error = "MQL5 source text cannot be empty." });
                }

                var published = LocalTradingEngine.Instance.CompileAndPublishMq5(name, mq5Source, symbol, timeframe, version, category, author, description);
                return Results.Ok(new
                {
                    success = true,
                    strategy = published,
                    message = $"Successfully compiled '{published.Name}' into encrypted .yo4x DRM container."
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = $"MQL5 compilation error: {ex.Message}" });
            }
        });

        app.MapPost("/v1/admin/strategies/{id}/delete", (string id) =>
        {
            bool removed = LocalTradingEngine.Instance.RemoveStrategy(id);
            return Results.Ok(new { success = removed, id });
        });

        app.MapDelete("/v1/admin/strategies/{id}", (string id) =>
        {
            bool removed = LocalTradingEngine.Instance.RemoveStrategy(id);
            return Results.Ok(new { success = removed, id });
        });
    }

    private static object FormatAccountView(DesktopAccountInfo acc)
    {
        return new
        {
            id = acc.Id,
            brokerId = acc.BrokerId,
            server = acc.Server,
            maskedLogin = acc.MaskedLogin,
            environment = acc.Environment,
            accountMode = acc.AccountMode,
            capabilityState = acc.CapabilityState,
            version = acc.Version,
            updatedAt = acc.UpdatedAt.ToString("O")
        };
    }

    private static object FormatBotView(DesktopBotInstance b)
    {
        return new
        {
            id = b.Id,
            name = b.Name.Trim(),
            strategyId = b.StrategyId,
            strategyName = b.StrategyName.Trim(),
            brokerAccountId = b.BrokerAccountId,
            maskedLogin = b.MaskedLogin,
            symbol = b.Symbol.Trim(),
            riskLabel = b.RiskLabel.Trim(),
            status = b.Status.Trim().ToUpperInvariant(),
            host = b.Host.Trim().ToUpperInvariant(),
            lastErrorCode = b.LastErrorCode,
            lastErrorMessage = b.LastErrorMessage,
            metrics = (b.Metrics ?? new List<DesktopBotMetric>()).Select(m => new
            {
                window = m.Window,
                plAmount = m.PlAmount,
                currency = m.Currency,
                tradeCount = m.TradeCount
            }).ToList(),
            createdAt = b.CreatedAt.ToString("O"),
            updatedAt = b.UpdatedAt.ToString("O")
        };
    }

    private static bool IsPortAvailable(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int GetRandomAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
