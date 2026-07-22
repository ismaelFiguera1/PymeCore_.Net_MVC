using System.ComponentModel.DataAnnotations;

namespace PymeCore.ViewModels.Productos
{
    public class ProductoFormViewModel : IValidatableObject
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "El nombre es obligatorio")]
        [MaxLength(100, ErrorMessage = "El nombre no puede superar los 100 caracteres")]
        [Display(Name = "Nombre")]
        public string Nombre { get; set; } = string.Empty;

        [Display(Name = "Referencia / SKU")]
        public string Sku { get; set; } = string.Empty;

        [Required(ErrorMessage = "El precio de coste es obligatorio")]
        [Display(Name = "Precio de coste (€)")]
        [Range(0.01, double.MaxValue, ErrorMessage = "El precio de coste debe ser mayor que cero")]
        public decimal PrecioCoste { get; set; }

        [Required(ErrorMessage = "El precio de venta es obligatorio")]
        [Display(Name = "Precio de venta (€)")]
        [Range(0.01, double.MaxValue, ErrorMessage = "El precio de venta debe ser mayor que cero")]
        public decimal PrecioVenta { get; set; }

        [Required(ErrorMessage = "El stock actual es obligatorio")]
        [Display(Name = "Stock actual")]
        [Range(0, int.MaxValue, ErrorMessage = "El stock no puede ser negativo")]
        public int StockActual { get; set; }

        [Required(ErrorMessage = "El stock mínimo es obligatorio")]
        [Display(Name = "Stock mínimo")]
        [Range(0, int.MaxValue, ErrorMessage = "El stock mínimo no puede ser negativo")]
        public int StockMinimo { get; set; }

        [Required(ErrorMessage = "Debes seleccionar un proveedor")]
        [Display(Name = "Proveedor")]
        public int? ProveedorId { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (PrecioVenta > 0 && PrecioCoste > 0 && PrecioVenta <= PrecioCoste)
                yield return new ValidationResult(
                    "El precio de venta debe ser mayor que el precio de coste.",
                    new[] { nameof(PrecioVenta) });
        }
    }
}
