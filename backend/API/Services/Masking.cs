namespace Relia.Api.Services;

/// <summary>
/// La base guarda celular y billetera completos; toda respuesta al corresponsal o al admin los enmascara aquí.
/// </summary>
public static class Masking
{
    /// <summary>"3000000001" → "300 *** 0001".</summary>
    public static string Phone(string phone) =>
        phone.Length < 7 ? "***" : $"{phone[..3]} *** {phone[^4..]}";

    /// <summary>
    /// "$ilp.interledger-test.dev/usuario" → "$ilp...test.dev/us***": primera etiqueta y últimos 8 caracteres del
    /// dominio, y hasta 2 letras de la ruta (siempre se oculta al menos una).
    /// </summary>
    public static string WalletAddress(string walletAddress)
    {
        var body = walletAddress.TrimStart('$');
        var slash = body.IndexOf('/');
        var host = slash < 0 ? body : body[..slash];
        var path = slash < 0 ? "" : body[(slash + 1)..];

        const int hostTail = 8;
        var dot = host.IndexOf('.');
        var maskedHost = dot > 0 && host.Length - dot - 1 > hostTail
            ? $"{host[..dot]}...{host[^hostTail..]}"
            : host;
        var maskedPath = path.Length == 0 ? "" : $"/{path[..Math.Min(2, path.Length - 1)]}***";
        return $"${maskedHost}{maskedPath}";
    }
}
