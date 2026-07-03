using Microsoft.AspNetCore.Mvc;
using PymeCore.Models;
using PymeCore.Services;
using PymeCore.ViewModels.Proveedores;

namespace PymeCore.Controllers
{
    public class ProveedoresController : Controller
    {
        private readonly ProveedorService _proveedorService;

        public ProveedoresController(ProveedorService proveedorService)
        {
            _proveedorService = proveedorService;
        }

        public async Task<IActionResult> Index(string? buscar)
        {
            ViewBag.Buscar = buscar;
            var proveedores = await _proveedorService.GetAllAsync(buscar);
            return View(proveedores);
        }

        public async Task<IActionResult> Details(int id)
        {
            var proveedor = await _proveedorService.GetByIdAsync(id);
            if (proveedor is null) return NotFound();
            return View(proveedor);
        }

        public IActionResult Create()
        {
            return View(new ProveedorFormViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ProveedorFormViewModel vm)
        {
            vm.Nombre = vm.Nombre?.Trim() ?? string.Empty;
            vm.Cif = vm.Cif?.Trim().ToUpperInvariant() ?? string.Empty;
            vm.PersonaContacto = string.IsNullOrWhiteSpace(vm.PersonaContacto) ? null : vm.PersonaContacto.Trim();
            vm.Email = vm.Email?.Trim() ?? string.Empty;

            if (await _proveedorService.ExisteCifAsync(vm.Cif))
                ModelState.AddModelError(nameof(vm.Cif), "Ya existe un proveedor con este CIF.");

            if (!ModelState.IsValid) return View(vm);

            var proveedor = new Proveedor
            {
                Nombre          = vm.Nombre,
                Cif             = vm.Cif,
                PersonaContacto = vm.PersonaContacto,
                Email           = vm.Email,
                Telefono        = vm.Telefono,
                Activo          = true
            };

            var (ok, error) = await _proveedorService.CreateAsync(proveedor);
            if (!ok)
            {
                ModelState.AddModelError(nameof(vm.Cif), error!);
                return View(vm);
            }

            TempData["Success"] = "Proveedor creado.";
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Edit(int id)
        {
            var proveedor = await _proveedorService.GetByIdAsync(id);
            if (proveedor is null) return NotFound();

            var vm = new ProveedorFormViewModel
            {
                Id              = proveedor.Id,
                Nombre          = proveedor.Nombre,
                Cif             = proveedor.Cif,
                PersonaContacto = proveedor.PersonaContacto,
                Email           = proveedor.Email,
                Telefono        = proveedor.Telefono
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ProveedorFormViewModel vm)
        {
            if (id != vm.Id) return BadRequest();

            vm.Nombre = vm.Nombre?.Trim() ?? string.Empty;
            vm.Cif = vm.Cif?.Trim().ToUpperInvariant() ?? string.Empty;
            vm.PersonaContacto = string.IsNullOrWhiteSpace(vm.PersonaContacto) ? null : vm.PersonaContacto.Trim();
            vm.Email = vm.Email?.Trim() ?? string.Empty;

            if (await _proveedorService.ExisteCifAsync(vm.Cif, id))
                ModelState.AddModelError(nameof(vm.Cif), "Ya existe un proveedor con este CIF.");

            if (!ModelState.IsValid) return View(vm);

            var proveedor = await _proveedorService.GetByIdAsync(id);
            if (proveedor is null) return NotFound();

            proveedor.Nombre          = vm.Nombre;
            proveedor.Cif             = vm.Cif;
            proveedor.PersonaContacto = vm.PersonaContacto;
            proveedor.Email           = vm.Email;
            proveedor.Telefono        = vm.Telefono;

            var (ok, error) = await _proveedorService.UpdateAsync(proveedor);
            if (!ok)
            {
                ModelState.AddModelError(nameof(vm.Cif), error!);
                return View(vm);
            }

            TempData["Success"] = "Proveedor actualizado.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Deactivate(int id)
        {
            var (ok, error) = await _proveedorService.DeactivateAsync(id);
            TempData[ok ? "Warning" : "Error"] = ok ? "Proveedor desactivado." : error;
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Activate(int id)
        {
            var (ok, error) = await _proveedorService.ActivateAsync(id);
            TempData[ok ? "Success" : "Error"] = ok ? "Proveedor activado." : error;
            return RedirectToAction(nameof(Index));
        }
    }
}
