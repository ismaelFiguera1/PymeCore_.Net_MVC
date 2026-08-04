using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PymeCore.Data;
using PymeCore.Models;
using PymeCore.Services;
using PymeCore.ViewModels.Clientes;

namespace PymeCore.Controllers
{
    public class ClientesController : Controller
    {
        private readonly ClienteService _clienteService;

        public ClientesController(ClienteService clienteService)
        {
            _clienteService = clienteService;
        }

        public async Task<IActionResult> Index(string? buscar)
        {
            ViewBag.Buscar = buscar;
            var clientes = await _clienteService.GetAllAsync(buscar);
            return View(clientes);
        }

        public async Task<IActionResult> Details(int id)
        {
            var cliente = await _clienteService.GetByIdAsync(id);
            if (cliente is null) return NotFound();
            return View(cliente);
        }

        public IActionResult Create()
        {
            return View(new ClienteFormViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ClienteFormViewModel vm)
        {
            vm.Nombre = vm.Nombre?.Trim() ?? string.Empty;
            vm.Nif = vm.Nif?.Trim().ToUpperInvariant() ?? string.Empty;
            vm.Email = vm.Email?.Trim() ?? string.Empty;
            vm.Direccion = string.IsNullOrWhiteSpace(vm.Direccion) ? null : vm.Direccion.Trim();
            vm.Ciudad = string.IsNullOrWhiteSpace(vm.Ciudad) ? null : vm.Ciudad.Trim();

            if (await _clienteService.ExisteNifAsync(vm.Nif))
                ModelState.AddModelError(nameof(vm.Nif), "Ya existe un cliente con este NIF.");

            if (!ModelState.IsValid) return View(vm);

            var cliente = new Cliente
            {
                Nombre = vm.Nombre,
                Nif = vm.Nif,
                Email = vm.Email,
                Telefono = vm.Telefono,
                Direccion = vm.Direccion,
                Ciudad = vm.Ciudad,
                Activo = true
            };

            var (ok, error) = await _clienteService.CreateAsync(cliente);
            if (!ok)
            {
                ModelState.AddModelError(nameof(vm.Nif), error!);
                return View(vm);
            }

            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Edit(int id)
        {
            var cliente = await _clienteService.GetByIdAsync(id);
            if (cliente is null) return NotFound();

            var vm = new ClienteFormViewModel
            {
                Id = cliente.Id,
                Nombre = cliente.Nombre,
                Nif = cliente.Nif,
                Email = cliente.Email,
                Telefono = cliente.Telefono,
                Direccion = cliente.Direccion,
                Ciudad = cliente.Ciudad
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ClienteFormViewModel vm)
        {
            if (id != vm.Id) return BadRequest();

            vm.Nombre = vm.Nombre?.Trim() ?? string.Empty;
            vm.Nif = vm.Nif?.Trim().ToUpperInvariant() ?? string.Empty;
            vm.Email = vm.Email?.Trim() ?? string.Empty;
            vm.Direccion = string.IsNullOrWhiteSpace(vm.Direccion) ? null : vm.Direccion.Trim();
            vm.Ciudad = string.IsNullOrWhiteSpace(vm.Ciudad) ? null : vm.Ciudad.Trim();

            if (await _clienteService.ExisteNifAsync(vm.Nif, id))
                ModelState.AddModelError(nameof(vm.Nif), "Ya existe un cliente con este NIF.");

            if (!ModelState.IsValid) return View(vm);

            var cliente = await _clienteService.GetByIdAsync(id);
            if (cliente is null) return NotFound();

            cliente.Nombre = vm.Nombre;
            cliente.Nif = vm.Nif;
            cliente.Email = vm.Email;
            cliente.Telefono = vm.Telefono;
            cliente.Direccion = vm.Direccion;
            cliente.Ciudad = vm.Ciudad;

            var (ok, error) = await _clienteService.UpdateAsync(cliente);
            if (!ok)
            {
                ModelState.AddModelError(nameof(vm.Nif), error!);
                return View(vm);
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = AppRoles.Administrador)]
        public async Task<IActionResult> Deactivate(int id)
        {
            var (ok, error) = await _clienteService.DeactivateAsync(id);
            TempData[ok ? "Warning" : "Error"] = ok ? "Cliente desactivado." : error;
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = AppRoles.Administrador)]
        public async Task<IActionResult> Activate(int id)
        {
            var (ok, error) = await _clienteService.ActivateAsync(id);
            TempData[ok ? "Success" : "Error"] = ok ? "Cliente activado." : error;
            return RedirectToAction(nameof(Index));
        }
    }
}
