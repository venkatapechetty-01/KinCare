namespace KinCare.API.Infrastructure;

public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["X-XSS-Protection"] = "0";
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains; preload";

        // The public driver tracking page (/track/{token}) is the one surface in this API
        // that serves interactive HTML with inline <script> and needs live GPS — the
        // default locked-down policy below (script-src 'self', geolocation=()) silently
        // blocked every one-tap status button and all GPS capture on that page. Scope a
        // relaxed policy to just that route instead of weakening security everywhere else.
        if (context.Request.Path.StartsWithSegments("/track"))
        {
            // Stored in HttpContext.Items (not a field) — this middleware instance is a
            // singleton shared across every concurrent request, so per-request state can
            // never live on the instance itself. The endpoint reads this same key to embed
            // the identical nonce into the <script nonce="..."> tag it renders.
            var nonce = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
            context.Items["CspNonce"] = nonce;
            context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(self)";
            context.Response.Headers["Content-Security-Policy"] =
                $"default-src 'self'; script-src 'self' 'nonce-{nonce}'; style-src 'self' 'unsafe-inline'; img-src 'self' data: https:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
        }
        else
        {
            context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
        }

        await _next(context);
    }
}
