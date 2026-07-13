using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Spid.Components;
using Spid.Data;
using Spid.Services;


var builder = WebApplication.CreateBuilder(args);
var isDevelopment = builder.Environment.IsDevelopment();
var dataProtectionKeysPath = Path.Combine(builder.Environment.ContentRootPath, "keys");
var useHttpsRedirection = builder.Configuration.GetValue("IIS:UseHttpsRedirection", false);
var httpsPort = builder.Configuration.GetValue<int?>("IIS:HttpsPort");

// Static Web Assets no ambiente Docker
if (builder.Environment.IsEnvironment("Docker"))
{
    StaticWebAssetsLoader.UseStaticWebAssets(builder.Environment, builder.Configuration);
}

var connectionString = Environment.GetEnvironmentVariable("SPID_CONNECTION_STRING")
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("A connection string 'DefaultConnection' não foi configurada. Defina via variável de ambiente SPID_CONNECTION_STRING ou em appsettings.");

// DbContext principal
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

// Factory para contextos curtos
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlServer(connectionString),
    ServiceLifetime.Scoped);

// Componentes Razor / Blazor Server
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ⭐ CONFIGURAÇÃO DE SIGNALR - CRÍTICO PARA PRODUÇÃO
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
    options.HandshakeTimeout = TimeSpan.FromSeconds(15);
    options.MaximumParallelInvocationsPerClient = 1;
});

builder.Services.AddScoped<UserSession>();
builder.Services.AddScoped<ImportacaoService>();

var sessionTimeoutMinutes = builder.Configuration.GetValue<int>("SessionTimeoutMinutes", 45);

builder.Services.AddAuthentication("SpidCookie")
    .AddCookie("SpidCookie", options =>
    {
        options.Cookie.Name = "SpidAuthToken";
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(sessionTimeoutMinutes);
        options.SlidingExpiration = false; // Rigid expiration so we can manage manual extension

        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SecurePolicy = useHttpsRedirection
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite = SameSiteMode.Lax;
    });

// Persistir chaves do Cookie no HD para não invalidar a sessão em caso de Load Balancer / Web Garden
Directory.CreateDirectory(dataProtectionKeysPath);
builder.Services.AddDataProtection()
    .SetApplicationName("Spid")
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

// ⭐ CORS - Restritivo em produção, aberto apenas em desenvolvimento
var allowedOrigins = builder.Configuration.GetSection("AllowedCorsOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("SpidCors", policy =>
    {
        if (isDevelopment || allowedOrigins.Length == 0)
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
        else
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .WithExposedHeaders("Content-Disposition");
        }
    });
});

// Health check para monitoramento
builder.Services.AddHealthChecks()
    .AddSqlServer(connectionString, name: "sqlserver");

// Forwarded headers para Nginx/Reverse Proxies (opcional)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto |
        ForwardedHeaders.XForwardedHost;

    options.RequireHeaderSymmetry = false;
    options.ForwardLimit = null;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// HTTPS opcional por configuração
if (useHttpsRedirection && httpsPort.HasValue)
{
    builder.Services.AddHttpsRedirection(options =>
    {
        options.HttpsPort = httpsPort.Value;
    });
}

var app = builder.Build();

// Força a formatação de números, moedas e datas para o padrão brasileiro
var cultureInfo = new System.Globalization.CultureInfo("pt-BR");
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;

app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture(cultureInfo),
    SupportedCultures = new[] { cultureInfo },
    SupportedUICultures = new[] { cultureInfo }
});

// Sobe o pipeline primeiro
app.UseForwardedHeaders();

