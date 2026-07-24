using System.ComponentModel.DataAnnotations;

namespace PymeCore.ViewModels.Presupuestos
{
    public class PresupuestoFormViewModel
    {
        public int Id { get; set; }

        [Display(Name = "Número")]
        public string? Numero { get; set; }

        [Required(ErrorMessage = "El cliente es obligatorio")]
        [Display(Name = "Cliente")]
        public int? ClienteId { get; set; }

        [MaxLength(500, ErrorMessage = "Las observaciones no pueden superar los 500 caracteres")]
        [Display(Name = "Observaciones")]
        public string? Observaciones { get; set; }
    }
}
