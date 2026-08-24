using System.ComponentModel.DataAnnotations;

namespace PymeCore.ViewModels.Proveedores
{
    public class ProveedorFormViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "El nombre es obligatorio")]
        [MaxLength(100, ErrorMessage = "El nombre no puede superar los 100 caracteres")]
        [Display(Name = "Nombre")]
        public string Nombre { get; set; } = string.Empty;

        [Required(ErrorMessage = "El CIF es obligatorio")]
        [MaxLength(9, ErrorMessage = "El CIF no puede superar los 9 caracteres")]
        [RegularExpression(@"^\d{8}[A-Za-z]$", ErrorMessage = "El CIF debe tener el formato 8 números + 1 letra (ej. 12345678A).")]
        [Display(Name = "CIF")]
        public string Cif { get; set; } = string.Empty;

        [MaxLength(100, ErrorMessage = "La persona de contacto no puede superar los 100 caracteres")]
        [Display(Name = "Persona de contacto")]
        public string? PersonaContacto { get; set; }

        [Required(ErrorMessage = "El email es obligatorio")]
        [EmailAddress(ErrorMessage = "Formato de email no válido")]
        [MaxLength(100, ErrorMessage = "El email no puede superar los 100 caracteres")]
        [RegularExpression(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", ErrorMessage = "Introduce un correo completo, por ejemplo usuario@empresa.com")]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [MaxLength(9, ErrorMessage = "El teléfono no puede superar los 9 caracteres")]
        [RegularExpression(@"^\d{9}$", ErrorMessage = "El teléfono debe tener 9 dígitos numéricos.")]
        [Display(Name = "Teléfono")]
        public string? Telefono { get; set; }
    }
}
