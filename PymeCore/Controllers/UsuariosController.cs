using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PymeCore.Data;
using PymeCore.ViewModels.Usuarios;

namespace PymeCore.Controllers
{
    [Authorize(Roles = AppRoles.Administrador)]
    public class UsuariosController : Controller
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IConfiguration _configuration;
        private readonly ILogger<UsuariosController> _logger;

        public UsuariosController(
            UserManager<IdentityUser> userManager,
            IConfiguration configuration,
            ILogger<UsuariosController> logger)
        {
            _userManager = userManager;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            var usuarios = await _userManager.Users.ToListAsync();
            var items = new List<UsuarioListItemViewModel>();

            foreach (var usuario in usuarios)
            {
                var roles = await _userManager.GetRolesAsync(usuario);

                items.Add(new UsuarioListItemViewModel
                {
                    Id = usuario.Id,
                    Email = usuario.Email ?? string.Empty,
                    Rol = roles.FirstOrDefault() ?? "(sin rol)",
                    Bloqueado = await _userManager.IsLockedOutAsync(usuario)
                });
            }

            ViewData["UsuarioActualId"] = _userManager.GetUserId(User);
            ViewData["AdminInicialEmail"] = _configuration["SeedAdmin:Email"];

            return View(items);
        }

        public async Task<IActionResult> Edit(string id)
        {
            var usuario = await _userManager.FindByIdAsync(id);
            if (usuario is null) return NotFound();

            var bloqueo = ValidarEdicionPermitida(usuario);
            if (bloqueo is not null)
            {
                TempData["Error"] = bloqueo;
                return RedirectToAction(nameof(Index));
            }

            var roles = await _userManager.GetRolesAsync(usuario);

            var vm = new UsuarioEditViewModel
            {
                Id = usuario.Id,
                Email = usuario.Email ?? string.Empty,
                Rol = roles.FirstOrDefault() ?? string.Empty
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string id, UsuarioEditViewModel vm)
        {
            if (id != vm.Id) return BadRequest();

            var usuario = await _userManager.FindByIdAsync(id);
            if (usuario is null) return NotFound();

            var bloqueo = ValidarEdicionPermitida(usuario);
            if (bloqueo is not null)
            {
                TempData["Error"] = bloqueo;
                return RedirectToAction(nameof(Index));
            }

            if (vm.Rol != AppRoles.Administrador && vm.Rol != AppRoles.Usuario)
            {
                ModelState.AddModelError(nameof(vm.Rol), "El rol seleccionado no es válido.");
            }

            if (!ModelState.IsValid)
            {
                vm.Email = usuario.Email ?? string.Empty;
                return View(vm);
            }

            var rolesActuales = await _userManager.GetRolesAsync(usuario);

            if (rolesActuales.Count == 1 && rolesActuales[0] == vm.Rol)
            {
                TempData["Warning"] = $"El usuario ya tenía el rol '{vm.Rol}'.";
                return RedirectToAction(nameof(Index));
            }

            if (rolesActuales.Count > 0)
            {
                var resultadoEliminar = await _userManager.RemoveFromRolesAsync(usuario, rolesActuales);
                if (!resultadoEliminar.Succeeded)
                {
                    var errores = string.Join("; ", resultadoEliminar.Errors.Select(e => e.Description));
                    _logger.LogError("No se pudieron quitar los roles actuales de '{Email}': {Errores}", usuario.Email, errores);
                    ModelState.AddModelError(string.Empty, "No se ha podido actualizar el rol. Inténtalo de nuevo.");
                    vm.Email = usuario.Email ?? string.Empty;
                    return View(vm);
                }
            }

            var resultadoAsignar = await _userManager.AddToRoleAsync(usuario, vm.Rol);
            if (!resultadoAsignar.Succeeded)
            {
                var erroresAsignar = string.Join("; ", resultadoAsignar.Errors.Select(e => e.Description));
                _logger.LogError("No se pudo asignar el rol '{Rol}' a '{Email}': {Errores}", vm.Rol, usuario.Email, erroresAsignar);

                if (rolesActuales.Count > 0)
                {
                    var resultadoRestaurar = await _userManager.AddToRolesAsync(usuario, rolesActuales);
                    if (!resultadoRestaurar.Succeeded)
                    {
                        var erroresRestaurar = string.Join("; ", resultadoRestaurar.Errors.Select(e => e.Description));
                        _logger.LogCritical(
                            "No se pudo restaurar el rol anterior de '{Email}' tras fallar la asignación del nuevo rol. El usuario ha quedado sin rol: {Errores}",
                            usuario.Email, erroresRestaurar);
                    }
                }

                ModelState.AddModelError(string.Empty, "No se ha podido actualizar el rol. Inténtalo de nuevo.");
                vm.Email = usuario.Email ?? string.Empty;
                return View(vm);
            }

            TempData["Success"] = "Rol actualizado correctamente.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Deactivate(string id)
        {
            var usuario = await _userManager.FindByIdAsync(id);
            if (usuario is null) return NotFound();

            var bloqueo = ValidarEdicionPermitida(usuario);
            if (bloqueo is not null)
            {
                TempData["Error"] = bloqueo;
                return RedirectToAction(nameof(Index));
            }

            var resultadoLockout = await _userManager.SetLockoutEndDateAsync(usuario, DateTimeOffset.MaxValue);
            if (!resultadoLockout.Succeeded)
            {
                var errores = string.Join("; ", resultadoLockout.Errors.Select(e => e.Description));
                _logger.LogError("No se pudo desactivar la cuenta '{Email}': {Errores}", usuario.Email, errores);
                TempData["Error"] = "No se ha podido desactivar la cuenta.";
                return RedirectToAction(nameof(Index));
            }

            var resultadoStamp = await _userManager.UpdateSecurityStampAsync(usuario);
            if (!resultadoStamp.Succeeded)
            {
                var erroresStamp = string.Join("; ", resultadoStamp.Errors.Select(e => e.Description));
                _logger.LogError(
                    "La cuenta '{Email}' quedó bloqueada pero no se pudo invalidar su sesión activa: {Errores}",
                    usuario.Email, erroresStamp);
                TempData["Warning"] = "Cuenta desactivada: no podrá volver a iniciar sesión, pero si tenía una sesión abierta podría tardar en cerrarse.";
                return RedirectToAction(nameof(Index));
            }

            TempData["Success"] = "Cuenta desactivada correctamente.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Activate(string id)
        {
            var usuario = await _userManager.FindByIdAsync(id);
            if (usuario is null) return NotFound();

            var resultado = await _userManager.SetLockoutEndDateAsync(usuario, null);
            if (!resultado.Succeeded)
            {
                var errores = string.Join("; ", resultado.Errors.Select(e => e.Description));
                _logger.LogError("No se pudo reactivar la cuenta '{Email}': {Errores}", usuario.Email, errores);
                TempData["Error"] = "No se ha podido reactivar la cuenta.";
                return RedirectToAction(nameof(Index));
            }

            TempData["Success"] = "Cuenta reactivada correctamente.";
            return RedirectToAction(nameof(Index));
        }

        private string? ValidarEdicionPermitida(IdentityUser usuario)
        {
            var usuarioActualId = _userManager.GetUserId(User);
            if (usuario.Id == usuarioActualId)
            {
                return "No puedes realizar esta acción sobre tu propia cuenta.";
            }

            var adminInicialEmail = _configuration["SeedAdmin:Email"];
            if (!string.IsNullOrWhiteSpace(adminInicialEmail) &&
                string.Equals(usuario.Email, adminInicialEmail, StringComparison.OrdinalIgnoreCase))
            {
                return "No se puede realizar esta acción sobre la cuenta del administrador inicial.";
            }

            return null;
        }
    }
}
