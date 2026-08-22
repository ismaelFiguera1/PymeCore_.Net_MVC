using Microsoft.AspNetCore.Authorization;
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
        private readonly ProductoService _productoService;

        public PresupuestosController(PresupuestoService presupuestoService, ClienteService clienteService, ProductoService productoService)
        {
            _presupuestoService = presupuestoService;
            _clienteService = clienteService;
            _productoService = productoService;
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

            await CargarProductosAsync();
            ViewBag.LineaVm = new LineaPresupuestoFormViewModel { PresupuestoId = id };
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
                ClienteId = vm.ClienteId!.Value,
                Fecha = DateTime.UtcNow,
                Estado = EstadoPresupuesto.Borrador,
                Observaciones = vm.Observaciones,
                Total = 0
            };

            await _presupuestoService.CreateAsync(presupuesto);
            return RedirectToAction(nameof(Details), new { id = presupuesto.Id });
        }

        public async Task<IActionResult> Edit(int id)
        {
            var presupuesto = await _presupuestoService.GetByIdAsync(id);
            if (presupuesto is null) return NotFound();

            if (presupuesto.Estado != EstadoPresupuesto.Borrador)
            {
                TempData["Error"] = $"Un presupuesto {presupuesto.Estado} no puede editarse.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var vm = new PresupuestoFormViewModel
            {
                Id = presupuesto.Id,
                Numero = presupuesto.Numero,
                ClienteId = presupuesto.ClienteId,
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

            if (presupuesto.Estado != EstadoPresupuesto.Borrador)
                return BadRequest();

            presupuesto.ClienteId = vm.ClienteId!.Value;
            presupuesto.Observaciones = vm.Observaciones;

            await _presupuestoService.UpdateAsync(presupuesto);
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Enviar(int id)
        {
            var (ok, error) = await _presupuestoService.EnviarAsync(id);
            if (ok) TempData["Success"] = "Presupuesto enviado al email del cliente.";
            else TempData["Error"] = error;
            return RedirectToAction(nameof(Details), new { id });
        }

        // Página pública (sin login) a la que llega el cliente desde los enlaces del correo.
        // Solo muestra la confirmación; en esta fase no cambia ningún dato.
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Responder(string? token, string? decision)
        {
            if (decision != "aceptar" && decision != "rechazar")
                return View(new ResponderPresupuestoViewModel { EsValido = false });

            var presupuesto = await _presupuestoService.ValidarTokenRespuestaAsync(token);
            if (presupuesto is null)
                return View(new ResponderPresupuestoViewModel { EsValido = false });

            var vm = new ResponderPresupuestoViewModel
            {
                EsValido = true,
                Token = token!,
                Numero = presupuesto.Numero,
                ClienteNombre = presupuesto.Cliente?.Nombre,
                Fecha = presupuesto.Fecha,
                Observaciones = presupuesto.Observaciones,
                Total = presupuesto.Total,
                Decision = decision,
                Lineas = presupuesto.Lineas.Select(l => new ResponderPresupuestoLineaViewModel
                {
                    Producto = l.Producto?.Nombre ?? string.Empty,
                    Descripcion = l.Descripcion,
                    Cantidad = l.Cantidad,
                    PrecioUnitario = l.PrecioUnitario,
                    Subtotal = l.Subtotal
                }).ToList()
            };

            return View(vm);
        }

        // POST público (sin login) que confirma la decisión mostrada en Responder.cshtml. La
        // identidad de quien responde se garantiza únicamente con el token del correo, no con
        // una sesión de PymeCore, así que no redirige a ninguna página que exija haber iniciado sesión.
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmarRespuesta(string? token, string? decision)
        {
            var (ok, numero, decisionConfirmada) = await _presupuestoService.ConfirmarRespuestaAsync(token, decision);

            if (!ok)
                return View("Responder", new ResponderPresupuestoViewModel { EsValido = false });

            var vm = new RespuestaConfirmadaViewModel
            {
                Numero = numero,
                Decision = decisionConfirmada!
            };
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Duplicar(int id)
        {
            var nuevo = await _presupuestoService.DuplicarAsync(id);
            TempData["Success"] = $"Se ha creado el presupuesto {nuevo.Numero} como copia. Ya puedes modificarlo.";
            return RedirectToAction(nameof(Details), new { id = nuevo.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AgregarLinea(LineaPresupuestoFormViewModel vm)
        {
            var presupuesto = await _presupuestoService.GetByIdAsync(vm.PresupuestoId);
            if (presupuesto is null) return NotFound();

            if (presupuesto.Estado != EstadoPresupuesto.Borrador)
            {
                TempData["Error"] = "Solo se pueden añadir líneas a un presupuesto en estado Borrador.";
                return RedirectToAction(nameof(Details), new { id = vm.PresupuestoId });
            }

            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Revisa los datos de la línea.";
                return RedirectToAction(nameof(Details), new { id = vm.PresupuestoId });
            }

            var linea = new LineaPresupuesto
            {
                PresupuestoId = vm.PresupuestoId,
                ProductoId = vm.ProductoId!.Value,
                Descripcion = vm.Descripcion,
                Cantidad = vm.Cantidad,
                PrecioUnitario = vm.PrecioUnitario
            };

            await _presupuestoService.AgregarLineaAsync(linea);
            return RedirectToAction(nameof(Details), new { id = vm.PresupuestoId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EliminarLinea(int lineaId, int presupuestoId)
        {
            var presupuesto = await _presupuestoService.GetByIdAsync(presupuestoId);
            if (presupuesto is null) return NotFound();

            if (presupuesto.Estado != EstadoPresupuesto.Borrador)
            {
                TempData["Error"] = "Solo se pueden eliminar líneas de un presupuesto en estado Borrador.";
                return RedirectToAction(nameof(Details), new { id = presupuestoId });
            }

            await _presupuestoService.EliminarLineaAsync(lineaId, presupuestoId);
            return RedirectToAction(nameof(Details), new { id = presupuestoId });
        }

        [HttpGet]
        public async Task<IActionResult> GetPrecioProducto(int productoId)
        {
            var producto = await _productoService.GetByIdAsync(productoId);
            if (producto is null) return NotFound();
            return Json(new { precio = producto.PrecioVenta });
        }

        private async Task CargarClientesAsync()
        {
            var clientes = (await _clienteService.GetAllAsync())
                .Where(c => c.Activo)
                .ToList();
            ViewBag.Clientes = new SelectList(clientes, "Id", "Nombre");
        }

        private async Task CargarProductosAsync()
        {
            var productos = await _productoService.GetAllAsync();
            ViewBag.Productos = new SelectList(productos, "Id", "Nombre");
        }
    }
}
