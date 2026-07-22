using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PymeCore.Models
{
    public class Producto
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Nombre { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Sku { get; set; } = string.Empty;

        [Column(TypeName = "decimal(10,2)")]
        public decimal PrecioCoste { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal PrecioVenta { get; set; }

        public int StockActual { get; set; }

        public int StockMinimo { get; set; }

        [Required]
        public int ProveedorId { get; set; }
        public Proveedor Proveedor { get; set; } = null!;

        public bool BajoStock => StockActual < StockMinimo;
    }
}
