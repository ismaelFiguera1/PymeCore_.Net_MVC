using PymeCore.Models;

namespace PymeCore.ViewModels
{
    public class DashboardViewModel
    {
        public int ClientesActivos { get; set; }

        public int ProductosBajoStockTotal { get; set; }
        public List<Producto> ProductosBajoStock { get; set; } = new();

        public int PresupuestosEnviadosTotal { get; set; }

        public int FacturasPendientesTotal { get; set; }
        public decimal FacturasPendientesImporteTotal { get; set; }
        public List<Factura> FacturasPendientes { get; set; } = new();

        public List<Pedido> UltimosPedidos { get; set; } = new();
    }
}