app.Use(async (ctx, next) =>
{
    var forwardedProto = ctx.Request.Headers["X-Forwarded-Proto"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(forwardedProto))
    {
        ctx.Request.Scheme = forwardedProto;
    }
    else
    {
        var httpsServerVariables = new[]
        {
            ctx.Request.Headers["X-Forwarded-Ssl"].FirstOrDefault(),
            ctx.Request.Headers["Front-End-Https"].FirstOrDefault(),
            ctx.Request.Headers["X-ARR-SSL"].FirstOrDefault()
        };

        if (httpsServerVariables.Any(value =>
                string.Equals(value, "on", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "https", StringComparison.OrdinalIgnoreCase)))
        {
            ctx.Request.Scheme = "https";
        }
    }

    var forwardedHost = ctx.Request.Headers["X-Forwarded-Host"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(forwardedHost))
    {
        ctx.Request.Host = HostString.FromUriComponent(forwardedHost);
    }

    await next();
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // Nota: UseHsts removido — não usar HSTS sem HTTPS configurado no IIS,
    // pois força o navegador a rejeitar requisições HTTP por até 1 ano.
}

if (useHttpsRedirection)
{
    app.UseHttpsRedirection();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// ⭐ WEBSOCKET - NECESSÁRIO PARA BLAZOR SERVER
app.UseWebSockets();

app.UseRouting();

// ⭐ CORS - IMPORTANTE para APIs e requisições cross-origin
app.UseCors("SpidCors");

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();

// Login precisa ser HTML/POST HTTP puro, fora do circuito Blazor/SignalR,
// para que SignInAsync consiga gravar o cookie de autenticação na resposta.
app.MapGet("/login", (HttpContext ctx) =>
{
    var erro = ctx.Request.Query["erro"].ToString();
    var erroHtml = erro switch
    {
        "campos" => """<div class="alert alert-danger" role="alert">Preencha o ponto e a senha.</div>""",
        "usuario" => """<div class="alert alert-danger" role="alert">Ponto não encontrado ou usuário inativo.</div>""",
        "senha" => """<div class="alert alert-danger" role="alert">Senha incorreta.</div>""",
        "faltap" => """<div class="alert alert-warning" role="alert">O Ponto deve começar com <strong>P_</strong>. Ex: P_123456</div>""",
        "ja_acessou" => """<div class="alert alert-warning" role="alert">Este usuário já realizou o primeiro acesso. Por favor, faça login com sua senha.</div>""",
        "use_primeiro_acesso" => """<div class="alert alert-info" role="alert">Este é o seu primeiro acesso. Por favor, utilize o botão <strong>Primeiro Acesso</strong> abaixo.</div>""",
        _ => ""
    };

    const string htmlTemplate = """
    <!DOCTYPE html>
    <html lang="pt-BR">
    <head>
        <meta charset="utf-8" />
        <meta name="viewport" content="width=device-width, initial-scale=1.0" />
        <title>Login - SPID</title>
        <link rel="stylesheet" href="/bootstrap.min.css" />
        <link rel="icon" type="image/png" href="/spid_web.png" />
        <style>
            body {
                background: linear-gradient(135deg, #1a2340 0%, #1a73e8 100%);
                min-height: 100vh;
                display: flex;
                align-items: center;
                justify-content: center;
                margin: 0;
                padding: 1rem;
            }

            .login-card {
                background: #fff;
                border-radius: 16px;
                padding: 2.5rem;
                box-shadow: 0 8px 32px rgba(0, 0, 0, 0.2);
                width: 100%;
                max-width: 400px;
            }

            .btn-primary {
                border: 0;
                border-radius: 8px;
                padding: 0.75rem 1rem;
                font-weight: 700;
                color: #fff;
                background: linear-gradient(135deg, #1a73e8, #1557ad);
                box-shadow: 0 3px 10px rgba(26, 115, 232, 0.28);
                transition: transform 0.16s ease, box-shadow 0.16s ease, background 0.16s ease;
            }

            .btn-primary:hover,
            .btn-primary:focus {
                color: #fff;
                background: linear-gradient(135deg, #1766ce, #104a96);
                box-shadow: 0 6px 16px rgba(26, 115, 232, 0.34);
                transform: translateY(-1px);
            }

            .btn-primary:active {
                transform: translateY(0);
                box-shadow: 0 3px 10px rgba(26, 115, 232, 0.28);
            }

            /* Custom Modal Styles */
            .custom-modal {
                display: none;
                position: fixed;
                z-index: 1000;
                left: 0;
                top: 0;
                width: 100%;
                height: 100%;
                background-color: rgba(0,0,0,0.5);
                align-items: center;
                justify-content: center;
            }
            .custom-modal-content {
                background-color: #fff;
                border-radius: 12px;
                width: 90%;
                max-width: 450px;
                overflow: hidden;
                box-shadow: 0 5px 15px rgba(0,0,0,0.3);
                animation: fadeIn 0.2s ease-in-out;
            }
            @keyframes fadeIn {
                from { opacity: 0; transform: scale(0.95); }
                to { opacity: 1; transform: scale(1); }
            }
            .custom-modal-header {
                background: linear-gradient(135deg, #1a73e8, #1557ad);
                color: white;
                padding: 1rem 1.5rem;
                display: flex;
                justify-content: space-between;
                align-items: center;
            }
            .custom-modal-header h5 {
                margin: 0;
                font-size: 1.1rem;
            }
            .custom-close {
                color: white;
                font-size: 1.5rem;
                font-weight: bold;
                cursor: pointer;
                line-height: 1;
            }
            .custom-modal-body {
                padding: 1.5rem;
                text-align: center;
                color: #333;
            }
            .custom-modal-footer {
                padding: 1rem;
                text-align: center;
                background-color: #f8f9fa;
                border-top: 1px solid #eee;
            }
        </style>
    </head>
    <body>
        <main class="login-card">
            <div class="text-center mb-3">
                <img src="/spid_web.png" alt="SPID Logo" style="max-width: 180px; width: 100%;" />
            </div>
            <h2 class="text-center mb-2">SPID</h2>
            <p class="text-center text-muted mb-4">Sistema de Gestão de Viagens</p>
            {{ERRO_HTML}}
            <form method="post" action="/do-login" autocomplete="on" data-enhance="false"
                  onkeydown="if (event.key === 'Enter' && event.target.tagName !== 'TEXTAREA') { event.preventDefault(); if (this.requestSubmit) { this.requestSubmit(); } else { this.submit(); } }">
                <div class="mb-3">
                    <label for="ponto" class="form-label">Ponto</label>
                    <input id="ponto" name="ponto" type="text" class="form-control" placeholder="Ex: P_******" autocomplete="username" required autofocus />
                </div>
                <div class="mb-3">
                    <label for="senha" class="form-label">Senha</label>
                    <input id="senha" name="senha" type="password" class="form-control" placeholder="Digite sua senha" autocomplete="current-password" required />
                </div>
                <br /><br />
                <button type="submit" class="btn btn-primary w-100">Entrar</button>

                <br /><br />
                <div class="d-flex w-100 align-items-center mt-1">
                    <div class="text-center" style="flex: 1 1 0;">
                        <a href="javascript:void(0)" onclick="openPrimeiroAcessoModal()" class="text-decoration-none">Primeiro Acesso</a>
                    </div>
                    <span class="text-muted">|</span>
                    <div class="text-center" style="flex: 1 1 0;">
                        <a href="javascript:void(0)" onclick="openModal()" class="text-decoration-none" style="font-size: 0.82em;">Esqueci minha senha</a>
                    </div>
                </div>
            </form>
        </main>

        <!-- Custom Modal -->
        <div id="forgotPasswordModal" class="custom-modal">
            <div class="custom-modal-content">
                <div class="custom-modal-header">
                    <h5>Aviso</h5>
                    <span class="custom-close" onclick="closeModal()">&times;</span>
                </div>
                <div class="custom-modal-body">
                    <p class="mb-3">A funcionalidade de <strong>Alterar Senha</strong> ainda está em desenvolvimento!</p>
                    <p class="mb-3">Caso tenha esquecido sua senha e precise alterá-la entre em contato pelo ramal:</p>
                    <h5 class="mb-1" style="color: #1a73e8; font-weight: 700;">6-3116 Dani</h5>
                    <h5 class="mb-0" style="color: #1a73e8; font-weight: 700;">6-3142 Flávia</h5>
                </div>
                <div class="custom-modal-footer">
                    <button type="button" class="btn btn-primary px-4" onclick="closeModal()">Entendido</button>
                </div>
            </div>
        </div>

        <!-- Modal Primeiro Acesso -->
        <div id="primeiroAcessoModal" class="custom-modal">
            <div class="custom-modal-content">
                <div class="custom-modal-header">
                    <h5>Primeiro Acesso</h5>
                    <span class="custom-close" onclick="closePrimeiroAcessoModal()">&times;</span>
                </div>
                <div class="custom-modal-body">
                    <form method="post" action="/do-primeiro-acesso">
                        <p class="mb-3">Informe seu Ponto para realizar o primeiro acesso no sistema:</p>
                        <div class="mb-3">
                            <input id="pontoPrimeiroAcesso" name="ponto" type="text" class="form-control" placeholder="Ex: P_******" required autocomplete="off" />
                        </div>
                        <div class="d-flex justify-content-center gap-3 mt-2">
                            <button type="button" class="btn btn-secondary px-4" onclick="closePrimeiroAcessoModal()">Cancelar</button>
                            <button type="submit" class="btn btn-primary px-4">Avançar</button>
                        </div>
                    </form>
                </div>
            </div>
        </div>

        <script>
            function openModal() {
                document.getElementById('forgotPasswordModal').style.display = 'flex';
            }

            function closeModal() {
                document.getElementById('forgotPasswordModal').style.display = 'none';
            }

            function openPrimeiroAcessoModal() {
                document.getElementById('primeiroAcessoModal').style.display = 'flex';
                setTimeout(() => document.getElementById('pontoPrimeiroAcesso').focus(), 100);
            }

            function closePrimeiroAcessoModal() {
                document.getElementById('primeiroAcessoModal').style.display = 'none';
            }
            window.onclick = function(event) {
                var modal = document.getElementById('forgotPasswordModal');
                if (event.target == modal) {
                    closeModal();
                }
                var modalPA = document.getElementById('primeiroAcessoModal');
                if (event.target == modalPA) {
                    closePrimeiroAcessoModal();
                }
            }
        </script>
    </body>
    </html>
    """;

    ctx.Response.Headers.CacheControl = "no-store";
    return Results.Content(htmlTemplate.Replace("{{ERRO_HTML}}", erroHtml), "text/html; charset=utf-8");
}).AllowAnonymous();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/logout-handler", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync("SpidCookie");
    return Results.Redirect("/login");
});

