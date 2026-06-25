using Microsoft.AspNetCore.Mvc;
using PymeCore.Services;

namespace PymeCore.Controllers
{
    public class PedidosController : Controller
    {
        private readonly PedidoService _pedidoService;

        public PedidosController(PedidoService pedidoService)
        {
            _pedidoService = pedidoService;
        }

        public async Task<IActionResult> Index()
        {
            var pedidos = await _pedidoService.GetAllAsync();
            return View(pedidos);
        }

        public async Task<IActionResult> Details(int id)
        {
            var pedido = await _pedidoService.GetByIdAsync(id);
            if (pedido is null) return NotFound();
            return View(pedido);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConvertirDesdePresupuesto(int presupuestoId)
        {
            var (ok, error, pedido) = await _pedidoService.CrearDesdePresupuestoAsync(presupuestoId);

            if (!ok)
            {
                TempData["Error"] = error;
                return RedirectToAction("Details", "Presupuestos", new { id = presupuestoId });
            }

            TempData["Success"] = $"Pedido {pedido!.Numero} creado correctamente.";
            return RedirectToAction("Details", new { id = pedido.Id });
        }
    }
}
