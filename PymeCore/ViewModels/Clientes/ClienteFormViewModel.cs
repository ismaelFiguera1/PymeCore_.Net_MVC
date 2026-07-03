using System.ComponentModel.DataAnnotations;

namespace PymeCore.ViewModels.Clientes
{
    public class ClienteFormViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "El nombre es obligatorio")]
        [MaxLength(50, ErrorMessage = "El nombre no puede superar los 50 caracteres")]
        [Display(Name = "Nombre")]
        public string Nombre { get; set; } = string.Empty;

        [Required(ErrorMessage = "El NIF es obligatorio")]
        [MaxLength(9, ErrorMessage = "El NIF no puede superar los 9 caracteres")]
        [RegularExpression(@"^\d{8}[A-Za-z]$", ErrorMessage = "El NIF debe tener el formato 8 números + 1 letra (ej. 12345678A).")]
        [Display(Name = "NIF")]
        public string Nif { get; set; } = string.Empty;

        [Required(ErrorMessage = "El email es obligatorio")]
        [EmailAddress(ErrorMessage = "Formato de email no válido")]
        [MaxLength(50, ErrorMessage = "El email no puede superar los 50 caracteres")]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "El teléfono es obligatorio")]
        [MaxLength(9, ErrorMessage = "El teléfono no puede superar los 9 caracteres")]
        [RegularExpression(@"^\d{9}$", ErrorMessage = "El teléfono debe tener 9 dígitos numéricos.")]
        [Display(Name = "Teléfono")]
        public string Telefono { get; set; } = string.Empty;

        [MaxLength(200, ErrorMessage = "La dirección no puede superar los 200 caracteres")]
        [Display(Name = "Dirección")]
        public string? Direccion { get; set; }

        [MaxLength(50, ErrorMessage = "La ciudad no puede superar los 50 caracteres")]
        [Display(Name = "Ciudad")]
        public string? Ciudad { get; set; }
    }
}
