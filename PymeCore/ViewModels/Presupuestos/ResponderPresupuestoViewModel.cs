namespace PymeCore.ViewModels.Presupuestos
{
    public class ResponderPresupuestoViewModel
    {
        public bool EsValido { get; set; }

        public string Token { get; set; } = string.Empty;
        public string? Numero { get; set; }
        public string? ClienteNombre { get; set; }
        public DateTime Fecha { get; set; }
        public string? Observaciones { get; set; }
        public decimal Total { get; set; }
        public string Decision { get; set; } = string.Empty;

        public List<ResponderPresupuestoLineaViewModel> Lineas { get; set; } = new();
    }

    public class ResponderPresupuestoLineaViewModel
    {
        public string Producto { get; set; } = string.Empty;
        public string? Descripcion { get; set; }
        public int Cantidad { get; set; }
        public decimal PrecioUnitario { get; set; }
        public decimal Subtotal { get; set; }
    }
}
