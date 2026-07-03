using System.ComponentModel.DataAnnotations;

namespace PymeCore.Models
{
    public class Proveedor
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Nombre { get; set; } = string.Empty;

        [Required]
        [MaxLength(9)]
        [RegularExpression(@"^\d{8}[A-Za-z]$", ErrorMessage = "El CIF debe tener el formato 8 números + 1 letra (ej. 12345678A).")]
        public string Cif { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? PersonaContacto { get; set; }

        [Required]
        [EmailAddress]
        [MaxLength(100)]
        public string Email { get; set; } = string.Empty;

        [MaxLength(9)]
        [RegularExpression(@"^\d{9}$", ErrorMessage = "El teléfono debe tener 9 dígitos numéricos.")]
        public string? Telefono { get; set; }

        public bool Activo { get; set; } = true;
    }
}
