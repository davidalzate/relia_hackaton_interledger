namespace Relia.Api.Services.Demo;

/// <summary>
/// Datos que siembra POST /demo/reset (sección "Demo:Seed"). Viven en configuración, no en el código:
/// nombres, correos, celulares, billeteras y contraseñas de personas se cargan por ambiente.
/// </summary>
public class DemoSeedOptions
{
    public const string Section = "Demo:Seed";

    public FeeSeed Fee { get; set; } = new();
    public List<AdminSeed> Admins { get; set; } = [];
    public List<AgentSeed> Agents { get; set; } = [];
    public List<WalletKeySeed> WalletKeys { get; set; } = [];

    public class FeeSeed
    {
        public long Total { get; set; }
        public long Relia { get; set; }
        public long Agent { get; set; }
    }

    public class AdminSeed
    {
        public string Email { get; set; } = "";
        public string Name { get; set; } = "";
        public string Password { get; set; } = "";
    }

    public class AgentSeed
    {
        public string Email { get; set; } = "";
        public string Name { get; set; } = "";
        public string ContactPerson { get; set; } = "";
        public string City { get; set; } = "";
        public string Address { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Password { get; set; } = "";

        /// <summary>Billetera del corresponsal. Con el prefijo Relia:SimulatedWalletPrefix su liquidación no pasa por el riel.</summary>
        public string PaymentPointer { get; set; } = "";

        public long Quota { get; set; }
        public long Cash { get; set; }
        public long AccruedCommission { get; set; }
        public long SettledCommissionTotal { get; set; }

        /// <summary>Siembra un historial de ejemplo: una carga Bre-B completada y una asignación de efectivo.</summary>
        public bool SampleHistory { get; set; }
    }

    public class WalletKeySeed
    {
        public string Mobile { get; set; } = "";
        public string PaymentPointer { get; set; } = "";
        public string WalletAddressUrl { get; set; } = "";
    }
}