app.MapPost("/do-primeiro-acesso", async (HttpContext ctx, AppDbContext db) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var ponto = form["ponto"].ToString().Trim();
    
    if (ponto.StartsWith("p_", StringComparison.OrdinalIgnoreCase))
    {
        ponto = ponto.ToLower();
    }

    if (string.IsNullOrWhiteSpace(ponto))
        return Results.Redirect("/login?erro=campos");

    var usuario = await db.Usuarios
        .FirstOrDefaultAsync(u => u.Ponto == ponto && u.Ativo);

    if (usuario is null)
    {
        var possivelPonto = "p_" + ponto;
        bool esqueceuP = ponto.Length > 4 || await db.Usuarios.AnyAsync(u => u.Ponto.ToLower() == possivelPonto.ToLower() && u.Ativo);
        
        if (esqueceuP)
        {
            return Results.Redirect("/login?erro=faltap");
        }
        return Results.Redirect("/login?erro=usuario");
    }

    if (usuario.ContadorAcessos > 0)
        return Results.Redirect("/login?erro=ja_acessou");

    var sessionTimeoutMinutes = ctx.RequestServices.GetRequiredService<IConfiguration>().GetValue<int>("SessionTimeoutMinutes", 45);
    var expiresUtc = DateTimeOffset.UtcNow.AddMinutes(sessionTimeoutMinutes);

    var claims = new List<System.Security.Claims.Claim>
    {
        new("UserId", usuario.Id.ToString()),
        new(System.Security.Claims.ClaimTypes.Name, usuario.Nome),
        new("SessionExpiresUtc", expiresUtc.ToString("o"))
    };

    var identity = new System.Security.Claims.ClaimsIdentity(claims, "SpidCookie");
    var principal = new System.Security.Claims.ClaimsPrincipal(identity);

    await ctx.SignInAsync("SpidCookie", principal, new AuthenticationProperties
    {
        IsPersistent = false,
        ExpiresUtc = expiresUtc
    });

    return Results.Redirect("/primeiro-acesso");
});

