namespace AdvancedDispatcherSystem.Host;

public static class SessionCookie
{
    // Cookies are shared across ports, and a previous Secure cookie can prevent
    // HTTP from replacing the same name. Keep each transport/Host port separate.
    public static string Name(HttpContext context)
        => (context.Request.IsHttps ? "__Host-ads_session_" : "ads_session_") +
            (context.Request.Host.Port ?? context.Connection.LocalPort);

    public static string Read(HttpContext context) => context.Request.Cookies[Name(context)];
}
