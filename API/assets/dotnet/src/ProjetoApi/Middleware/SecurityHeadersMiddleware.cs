namespace ProjetoApi.Middleware;

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // OnStarting: os headers são definidos na hora de enviar a resposta. O tratador de exceções do ASP.NET limpa
        // os headers (Response.Clear) ao montar o erro; definidos antes do next, eles sumiam de todo erro vindo de
        // exceção (400, 413, 422, 500, 504...), inclusive o X-Request-ID, que o suporte usa para achar o erro.
        context.Response.OnStarting(
            static estado =>
            {
                var contexto = (HttpContext)estado;
                var headers = contexto.Response.Headers;
                headers["X-Content-Type-Options"] = "nosniff";
                headers["X-Frame-Options"] = "DENY";
                headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
                headers["X-Request-ID"] = contexto.TraceIdentifier;
                return Task.CompletedTask;
            },
            context);
        return next(context);
    }
}
