using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PymeCore.Models
{
    public enum EstadoPedido
    {
        Pendiente,
        EnPreparacion,
        Completado,
        Cancelado
    }

    public class Pedido
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(20)]
        public string Numero { get; set; } = string.Empty;

        [Required]
        public int ClienteId { get; set; }
        public Cliente? Cliente { get; set; }

        public int PresupuestoOrigenId { get; set; }
        public Presupuesto? PresupuestoOrigen { get; set; }

        public DateTime Fecha { get; set; } = DateTime.UtcNow;

        public EstadoPedido Estado { get; set; } = EstadoPedido.Pendiente;

        [MaxLength(500)]
        public string? Observaciones { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal Total { get; set; }

        public ICollection<LineaPedido> Lineas { get; set; } = new List<LineaPedido>();
    }
}
