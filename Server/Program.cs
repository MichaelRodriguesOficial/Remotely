using Microsoft.OpenApi.Models;
using Remotely.Server.Extensions;
using Bitbound.SimpleMessenger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Remotely.Server.Auth;
using Remotely.Server.Components.Account;
using Remotely.Server.Data;
using Remotely.Server.Hubs;
using Remotely.Server.Models;
using Remotely.Server.Options;
using Remotely.Server.Services;
using Remotely.Server.Services.Stores;
using Remotely.Shared.Entities;
using Remotely.Shared.Services;
using Remotely.Shared.Utilities;
using Serilog;
using System.Net;
using System.Threading.RateLimiting;
using RatePolicyNames = Remotely.Server.RateLimiting.PolicyNames;
using Remotely.Server.Filters;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;
var services = builder.Services;

configuration.AddEnvironmentVariables("Remotely_");

services.Configure<ApplicationOptions>(
    configuration.GetSection(ApplicationOptions.SectionKey));

var appOptions = configuration
    .GetSection(ApplicationOptions.SectionKey)
    .Get<ApplicationOptions>();

services
    .AddRazorComponents()
    .AddInteractiveServerComponents();

services.AddRazorPages();
// Registra o ApiExplorer dos controllers, usado pelo Swagger.
services.AddControllers();

services.AddCascadingAuthenticationState();
services.AddScoped<IdentityUserAccessor>();
services.AddScoped<IdentityRedirectManager>();
services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

var dbProvider = appOptions?.DbProvider?.ToLower();

switch (dbProvider)
{
    case "sqlite":
        services.AddDbContext<AppDb, SqliteDbContext>(
            contextLifetime: ServiceLifetime.Transient,
            optionsLifetime: ServiceLifetime.Transient);
        break;

    case "sqlserver":
        services.AddDbContext<AppDb, SqlServerDbContext>(
            contextLifetime: ServiceLifetime.Transient,
            optionsLifetime: ServiceLifetime.Transient);
        break;

    case "postgresql":
        services.AddDbContext<AppDb, PostgreSqlDbContext>(
            contextLifetime: ServiceLifetime.Transient,
            optionsLifetime: ServiceLifetime.Transient);
        break;

    default:
        throw new InvalidOperationException(
            $"Invalid DBProvider: {dbProvider}.  Ensure a valid value " +
            $"is set in appsettings.json or environment variables.");
}

using AppDb appDb = dbProvider switch
{
    "sqlite" => new SqliteDbContext(builder.Configuration, builder.Environment),
    "sqlserver" => new SqlServerDbContext(builder.Configuration, builder.Environment),
    "postgresql" => new PostgreSqlDbContext(builder.Configuration, builder.Environment),
    _ => throw new InvalidOperationException($"Invalid DBProvider: {dbProvider}")
};

await appDb.Database.MigrateAsync();
var settings = await appDb.GetAppSettings();

ConfigureSerilog(builder, settings);

builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));

if (OperatingSystem.IsWindows() && settings.EnableWindowsEventLog)
{
    builder.Logging.AddEventLog();
}

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = IdentityConstants.ApplicationScheme;
    options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
})
    .AddIdentityCookies();

services.AddIdentityCore<RemotelyUser>(options =>
{
    options.Stores.MaxLengthForKeys = 128;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 8;

    // Protect against brute-force/password-spraying attacks.
    options.Lockout.AllowedForNewUsers = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
})
    .AddEntityFrameworkStores<AppDb>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

services.AddScoped<IAuthorizationHandler, TwoFactorRequiredHandler>();
services.AddScoped<IAuthorizationHandler, OrganizationAdminRequirementHandler>();
services.AddScoped<IAuthorizationHandler, ServerAdminRequirementHandler>();
services.AddSingleton<IEmailSender<RemotelyUser>, IdentityNoOpEmailSender>();

services.AddAuthorization(options =>
{
    options.AddPolicy(PolicyNames.TwoFactorRequired, builder =>
    {
        builder.Requirements.Add(new TwoFactorRequiredRequirement());
    });

    options.AddPolicy(PolicyNames.OrganizationAdminRequired, builder =>
    {
        builder.Requirements.Add(new OrganizationAdminRequirement());
    });

    options.AddPolicy(PolicyNames.ServerAdminRequired, builder =>
    {
        builder.Requirements.Add(new ServerAdminRequirement());
    });
});

