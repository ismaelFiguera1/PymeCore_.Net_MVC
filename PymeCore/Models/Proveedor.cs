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
        public string Cif { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? PersonaContacto { get; set; }

        [Required]
        [EmailAddress]
        [MaxLength(100)]
        public string Email { get; set; } = string.Empty;

        [MaxLength(15)]
        public string? Telefono { get; set; }

        public bool Activo { get; set; } = true;
    }
}
