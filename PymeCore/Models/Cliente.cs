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
        public string Nif { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [MaxLength(50)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [MaxLength(9)]
        public string Telefono { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? Direccion { get; set; }

        [MaxLength(50)]
        public string? Ciudad { get; set; }

        public bool Activo { get; set; } = true;
    }
}
