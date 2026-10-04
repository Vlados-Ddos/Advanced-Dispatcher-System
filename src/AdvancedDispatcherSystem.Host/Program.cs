using AdvancedDispatcherSystem.Core;
using AdvancedDispatcherSystem.Host;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

try
{
var argsMap = new Dictionary<string, string>();
for (int i = 0; i + 1 < args.Length; i += 2) argsMap[args[i]] = args[i + 1];
string Option(string key, string fallback) => argsMap.TryGetValue(key, out var v) ? v : fallback;
int port = int.Parse(Option("--port", "7246"));
if (port < 1024 || port > 65535) throw new ArgumentException("INVALID_PORT");
bool remoteAccess = Option("--remote-access", "true") == "true";
bool https = remoteAccess;
string secret = Environment.GetEnvironmentVariable("ADS_IPC_TOKEN");
bool fixture = Option("--fixture", "false") == "true";
if (string.IsNullOrEmpty(secret) || secret.Length < 32) throw new ArgumentException("IPC_SECRET_REQUIRED");
string configDirectory = Option("--config", Path.Combine(AppContext.BaseDirectory, "data"));
// Secure the data directory before writing private keys.
var accounts = new Accounts(configDirectory, secret);
var interfaces = NetworkInterface.GetAllNetworkInterfaces()
    .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
    .SelectMany(n => n.GetIPProperties().UnicastAddresses
        .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address) && !a.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
        .Select(a => new { label = (n.Name + " " + n.Description).Contains("Radmin", StringComparison.OrdinalIgnoreCase) || n.Description.Contains("Famatech", StringComparison.OrdinalIgnoreCase) ? "Radmin VPN" : "LAN", address = a.Address.ToString() }))
    .DistinctBy(n => n.address).ToArray();
