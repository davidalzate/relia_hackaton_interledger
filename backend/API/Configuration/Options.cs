namespace Relia.Api.Configuration;

/// <summary>Sección "Jwt". La llave de firma va fuera del repo fuera de Development.</summary>
public class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public string SigningKey { get; set; } = "";
    public int ExpiresInSeconds { get; set; } = 28800;
}

/// <summary>Sección "RailProxy": cómo hablar con el proxy de riel (contracts/rail-proxy.openapi.yaml).</summary>
public class RailProxyOptions
{
    public const string Section = "RailProxy";

    public string BaseUrl { get; set; } = "http://localhost:4000";

    /// <summary>Secreto compartido con el proxy (X-Api-Key). Por user-secrets o variable de entorno.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Tiempo máximo de cada llamada; pasado este plazo el riel se trata como no disponible.</summary>
    public int TimeoutSeconds { get; set; } = 10;
}

/// <summary>Sección "Relia" más el flag DEMO_MODE.</summary>
public class ReliaOptions
{
    public const string Section = "Relia";

    /// <summary>Payment pointer de la billetera de Relia: el mismo RELIA_WALLET_ADDRESS del proxy.</summary>
    public string WalletAddress { get; set; } = "";

    /// <summary>URL base de la página pública (enlace de la invitación por WhatsApp).</summary>
    public string PublicBaseUrl { get; set; } = "";

    /// <summary>Cada cuánto corre el barrido de fondo de operaciones (y liquidaciones).</summary>
    public int SweepIntervalSeconds { get; set; } = 15;

    /// <summary>Peticiones por minuto y por IP a /key-links (rate limit en memoria).</summary>
    public int KeyLinkRateLimitPerMinute { get; set; } = 20;

    /// <summary>Variable DEMO_MODE: sin ella las rutas /demo/* responden 404.</summary>
    public bool DemoMode { get; set; }
}
