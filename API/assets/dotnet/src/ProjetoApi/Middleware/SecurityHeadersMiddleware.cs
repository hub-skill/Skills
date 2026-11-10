namespace ProjetoApi.Middleware;

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
        return next(context);
    }
}