var localHosts = new List<string> { "localhost", "127.0.0.1", "::1", Environment.MachineName };
localHosts.AddRange(interfaces.Select(n => n.address));
var allowedHosts = HostAllowlist.Build(localHosts, Option("--public-host", ""));
using var certificate = https ? new LocalCertificate(configDirectory, allowedHosts) : null;
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = Array.Empty<string>(), ContentRootPath = AppContext.BaseDirectory, WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot") });
builder.Logging.ClearProviders(); builder.Logging.AddSimpleConsole(o => o.SingleLine = true);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.MaxRequestBodySize = Protocol.MaxCommandBytes;
    o.Limits.MaxConcurrentConnections = 100; o.Limits.MaxConcurrentUpgradedConnections = 64; // Hub admits 50 peers; leave room for closing sockets.
    o.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
    void ConfigureListener(Microsoft.AspNetCore.Server.Kestrel.Core.ListenOptions listen)
    {
        if (!https) return;
        listen.UseHttps(certificate.Server);
    }
    if(remoteAccess) o.ListenAnyIP(port,ConfigureListener);
    else {
        o.Listen(IPAddress.Loopback,port,ConfigureListener);
        if(System.Net.Sockets.Socket.OSSupportsIPv6)o.Listen(IPAddress.IPv6Loopback,port,ConfigureListener);
    }
});
var hub = new StateHub(configDirectory); var rates = new RateGate();
int.TryParse(Option("--ipc-port", "0"), out int ipcPort);
if (!fixture && (ipcPort < 1 || ipcPort > 65535)) throw new ArgumentException("IPC_PORT_REQUIRED");
var game = new GameConnection(secret, hub, ipcPort); var gateway = new CommandGateway(hub, game);
var app = builder.Build();
app.Use(async (ctx, next) =>
{
    string requestHost=ctx.Request.Host.Host;
    if(requestHost.StartsWith('[')&&requestHost.EndsWith(']'))requestHost=requestHost[1..^1];
    if (!allowedHosts.Contains(requestHost) || ctx.Request.Host.Port != port) { ctx.Response.StatusCode = 400; return; }
    string origin = ctx.Request.Headers.Origin;
    string expected = (https ? "https" : "http") + "://" + ctx.Request.Host;
    if ((!string.IsNullOrEmpty(origin) && !string.Equals(origin, expected, StringComparison.OrdinalIgnoreCase)) || (ctx.Request.Method != "GET" && string.IsNullOrEmpty(origin))) { ctx.Response.StatusCode = 403; return; }
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    ctx.Response.Headers["Content-Security-Policy"] = "default-src 'self'; connect-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
    if (ctx.Request.Path.StartsWithSegments("/api")) ctx.Response.Headers.CacheControl = "no-store";
    try { await next(); }
    catch (RouteSearchLimitException) { if (!ctx.Response.HasStarted) {ctx.Response.StatusCode=422;await ctx.Response.WriteAsJsonAsync(new {code="ROUTE_SEARCH_LIMIT"});} }
    catch (Exception e) when (e is JsonException || e is ArgumentException || e is BadHttpRequestException) { if (!ctx.Response.HasStarted) ctx.Response.StatusCode = 400; }
});
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15), KeepAliveTimeout = TimeSpan.FromSeconds(15) });
app.UseDefaultFiles(); app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = c => c.Context.Response.Headers.CacheControl = "no-cache" });
bool Activity(HttpContext ctx) => ctx.Request.Headers.TryGetValue("X-ADS-Activity", out var value) && value == "1";
AuthSession User(HttpContext ctx, bool touch = true) => accounts.Get(SessionCookie.Read(ctx), touch);
app.MapPost("/api/login", async (HttpContext ctx) =>
{
    if (!rates.Allow("login:" + ctx.Connection.RemoteIpAddress, 10, 60000)) return Results.StatusCode(429);
    var login = await System.Text.Json.JsonSerializer.DeserializeAsync<LoginRequest>(ctx.Request.Body, Json.Options, ctx.RequestAborted);
    var result = await accounts.Login(login?.name, login?.password, login?.bootstrap, IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress ?? IPAddress.Any), currentToken: SessionCookie.Read(ctx));
    if (result.Session == null)
        return result.Error == "SESSION_ACTIVE"
            ? Results.Conflict(new { code = "SESSION_ACTIVE" })
            : Results.Unauthorized();
    ctx.Response.Cookies.Append(SessionCookie.Name(ctx), result.Token, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = https, MaxAge = TimeSpan.FromHours(8), Path = "/" });
    return Results.Json(new { result.Session.name, result.Session.role }, Json.Options);
});
app.MapPost("/api/logout", (HttpContext ctx) => { accounts.Logout(SessionCookie.Read(ctx)); ctx.Response.Cookies.Delete(SessionCookie.Name(ctx), new CookieOptions { Path = "/", Secure = https, HttpOnly = true, SameSite = SameSiteMode.Strict }); return Results.NoContent(); });
app.MapGet("/api/me", (HttpContext ctx) => User(ctx) is { } user ? Results.Json(new { user.name, user.role }, Json.Options) : Results.Unauthorized());
app.MapGet("/api/health", (HttpContext ctx) => User(ctx, Activity(ctx)) != null ? Results.Json(hub.Health(accounts.RecoveryRequired), Json.Options) : Results.Unauthorized());
app.MapGet("/api/accounts", (HttpContext ctx) => User(ctx)?.role == "admin" ? Results.Json(accounts.List(), Json.Options) : Results.StatusCode(403));
IResult AccountResult(string error) => error == null ? Results.NoContent() :
    Results.Json(new { code = error }, statusCode: error == "ACCOUNT_NOT_FOUND" ? 404 : error == "ACCOUNT_EXISTS" || error == "ACCOUNT_LIMIT" ? 409 : 400);
app.MapPost("/api/accounts", async (HttpContext ctx) =>
{
    var user = User(ctx);
    if (user?.role != "admin") return Results.StatusCode(403);
    if (!rates.Allow("account:" + user.name, 4, 60000)) return Results.StatusCode(429);
    var request = await System.Text.Json.JsonSerializer.DeserializeAsync<LoginRequest>(ctx.Request.Body, Json.Options, ctx.RequestAborted);
    return AccountResult(request == null ? "INVALID_ACCOUNT" : accounts.Create(request.name, request.role, request.password));
});
app.MapPut("/api/accounts/{name}", async (HttpContext ctx, string name) =>
{
    var user = User(ctx);
    if (user?.role != "admin") return Results.StatusCode(403);
    if (!rates.Allow("account:" + user.name, 4, 60000)) return Results.StatusCode(429);
    var request = await System.Text.Json.JsonSerializer.DeserializeAsync<LoginRequest>(ctx.Request.Body, Json.Options, ctx.RequestAborted);
    return AccountResult(request == null || request.name != null && request.name != name ? "INVALID_ACCOUNT" : accounts.Update(name, request.role, request.password));
});
app.MapDelete("/api/accounts/{name}", (HttpContext ctx, string name) =>
    User(ctx)?.role != "admin" ? Results.StatusCode(403) : AccountResult(accounts.Delete(name) ? null : "ACCOUNT_NOT_FOUND"));
