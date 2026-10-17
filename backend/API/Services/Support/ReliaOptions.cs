namespace Relia.Api.Services.Support;

/// <summary>Configuración de negocio (sección "Relia").</summary>
public class ReliaOptions
{
    public const string Section = "Relia";

    /// <summary>Billetera de Relia: origen de depósitos y liquidaciones, destino de retiros y cargas.</summary>
    public string WalletAddress { get; set; } = "$ilp.interledger-test.dev/relia-test1";

    /// <summary>Base del enlace de la página pública de vinculación (invitación por WhatsApp).</summary>
    public string PublicLinkBaseUrl { get; set; } = "relia.com";

    /// <summary>Dominio de las billeteras simuladas del demo: su liquidación se paga sin riel.</summary>
    public string SimulatedWalletPrefix { get; set; } = "$demo.relia.local/";
}

public class JwtOptions
{
    public const string Section = "Jwt";
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public string SigningKey { get; set; } = "";
    public int ExpiresInSeconds { get; set; } = 28800;
}
