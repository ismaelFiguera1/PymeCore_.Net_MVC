namespace PymeCore.Models
{
    public class FacturaSnapshot
    {
        public int Id { get; set; }

        public int FacturaId { get; set; }
        public Factura Factura { get; set; } = null!;

        public string DatosJson { get; set; } = string.Empty;

        public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

        public int Version { get; set; } = 1;
    }
}
