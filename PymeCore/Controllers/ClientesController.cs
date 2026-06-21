using Microsoft.AspNetCore.Mvc;
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

        public async Task<IActionResult> Index()
        {
            var clientes = await _clienteService.GetAllAsync();
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
            if (!ModelState.IsValid) return View(vm);

            var cliente = new Cliente
            {
                Nombre    = vm.Nombre,
                Nif    = vm.Nif,
                Email     = vm.Email,
                Telefono  = vm.Telefono,
                Direccion = vm.Direccion,
                Ciudad    = vm.Ciudad,
                Activo    = true
            };

            await _clienteService.CreateAsync(cliente);
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Edit(int id)
        {
            var cliente = await _clienteService.GetByIdAsync(id);
            if (cliente is null) return NotFound();

            var vm = new ClienteFormViewModel
            {
                Id        = cliente.Id,
                Nombre    = cliente.Nombre,
                Nif    = cliente.Nif,
                Email     = cliente.Email,
                Telefono  = cliente.Telefono,
                Direccion = cliente.Direccion,
                Ciudad    = cliente.Ciudad
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ClienteFormViewModel vm)
        {
            if (id != vm.Id) return BadRequest();
            if (!ModelState.IsValid) return View(vm);

            var cliente = await _clienteService.GetByIdAsync(id);
            if (cliente is null) return NotFound();

            cliente.Nombre    = vm.Nombre;
            cliente.Nif    = vm.Nif;
            cliente.Email     = vm.Email;
            cliente.Telefono  = vm.Telefono;
            cliente.Direccion = vm.Direccion;
            cliente.Ciudad    = vm.Ciudad;

            await _clienteService.UpdateAsync(cliente);
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Deactivate(int id)
        {
            await _clienteService.DeactivateAsync(id);
            return RedirectToAction(nameof(Index));
        }
    }
}