app.MapPost("/do-login", async (HttpContext ctx, AppDbContext db) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var ponto = form["ponto"].ToString().Trim();
    
    if (ponto.StartsWith("p_", StringComparison.OrdinalIgnoreCase))
    {
        ponto = ponto.ToLower();
    }

    var senha = form["senha"].ToString();

    if (string.IsNullOrWhiteSpace(ponto) || string.IsNullOrWhiteSpace(senha))
        return Results.Redirect("/login?erro=campos");

    var usuario = await db.Usuarios
        .FirstOrDefaultAsync(u => u.Ponto == ponto && u.Ativo);

    if (usuario is null)
    {
        if (!ponto.StartsWith("p_", StringComparison.OrdinalIgnoreCase))
        {
            var possivelPonto = "p_" + ponto;
            bool esqueceuP = ponto.Length > 4 || await db.Usuarios.AnyAsync(u => u.Ponto.ToLower() == possivelPonto.ToLower() && u.Ativo);
            
            if (esqueceuP)
            {
                return Results.Redirect("/login?erro=faltap");
            }
        }
        return Results.Redirect("/login?erro=usuario");
    }

    if (usuario.ContadorAcessos == 0)
    {
        return Results.Redirect("/login?erro=use_primeiro_acesso");
    }

    var hasher = new Microsoft.AspNetCore.Identity.PasswordHasher<Usuario>();
    var result = hasher.VerifyHashedPassword(usuario, usuario.SenhaHash, senha);

    if (result == Microsoft.AspNetCore.Identity.PasswordVerificationResult.Failed)
        return Results.Redirect("/login?erro=senha");

    var sessionTimeoutMinutes = ctx.RequestServices.GetRequiredService<IConfiguration>().GetValue<int>("SessionTimeoutMinutes", 45);
    var expiresUtc = DateTimeOffset.UtcNow.AddMinutes(sessionTimeoutMinutes);

    var claims = new List<System.Security.Claims.Claim>
    {
        new("UserId", usuario.Id.ToString()),
        new(System.Security.Claims.ClaimTypes.Name, usuario.Nome),
        new("SessionExpiresUtc", expiresUtc.ToString("o"))
    };

    var identity = new System.Security.Claims.ClaimsIdentity(claims, "SpidCookie");
    var principal = new System.Security.Claims.ClaimsPrincipal(identity);

    await ctx.SignInAsync("SpidCookie", principal, new AuthenticationProperties
    {
        IsPersistent = true,
        ExpiresUtc = expiresUtc
    });

    return Results.Redirect("/");
}).DisableAntiforgery();

