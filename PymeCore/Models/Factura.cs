using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace PymeCore.Models
{
    public enum EstadoFactura
    {
        Pendiente,
        Pagada,
        Anulada
    }

    public class Factura
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(20)]
        public string Numero { get; set; } = string.Empty;

        [Required]
        public int ClienteId { get; set; }
        public Cliente? Cliente { get; set; }

        [Required]
        public int PedidoId { get; set; }
        public Pedido? Pedido { get; set; }

        public DateTime FechaEmision { get; set; } = DateTime.UtcNow;

        public EstadoFactura Estado { get; set; } = EstadoFactura.Pendiente;

        [Column(TypeName = "decimal(10,2)")]
        public decimal BaseImponible { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal PorcentajeIVA { get; set; } = 21m;

        [Column(TypeName = "decimal(10,2)")]
        public decimal TotalIVA { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal Total { get; set; }

        public string? CreadoPorUsuarioId { get; set; }
        public IdentityUser? CreadoPorUsuario { get; set; }
    }
}
