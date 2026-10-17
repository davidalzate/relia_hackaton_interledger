namespace Relia.Api.Services.Support;

/// <summary>La base guarda celulares y billeteras completos; al frontend salen enmascarados.</summary>
public static class Masking
{
    /// <summary>3000000001 → "300 *** 0001".</summary>
    public static string Phone(string phone) =>
        phone.Length == 10 ? $"{phone[..3]} *** {phone[^4..]}" : "***";

    /// <summary>$ilp.interledger-test.dev/usuario → "$ilp...test.dev/us***".</summary>
    public static string Wallet(string pointer)
    {
        var slash = pointer.IndexOf('/');
        var host = slash < 0 ? pointer[1..] : pointer[1..slash];
        var path = slash < 0 ? "" : pointer[(slash + 1)..];
        var maskedHost = host.Length <= 11 ? host : $"{host[..3]}...{host[^8..]}";
        var maskedPath = path.Length == 0 ? "" : $"/{path[..Math.Min(2, path.Length)]}***";
        return $"${maskedHost}{maskedPath}";
    }
}
