using System.ComponentModel.DataAnnotations;

namespace PymeCore.Models
{
    public enum TipoMovimiento
    {
        Entrada,
        Salida,
        Ajuste,
        Devolucion
    }

    public class MovimientoStock
    {
        public int Id { get; set; }

        public int ProductoId { get; set; }
        public Producto Producto { get; set; } = null!;

        public TipoMovimiento Tipo { get; set; }

        public int Cantidad { get; set; }

        public DateTime Fecha { get; set; }

        [MaxLength(200)]
        public string? Motivo { get; set; }
    }
}