app.MapPost("/extend-session", async (HttpContext ctx) =>
{
    if (ctx.User.Identity?.IsAuthenticated != true)
        return Results.Unauthorized();

    var sessionTimeoutMinutes = ctx.RequestServices.GetRequiredService<IConfiguration>().GetValue<int>("SessionTimeoutMinutes", 45);

    var newExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(sessionTimeoutMinutes);

    var identity = new System.Security.Claims.ClaimsIdentity(ctx.User.Identity);
    var existingClaim = identity.FindFirst("SessionExpiresUtc");
    if (existingClaim != null) identity.RemoveClaim(existingClaim);
    identity.AddClaim(new System.Security.Claims.Claim("SessionExpiresUtc", newExpiresUtc.ToString("o")));

    await ctx.SignInAsync("SpidCookie", new System.Security.Claims.ClaimsPrincipal(identity), new AuthenticationProperties
    {
        IsPersistent = true,
        ExpiresUtc = newExpiresUtc
    });

    return Results.Ok(new { ExpiresUtc = newExpiresUtc });
}).DisableAntiforgery();

app.MapHealthChecks("/health");

var runDatabaseSetupOnBoot = builder.Configuration.GetValue("StartupTasks:RunDatabaseSetupOnBoot", false);

if (runDatabaseSetupOnBoot)
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        _ = Task.Run(async () =>
        {
            var logger = app.Services
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("StartupDatabaseTasks");

            try
            {
                await RunDatabaseSetupSafelyAsync(app.Services, logger);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha não tratada nas tarefas de banco executadas após o startup.");
            }
        });
    });
}