services.AddDatabaseDeveloperPageExceptionFilter();

if (settings.UseHttpLogging)
{
    services.AddHttpLogging(options =>
    {
        options.RequestHeaders.Add("X-Forwarded-For");
        options.RequestHeaders.Add("X-Forwarded-Proto");
        options.RequestHeaders.Add("X-Forwarded-Host");
        options.RequestHeaders.Add("X-Original-For");
        options.RequestHeaders.Add("X-Original-Proto");
        options.RequestHeaders.Add("X-Original-Host");
        options.RequestHeaders.Add("Host");
    });
}

services.AddCors(options =>
{
    if (settings.TrustedCorsOrigins is { Count: > 0 } trustedOrigins)
    {
        options.AddPolicy("TrustedOriginPolicy", builder => builder
            .WithOrigins(trustedOrigins.ToArray())
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
        );
    }
});

services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.All;
    options.ForwardLimit = null;

    // Default Docker host. We want to allow forwarded headers from this address.
    if (IPAddress.TryParse(appOptions?.DockerGatewayIp, out var dockerGatewayIp))
    {
        options.KnownProxies.Add(dockerGatewayIp);
    }

    if (settings.KnownProxies is { Count: > 0 } knownProxies)
    {
        foreach (var proxy in knownProxies)
        {
            if (IPAddress.TryParse(proxy, out var ip))
            {
                options.KnownProxies.Add(ip);
            }
        }
    }
});

services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.MaximumReceiveMessageSize = 100_000;
})
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.PropertyNameCaseInsensitive = true;
    })
    .AddMessagePackProtocol();

// Persist Data Protection keys (used for auth cookies, antiforgery tokens, etc.)
// in AppData so they survive container restarts and upgrades.  Without this,
// every restart would generate new keys and sign out all users.
var dataProtectionKeysDir = Path.Combine(builder.Environment.ContentRootPath, "AppData", "DataProtectionKeys");
Directory.CreateDirectory(dataProtectionKeysDir);
services
    .AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysDir));

