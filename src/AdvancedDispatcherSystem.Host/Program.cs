using AdvancedDispatcherSystem.Core;
using AdvancedDispatcherSystem.Host;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

var argsMap = new Dictionary<string, string>();
for (int i = 0; i + 1 < args.Length; i += 2) argsMap[args[i]] = args[i + 1];
string Option(string key, string fallback) => argsMap.TryGetValue(key, out var v) ? v : fallback;
int port = int.Parse(Option("--port", "7246"));
if (port < 1024 || port > 65535) throw new ArgumentException("INVALID_PORT");
bool lan = Option("--lan", "false") == "true", https = Option("--https", "false") == "true";
string secret = Environment.GetEnvironmentVariable("ADS_IPC_TOKEN");
bool fixture = Option("--fixture", "false") == "true";
if (string.IsNullOrEmpty(secret) || secret.Length < 32) throw new ArgumentException("IPC_SECRET_REQUIRED");
string configDirectory = Option("--config", Path.Combine(AppContext.BaseDirectory, "data"));
var allowedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "localhost", "127.0.0.1", "::1", Environment.MachineName };
foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()) foreach (var addr in nic.GetIPProperties().UnicastAddresses) allowedHosts.Add(addr.Address.ToString());
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = Array.Empty<string>(), ContentRootPath = AppContext.BaseDirectory, WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot") });
builder.Logging.ClearProviders(); builder.Logging.AddSimpleConsole(o => o.SingleLine = true);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.MaxRequestBodySize = Protocol.MaxCommandBytes;
    o.Limits.MaxConcurrentConnections = 100; o.Limits.MaxConcurrentUpgradedConnections = 64; // Hub admits 50 peers; leave room for closing sockets.
    o.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
    o.Listen(lan ? IPAddress.Any : IPAddress.Loopback, port, listen =>
    {
        if (!https) return;
        Directory.CreateDirectory(configDirectory);
        string path = Path.Combine(configDirectory, "server.pfx");
        if (!File.Exists(path))
        {
            using RSA rsa = RSA.Create(3072);
            var request = new CertificateRequest("CN=Advanced Dispatcher System", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var san = new SubjectAlternativeNameBuilder();
            foreach (string host in allowedHosts) { if (IPAddress.TryParse(host, out var ip)) san.AddIpAddress(ip); else san.AddDnsName(host); }
            request.CertificateExtensions.Add(san.Build());
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
            using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
            File.WriteAllBytes(path, cert.Export(X509ContentType.Pfx));
        }
        listen.UseHttps(X509CertificateLoader.LoadPkcs12FromFile(path, null));
    });
});
var hub = new StateHub(); var accounts = new Accounts(configDirectory, secret); var rates = new RateGate();
int.TryParse(Option("--ipc-port", "0"), out int ipcPort);
if (!fixture && (ipcPort < 1 || ipcPort > 65535)) throw new ArgumentException("IPC_PORT_REQUIRED");
var game = new GameConnection(secret, hub, ipcPort); var gateway = new CommandGateway(hub, game);
var app = builder.Build();
app.Use(async (ctx, next) =>
{
    if (!allowedHosts.Contains(ctx.Request.Host.Host) || ctx.Request.Host.Port != port) { ctx.Response.StatusCode = 400; return; }
    string origin = ctx.Request.Headers.Origin;
    string expected = (https ? "https" : "http") + "://" + ctx.Request.Host;
    if ((!string.IsNullOrEmpty(origin) && !string.Equals(origin, expected, StringComparison.OrdinalIgnoreCase)) || (ctx.Request.Method != "GET" && string.IsNullOrEmpty(origin))) { ctx.Response.StatusCode = 403; return; }
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    ctx.Response.Headers["Content-Security-Policy"] = "default-src 'self'; connect-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
    if (ctx.Request.Path.StartsWithSegments("/api")) ctx.Response.Headers.CacheControl = "no-store";
    try { await next(); }
    catch (Exception e) when (e is JsonException || e is ArgumentException || e is BadHttpRequestException) { if (!ctx.Response.HasStarted) ctx.Response.StatusCode = 400; }
});
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });
app.UseDefaultFiles(); app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = c => c.Context.Response.Headers.CacheControl = "no-cache" });
AuthSession User(HttpContext ctx) => accounts.Get(ctx.Request.Cookies["ads_session"]);
app.MapPost("/api/login", async (HttpContext ctx) =>
{
    if (!rates.Allow("login:" + ctx.Connection.RemoteIpAddress, 10, 60000)) return Results.StatusCode(429);
    var login = await System.Text.Json.JsonSerializer.DeserializeAsync<LoginRequest>(ctx.Request.Body, Json.Options, ctx.RequestAborted);
    var result = await accounts.Login(login?.name, login?.password, login?.bootstrap, IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress ?? IPAddress.Any));
    if (result.Session == null) return Results.Unauthorized();
    ctx.Response.Cookies.Append("ads_session", result.Token, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = https, MaxAge = TimeSpan.FromHours(8), Path = "/" });
    return Results.Json(new { result.Session.name, result.Session.role }, Json.Options);
});
app.MapPost("/api/logout", (HttpContext ctx) => { accounts.Logout(ctx.Request.Cookies["ads_session"]); ctx.Response.Cookies.Delete("ads_session"); return Results.NoContent(); });
app.MapGet("/api/me", (HttpContext ctx) => User(ctx) is { } user ? Results.Json(new { user.name, user.role }, Json.Options) : Results.Unauthorized());
app.MapGet("/api/health", (HttpContext ctx) => User(ctx) != null ? Results.Json(hub.Health(accounts.RecoveryRequired), Json.Options) : Results.Unauthorized());
app.MapGet("/api/accounts", (HttpContext ctx) => User(ctx)?.role == "admin" ? Results.Json(accounts.List(), Json.Options) : Results.StatusCode(403));
app.MapPost("/api/accounts", async (HttpContext ctx) =>
{
    var user = User(ctx);
    if (user?.role != "admin") return Results.StatusCode(403);
    if (!rates.Allow("account:" + user.name, 4, 60000)) return Results.StatusCode(429);
    var request = await System.Text.Json.JsonSerializer.DeserializeAsync<LoginRequest>(ctx.Request.Body, Json.Options, ctx.RequestAborted);
    return request != null && accounts.Set(request.name, request.role, request.password) ? Results.NoContent() : Results.BadRequest();
});
app.MapGet("/api/route", (HttpContext ctx) =>
{
    var user = User(ctx);
    if (user == null) return Results.Unauthorized();
    if (!rates.Allow("route:" + user.name, 10, 1000)) return Results.StatusCode(429);
    var via = ctx.Request.Query["via"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var route = hub.Preview(ctx.Request.Query["from"], ctx.Request.Query["to"], ctx.Request.Query["train"], via);
    return route != null ? Results.Json(route, Json.Options) : Results.Json(new { code = hub.RouteFailure(ctx.Request.Query["from"],ctx.Request.Query["to"],ctx.Request.Query["train"],via) }, Json.Options, statusCode:404);
});
app.Map("/api/ws", ctx => BrowserSocket.Handle(ctx, hub, accounts, gateway, rates));
using var lifetime = new CancellationTokenSource();
app.Lifetime.ApplicationStopping.Register(lifetime.Cancel);
var ipcTask = fixture ? DemoFixture.Run(hub, lifetime.Token) : game.Run(lifetime.Token);
int.TryParse(Option("--parent", "0"), out int parentId);
if (parentId > 0) _ = Task.Run(async () =>
{
    try
    {
        using var parent = System.Diagnostics.Process.GetProcessById(parentId);
        await parent.WaitForExitAsync(lifetime.Token); await app.StopAsync();
    }
    catch (ArgumentException) { await app.StopAsync(); }
    catch (OperationCanceledException) { }
});
Console.WriteLine("Advanced Dispatcher System listening on " + (https ? "https" : "http") + "://" + (lan ? "0.0.0.0" : "127.0.0.1") + ":" + port);
try { await app.RunAsync(); }
finally { lifetime.Cancel(); try { await ipcTask; } catch (OperationCanceledException) { } }

public sealed class LoginRequest { public string name, password, role, bootstrap; }
