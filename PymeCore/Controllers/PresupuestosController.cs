using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using PymeCore.Models;
using PymeCore.Services;
using PymeCore.ViewModels.Presupuestos;

namespace PymeCore.Controllers
{
    public class PresupuestosController : Controller
    {
        private readonly PresupuestoService _presupuestoService;
        private readonly ClienteService _clienteService;

        public PresupuestosController(PresupuestoService presupuestoService, ClienteService clienteService)
        {
            _presupuestoService = presupuestoService;
            _clienteService = clienteService;
        }

        public async Task<IActionResult> Index(string? buscar)
        {
            ViewBag.Buscar = buscar;
            var presupuestos = await _presupuestoService.GetAllAsync(buscar);
            return View(presupuestos);
        }

        public async Task<IActionResult> Details(int id)
        {
            var presupuesto = await _presupuestoService.GetByIdAsync(id);
            if (presupuesto is null) return NotFound();
            return View(presupuesto);
        }

        public async Task<IActionResult> Create()
        {
            await CargarClientesAsync();
            return View(new PresupuestoFormViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(PresupuestoFormViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                await CargarClientesAsync();
                return View(vm);
            }

            var presupuesto = new Presupuesto
            {
                Numero        = await _presupuestoService.GenerarNumeroAsync(),
                ClienteId     = vm.ClienteId!.Value,
                Fecha         = DateTime.UtcNow,
                Estado        = vm.Estado,
                Observaciones = vm.Observaciones,
                Total         = 0
            };

            await _presupuestoService.CreateAsync(presupuesto);
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Edit(int id)
        {
            var presupuesto = await _presupuestoService.GetByIdAsync(id);
            if (presupuesto is null) return NotFound();

            var vm = new PresupuestoFormViewModel
            {
                Id            = presupuesto.Id,
                Numero        = presupuesto.Numero,
                ClienteId     = presupuesto.ClienteId,
                Estado        = presupuesto.Estado,
                Observaciones = presupuesto.Observaciones
            };

            await CargarClientesAsync();
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, PresupuestoFormViewModel vm)
        {
            if (id != vm.Id) return BadRequest();

            if (!ModelState.IsValid)
            {
                await CargarClientesAsync();
                return View(vm);
            }

            var presupuesto = await _presupuestoService.GetByIdAsync(id);
            if (presupuesto is null) return NotFound();

            presupuesto.ClienteId     = vm.ClienteId!.Value;
            presupuesto.Estado        = vm.Estado;
            presupuesto.Observaciones = vm.Observaciones;

            await _presupuestoService.UpdateAsync(presupuesto);
            return RedirectToAction(nameof(Index));
        }

        private async Task CargarClientesAsync()
        {
            var clientes = (await _clienteService.GetAllAsync())
                .Where(c => c.Activo)
                .ToList();
            ViewBag.Clientes = new SelectList(clientes, "Id", "Nombre");
        }
    }
}
