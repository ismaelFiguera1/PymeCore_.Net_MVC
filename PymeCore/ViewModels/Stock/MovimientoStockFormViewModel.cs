using System.ComponentModel.DataAnnotations;
using PymeCore.Models;

namespace PymeCore.ViewModels.Stock
{
    public class MovimientoStockFormViewModel
    {
        public int ProductoId { get; set; }
        public string NombreProducto { get; set; } = string.Empty;
        public int StockActual { get; set; }

        [Required(ErrorMessage = "El tipo es obligatorio")]
        [Display(Name = "Tipo de movimiento")]
        public TipoMovimiento Tipo { get; set; }

        [Required(ErrorMessage = "La cantidad es obligatoria")]
        [Display(Name = "Cantidad")]
        public int Cantidad { get; set; }

        [Display(Name = "Motivo (opcional)")]
        [MaxLength(200, ErrorMessage = "El motivo no puede superar los 200 caracteres")]
        public string? Motivo { get; set; }
    }
}