await app.RunAsync();

static async Task RunDatabaseSetupSafelyAsync(IServiceProvider rootServices, ILogger logger)
{
    await using var scope = rootServices.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    try
    {
        logger.LogInformation("Iniciando migration/seed pós-startup do Spid.");

        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(2));

        await db.Database.MigrateAsync();

        if (!await db.Recursos.AnyAsync())
        {
            var importar = new Recurso
            {
                Chave = "ImportarViagens",
                Nome = "Importar Viagens",
                Descricao = "Permite importar planilhas Excel com dados de viagens"
            };

            var analisar = new Recurso
            {
                Chave = "AnalisarViagens",
                Nome = "Analisar Viagens",
                Descricao = "Permite conferir e aprovar/contestar viagens"
            };

            db.Recursos.AddRange(importar, analisar);
            await db.SaveChangesAsync();

            db.PerfisRecurso.AddRange(
                new PerfilRecurso { Perfil = "Admin", RecursoId = importar.Id },
                new PerfilRecurso { Perfil = "Gestor Titular", RecursoId = analisar.Id },
                new PerfilRecurso { Perfil = "Gestor Substituto", RecursoId = analisar.Id }
            );

            await db.SaveChangesAsync();
        }

        var gestoresAntigos = await db.Usuarios
            .Where(u => u.Perfil == "Gestor")
            .ToListAsync();

        if (gestoresAntigos.Count > 0)
        {
            foreach (var u in gestoresAntigos)
                u.Perfil = "Gestor Titular";

            await db.SaveChangesAsync();
        }

        var perfisAntigos = await db.PerfisRecurso
            .Where(pr => pr.Perfil == "Gestor")
            .ToListAsync();

        if (perfisAntigos.Count > 0)
        {
            foreach (var pr in perfisAntigos)
                pr.Perfil = "Gestor Titular";

            await db.SaveChangesAsync();
        }

        var analisarRecurso = await db.Recursos
            .FirstOrDefaultAsync(r => r.Chave == "AnalisarViagens");

        if (analisarRecurso is not null)
        {
            var jaTemSecundario = await db.PerfisRecurso.AnyAsync(pr =>
                pr.Perfil == "Gestor Substituto" &&
                pr.RecursoId == analisarRecurso.Id);

            if (!jaTemSecundario)
            {
                db.PerfisRecurso.Add(new PerfilRecurso
                {
                    Perfil = "Gestor Substituto",
                    RecursoId = analisarRecurso.Id
                });

                await db.SaveChangesAsync();
            }
        }

        logger.LogInformation("Migration/seed pós-startup finalizado com sucesso.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Falha ao executar migration/seed pós-startup. A aplicação continuará ativa.");
    }
}
