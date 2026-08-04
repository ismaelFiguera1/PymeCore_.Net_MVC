using System.ComponentModel.DataAnnotations;

namespace PymeCore.ViewModels.Usuarios
{
    public class UsuarioEditViewModel
    {
        public string Id { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "El rol es obligatorio.")]
        [Display(Name = "Rol")]
        public string Rol { get; set; } = string.Empty;
    }
}
