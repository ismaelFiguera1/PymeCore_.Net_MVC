using System.ComponentModel.DataAnnotations;

namespace PymeCore.ViewModels.Presupuestos
{
    public class LineaPresupuestoFormViewModel
    {
        public int PresupuestoId { get; set; }

        [Required(ErrorMessage = "El producto es obligatorio")]
        [Display(Name = "Producto")]
        public int? ProductoId { get; set; }

        [MaxLength(200)]
        [Display(Name = "Descripción")]
        public string? Descripcion { get; set; }

        [Required(ErrorMessage = "La cantidad es obligatoria")]
        [Range(1, int.MaxValue, ErrorMessage = "La cantidad debe ser mayor que 0")]
        [Display(Name = "Cantidad")]
        public int Cantidad { get; set; } = 1;

        [Required(ErrorMessage = "El precio es obligatorio")]
        [Range(0.01, double.MaxValue, ErrorMessage = "El precio debe ser mayor que 0")]
        [Display(Name = "Precio unitario")]
        public decimal PrecioUnitario { get; set; }
    }
}
