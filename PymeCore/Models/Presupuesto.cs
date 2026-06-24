using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PymeCore.Models
{
    public enum EstadoPresupuesto
    {
        Borrador,
        Enviado,
        Aceptado,
        Rechazado
    }

    public class Presupuesto
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(20)]
        public string Numero { get; set; } = string.Empty;

        [Required]
        public int ClienteId { get; set; }
        public Cliente? Cliente { get; set; }

        public DateTime Fecha { get; set; } = DateTime.UtcNow;

        public EstadoPresupuesto Estado { get; set; } = EstadoPresupuesto.Borrador;

        [MaxLength(500)]
        public string? Observaciones { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal Total { get; set; }
    }
}