services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    var authPermitLimit = appOptions is { AuthRateLimitPerMinute: > 0 } ?
        appOptions.AuthRateLimitPerMinute :
        20;

    // Throttle authentication endpoints per client IP to slow down
    // brute-force and credential-stuffing attempts.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        if (!IsAuthenticationRequest(httpContext.Request))
        {
            return RateLimitPartition.GetNoLimiter("unlimited");
        }

        var clientIp = $"{httpContext.Connection.RemoteIpAddress}";
        return RateLimitPartition.GetFixedWindowLimiter($"auth:{clientIp}", _ => new FixedWindowRateLimiterOptions()
        {
            PermitLimit = authPermitLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });

    options.AddConcurrencyLimiter(RatePolicyNames.AgentUpdateDownloads, clOptions =>
    {
        clOptions.QueueLimit = int.MaxValue;

        clOptions.PermitLimit =
            settings.MaxConcurrentUpdates <= 0 ?
                10 :
                settings.MaxConcurrentUpdates;
    });
});
services.AddHttpClient();
services.AddLogging();
services.AddScoped<IEmailSender, EmailSender>();
if (builder.Environment.IsDevelopment())
{
    services.AddScoped<IEmailSenderEx, EmailSenderFake>();
}
else
{
    services.AddScoped<IEmailSender<RemotelyUser>, EmailSenderEx>();
    services.AddScoped<IEmailSenderEx, EmailSenderEx>();
}
services.AddSingleton<IAppDbFactory, AppDbFactory>();
services.AddSingleton<IAccessProfileService, AccessProfileService>();
services.AddTransient<IDataService, DataService>();
services.AddScoped<ApiAuthorizationFilter>();
services.AddScoped<LocalOnlyFilter>();
services.AddScoped<ExpiringTokenFilter>();
services.AddHostedService<DataCleanupService>();
services.AddHostedService<ScriptScheduler>();
services.AddSingleton<IUpgradeService, UpgradeService>();
services.AddScoped<IToastService, ToastService>();
services.AddScoped<IModalService, ModalService>();
services.AddScoped<IJsInterop, JsInterop>();
services.AddScoped<ICircuitConnection, CircuitConnection>();
services.AddScoped<ILoaderService, LoaderService>();
services.AddScoped(x => (CircuitHandler)x.GetRequiredService<ICircuitConnection>());
services.AddSingleton<ICircuitManager, CircuitManager>();
services.AddScoped<IAuthService, AuthService>();
services.AddScoped<ISelectedCardsStore, SelectedCardsStore>();
services.AddScoped<IExpiringTokenService, ExpiringTokenService>();
services.AddScoped<IScriptScheduleDispatcher, ScriptScheduleDispatcher>();
services.AddSingleton<IOtpProvider, OtpProvider>();
services.AddSingleton<IEmbeddedServerDataProvider, EmbeddedServerDataProvider>();
services.AddSingleton<ILogsManager, LogsManager>();
services.AddScoped<IThemeProvider, ThemeProvider>();
services.AddScoped<IChatSessionStore, ChatSessionStore>();
services.AddScoped<ITerminalStore, TerminalStore>();
services.AddScoped<ViewerAuthorizationFilter>();
services.AddSingleton(WeakReferenceMessenger.Default);
services.AddSingleton<ISessionRecordingSink, SessionRecordingSink>();
services.AddSingleton<IDesktopStreamCache, DesktopStreamCache>();
services.AddSingleton<IRemoteControlSessionCache, RemoteControlSessionCache>();
services.AddSingleton<ISystemTime, SystemTime>();
services.AddSingleton<IAgentHubSessionCache, AgentHubSessionCache>();
services.AddHostedService<RemoteControlSessionCleaner>();
services.AddHostedService<RemoteControlSessionReconnector>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Remotely API",
        Version = AppVersionHelper.GetAppVersion(),
        Description =
            "API do Remotely. Autentique com uma chave criada em **Chaves de API** no painel, " +
            "enviando o cabeçalho `X-Api-Key: {id-da-chave}:{segredo}`. " +
            "Chamadas feitas a partir desta página com a sessão do navegador também são aceitas.",
        Contact = new OpenApiContact
        {
            Name = "Michael Rodrigues",
            Url = new Uri("https://github.com/MichaelRodriguesOficial/Remotely")
        },
        License = new OpenApiLicense
        {
            Name = "GPL-3.0",
            Url = new Uri("https://github.com/MichaelRodriguesOficial/Remotely/blob/master/LICENSE")
        }
    });

    options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = "X-Api-Key",
        Description = "Chave de API no formato {id-da-chave}:{segredo}."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "ApiKey"
                }
            },
            Array.Empty<string>()
        }
    });

    // Tipos com o mesmo nome em namespaces diferentes não podem colidir no documento.
    options.CustomSchemaIds(type => type.FullName?.Replace("+", "."));
    options.ResolveConflictingActions(descriptions => descriptions.First());

    // Só os endpoints /api entram na documentação; as rotas internas de login ficam de fora.
    options.DocInclusionPredicate((_, description) =>
        description.RelativePath?.StartsWith("api/", StringComparison.OrdinalIgnoreCase) == true);
});

var app = builder.Build();

app.UseForwardedHeaders();

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["X-Permitted-Cross-Domain-Policies"] = "none";

    // Clickjacking protection.  Remotely can only be framed by itself unless
    // additional origins are explicitly allowed in ApplicationOptions.
    var frameAncestors = appOptions?.AllowedFrameAncestors?.Trim();
    if (string.IsNullOrWhiteSpace(frameAncestors))
    {
        headers["X-Frame-Options"] = "SAMEORIGIN";
        headers["Content-Security-Policy"] = "frame-ancestors 'self'";
    }
    else
    {
        headers["Content-Security-Policy"] = $"frame-ancestors 'self' {frameAncestors}";
    }

    await next();
});

app.UseRateLimiter();

if (settings.UseHttpLogging)
{
    app.UseHttpLogging();
}

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error");
    if (settings.UseHsts)
    {
        app.UseHsts();
    }
    if (settings.RedirectToHttps)
    {
        app.UseHttpsRedirection();
    }
}

ConfigureStaticFiles();

app.UseRouting();

// CORS must run after routing and before authentication/authorization.
app.UseCors("TrustedOriginPolicy");
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment() || appOptions?.EnableSwagger != false)
{
    // A documentação da API fica disponível só para usuários logados no painel.
    app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/swagger") &&
            context.User.Identity?.IsAuthenticated != true)
        {
            if (context.Request.Path.Value?.EndsWith(".json", StringComparison.OrdinalIgnoreCase) == true)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            var returnUrl = Uri.EscapeDataString($"{context.Request.PathBase}{context.Request.Path}");
            context.Response.Redirect($"{context.Request.PathBase}/Account/Login?ReturnUrl={returnUrl}");
            return;
        }

        await next();
    });

    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.DocumentTitle = "Remotely API";
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Remotely API v1");
        options.DisplayRequestDuration();
        options.EnableTryItOutByDefault();
    });
}

