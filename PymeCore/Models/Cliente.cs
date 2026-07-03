using System.ComponentModel.DataAnnotations;

namespace PymeCore.Models
{
    public class Cliente
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string Nombre { get; set; } = string.Empty;

        [Required]
        [MaxLength(9)]
        [RegularExpression(@"^\d{8}[A-Za-z]$", ErrorMessage = "El NIF debe tener el formato 8 números + 1 letra (ej. 12345678A).")]
        public string Nif { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [MaxLength(50)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [MaxLength(9)]
        [RegularExpression(@"^\d{9}$", ErrorMessage = "El teléfono debe tener 9 dígitos numéricos.")]
        public string Telefono { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? Direccion { get; set; }

        [MaxLength(50)]
        public string? Ciudad { get; set; }

        public bool Activo { get; set; } = true;
    }
}
