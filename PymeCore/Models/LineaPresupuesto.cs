using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PymeCore.Models
{
    public class LineaPresupuesto
    {
        public int Id { get; set; }

        [Required]
        public int PresupuestoId { get; set; }
        public Presupuesto? Presupuesto { get; set; }

        [Required]
        public int ProductoId { get; set; }
        public Producto? Producto { get; set; }

        [MaxLength(200)]
        public string? Descripcion { get; set; }

        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "La cantidad debe ser mayor que 0")]
        public int Cantidad { get; set; }

        [Required]
        [Column(TypeName = "decimal(10,2)")]
        public decimal PrecioUnitario { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal Subtotal { get; set; }
    }
}