app.UseAntiforgery();


app.MapRazorPages();
app.MapHub<DesktopHub>("/hubs/desktop");
app.MapHub<ViewerHub>("/hubs/viewer");
app.MapHub<AgentHub>("/hubs/service");
app.MapControllers();

// No .NET 10 o script do Blazor (_framework/blazor.web.js) é um "static web asset"
// servido pelo manifesto de endpoints. Sem isto, a página carrega mas nenhum botão funciona.
app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapAdditionalIdentityEndpoints();

using (var scope = app.Services.CreateScope())
{
    var dataService = scope.ServiceProvider.GetRequiredService<IDataService>();

    await dataService.SetAllDevicesNotOnline();
    await dataService.CleanupOldRecords();
}

await app.RunAsync();

static bool IsAuthenticationRequest(HttpRequest request)
{
    if (!HttpMethods.IsPost(request.Method))
    {
        return false;
    }

    var path = request.Path;
    return
        path.StartsWithSegments("/Account/Login", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/Account/LoginWith2fa", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/Account/LoginWithRecoveryCode", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/Account/Register", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/Account/ForgotPassword", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/Account/ResetPassword", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/Account/ResendEmailConfirmation", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/api/Login", StringComparison.OrdinalIgnoreCase) ||
        // Legacy remote control endpoint that accepts email/password in the body.
        path.Equals("/api/RemoteControl", StringComparison.OrdinalIgnoreCase);
}

void ConfigureStaticFiles()
{
    var provider = new FileExtensionContentTypeProvider();
    // Add new mappings
    provider.Mappings[".ps1"] = "application/octet-stream";
    provider.Mappings[".exe"] = "application/octet-stream";
    provider.Mappings[".dll"] = "application/octet-stream";
    provider.Mappings[".appimage"] = "application/octet-stream";
    provider.Mappings[".zip"] = "application/octet-stream";
    provider.Mappings[".config"] = "application/octet-stream";
    app.UseStaticFiles();
    var contentPath = Path.Combine(app.Environment.WebRootPath, "Content");

    if (Directory.Exists(contentPath))
    {
        app.UseStaticFiles(new StaticFileOptions()
        {
            FileProvider = new PhysicalFileProvider(Path.Combine(app.Environment.WebRootPath, "Content")),
            ServeUnknownFileTypes = true,
            RequestPath = new PathString("/Content"),
            ContentTypeProvider = provider,
            DefaultContentType = "application/octet-stream"
        });
    }

    // Needed for Let's Encrypt.
    if (Directory.Exists(Path.Combine(app.Environment.ContentRootPath, ".well-known")))
    {
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(Path.Combine(app.Environment.ContentRootPath, @".well-known")),
            RequestPath = new PathString("/.well-known"),
            ServeUnknownFileTypes = true
        });
    }
}

void ConfigureSerilog(WebApplicationBuilder webAppBuilder, SettingsModel settings)
{
    try
    {
        var dataRetentionDays = settings.DataRetentionInDays;
        if (dataRetentionDays <= 0)
        {
            dataRetentionDays = 7;
        }

        var logPath = LogsManager.DefaultLogsDirectory;

        void ApplySharedLoggerConfig(LoggerConfiguration loggerConfiguration)
        {
            loggerConfiguration
                .Enrich.FromLogContext()
                .Enrich.WithThreadId()
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties}{NewLine}{Exception}")
                .WriteTo.File($"{logPath}/Remotely_Server.log",
                    rollingInterval: RollingInterval.Day,
                    retainedFileTimeLimit: TimeSpan.FromDays(dataRetentionDays),
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties}{NewLine}{Exception}",
                    shared: true);
        }

        // https://github.com/serilog/serilog-aspnetcore#two-stage-initialization
        var loggerConfig = new LoggerConfiguration();
        ApplySharedLoggerConfig(loggerConfig);
        Log.Logger = loggerConfig.CreateBootstrapLogger();

        builder.Host.UseSerilog((context, services, configuration) =>
        {
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services);

            ApplySharedLoggerConfig(configuration);
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to configure Serilog file logging.  Error: {ex.Message}");
    }
}