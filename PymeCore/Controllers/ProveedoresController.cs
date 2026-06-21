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

            await _proveedorService.CreateAsync(proveedor);
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
            if (!ModelState.IsValid) return View(vm);

            var proveedor = await _proveedorService.GetByIdAsync(id);
            if (proveedor is null) return NotFound();

            proveedor.Nombre          = vm.Nombre;
            proveedor.Cif             = vm.Cif;
            proveedor.PersonaContacto = vm.PersonaContacto;
            proveedor.Email           = vm.Email;
            proveedor.Telefono        = vm.Telefono;

            await _proveedorService.UpdateAsync(proveedor);
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Deactivate(int id)
        {
            await _proveedorService.DeactivateAsync(id);
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Activate(int id)
        {
            await _proveedorService.ActivateAsync(id);
            return RedirectToAction(nameof(Index));
        }
    }
}
