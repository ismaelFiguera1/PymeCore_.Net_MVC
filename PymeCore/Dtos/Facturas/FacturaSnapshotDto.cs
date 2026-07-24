namespace PymeCore.Dtos.Facturas
{
    public class FacturaSnapshotDto
    {
        public string NumeroFactura { get; set; } = string.Empty;

        public DateTime FechaEmision { get; set; }

        public EmpresaSnapshotDto Empresa { get; set; } = new();

        public ClienteSnapshotDto Cliente { get; set; } = new();

        public List<FacturaLineaSnapshotDto> Lineas { get; set; } = [];

        public decimal BaseImponible { get; set; }

        public decimal PorcentajeIVA { get; set; }

        public decimal TotalIVA { get; set; }

        public decimal Total { get; set; }
    }
}