app.MapGet("/api/connection", (HttpContext ctx) =>
{
    if (User(ctx)?.role != "admin") return Results.StatusCode(403);
    string scheme = https ? "https" : "http";
    var addresses = new List<object> { new { label = "Local", url = scheme + "://localhost:" + port } };
    if (remoteAccess)
    {
        addresses.AddRange(interfaces.Select(n => (object)new { n.label, url = scheme + "://" + n.address + ":" + port }));
        addresses.AddRange(HostAllowlist.PublicUrls(Option("--public-host", ""), https, port)
            .Select(url => (object)new { label = "Public", url }));
    }
    return Results.Json(new { remoteAccess, https, port, addresses, certificate = certificate == null ? null : new { sha256 = certificate.Fingerprint, expires = certificate.Authority.NotAfter.ToUniversalTime().ToString("O"), download = "/api/certificate" } });
});
app.MapGet("/api/certificate", (HttpContext ctx) => User(ctx)?.role != "admin" ? Results.StatusCode(403) :
    certificate == null ? Results.NotFound() : Results.File(certificate.Authority.Export(X509ContentType.Cert), "application/pkix-cert", "AdvancedDispatch-CA.cer"));
app.MapGet("/api/route", async (HttpContext ctx) =>
{
    var user = User(ctx);
    if (user == null) return Results.Unauthorized();
    if (!rates.Allow("route:" + user.name, 10, 1000)) return Results.StatusCode(429);
    var via = ctx.Request.Query["via"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    int taskIndex=int.TryParse(ctx.Request.Query["taskIndex"],out var index)?index:-1;
    string jobId=ctx.Request.Query["jobId"],taskId=ctx.Request.Query["taskId"];
    bool avoidReservations = !bool.TryParse(ctx.Request.Query["avoidReservations"], out var avoid) || avoid;
    var route = await hub.PreviewAsync(ctx.Request.Query["from"], ctx.Request.Query["to"], ctx.Request.Query["train"], via, ctx.RequestAborted,jobId,taskIndex,taskId,ctx.Request.Query["editingId"],user.name,avoidReservations);
    return route != null ? Results.Json(route, Json.Options) : Results.Json(new { code = hub.JobRouteError(jobId,taskIndex,taskId,ctx.Request.Query["from"],ctx.Request.Query["to"],via,train:ctx.Request.Query["train"])??hub.RouteFailure(ctx.Request.Query["from"],ctx.Request.Query["to"],ctx.Request.Query["train"],via) }, Json.Options, statusCode:404);
});
app.Map("/api/ws", ctx => BrowserSocket.Handle(ctx, hub, accounts, gateway, rates));
using var lifetime = new CancellationTokenSource();
app.Lifetime.ApplicationStopping.Register(lifetime.Cancel);
Task ipcTask = Task.CompletedTask;
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
    catch (Exception e) when (!lifetime.IsCancellationRequested)
    {
        // Parent monitoring is a production lifecycle task.  Observe its
        // failure instead of leaving an unobserved Task exception that looks
        // like a Host.exe crash, while still stopping the host and retaining
        // the diagnostic type in stderr.
        Console.Error.WriteLine("HOST_PARENT_MONITOR_FAILED " + e.GetType().Name);
        await app.StopAsync();
    }
});
try
{
    // A failed bind/certificate must never announce a ready bridge to Unity.
    await app.StartAsync();
    Console.WriteLine("Advanced Dispatcher System listening on " + (https ? "https" : "http") + "://" + (remoteAccess ? "0.0.0.0" : "127.0.0.1") + ":" + port);
    ipcTask = fixture ? DemoFixture.Run(hub, lifetime.Token) : game.Run(lifetime.Token);
    await app.WaitForShutdownAsync();
}
finally
{
    lifetime.Cancel();
    try { await ipcTask; }
    catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    catch (Exception e)
    {
        Console.Error.WriteLine("HOST_IPC_FAILED " + e.GetType().Name);
        throw;
    }
}

}
catch (Exception error)
{
    // Exit normally with a failure code. An unhandled startup exception can
    // leave a hidden Windows Error Reporting process alive, so Unity sees a
    // living child that can never establish IPC or serve HTTP.
    Console.Error.WriteLine(error);
    Console.Error.WriteLine("WEB_HOST_START_FAILED " + error.GetType().Name + ": " + error.Message);
    Environment.ExitCode = 1;
}

public sealed class LoginRequest { public string name, password, role, bootstrap; }
