namespace PymeCore.ViewModels.Usuarios
{
    public class UsuarioListItemViewModel
    {
        public string Id { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string Rol { get; set; } = string.Empty;

        public bool Bloqueado { get; set; }
    }
}
