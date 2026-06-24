using Microsoft.AspNetCore.Mvc;
using PymeCore.Models;
using PymeCore.Services;
using PymeCore.ViewModels.Stock;

namespace PymeCore.Controllers
{
    public class MovimientoStockController : Controller
    {
        private readonly StockService _stockService;
        private readonly ProductoService _productoService;

        public MovimientoStockController(StockService stockService, ProductoService productoService)
        {
            _stockService = stockService;
            _productoService = productoService;
        }

        public async Task<IActionResult> Index(int productoId)
        {
            var producto = await _productoService.GetByIdAsync(productoId);
            if (producto is null) return NotFound();

            var movimientos = await _stockService.GetHistorialPorProductoAsync(productoId);

            ViewBag.Producto = producto;
            return View(movimientos);
        }

        public async Task<IActionResult> Create(int productoId)
        {
            var producto = await _productoService.GetByIdAsync(productoId);
            if (producto is null) return NotFound();

            var vm = new MovimientoStockFormViewModel
            {
                ProductoId      = producto.Id,
                NombreProducto  = producto.Nombre,
                StockActual     = producto.StockActual
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(MovimientoStockFormViewModel vm)
        {
            if (vm.Tipo != TipoMovimiento.Ajuste && vm.Cantidad <= 0)
                ModelState.AddModelError(nameof(vm.Cantidad), "La cantidad debe ser mayor que cero.");

            if (!ModelState.IsValid)
            {
                var producto = await _productoService.GetByIdAsync(vm.ProductoId);
                vm.NombreProducto = producto?.Nombre ?? string.Empty;
                vm.StockActual    = producto?.StockActual ?? 0;
                return View(vm);
            }

            var movimiento = new MovimientoStock
            {
                ProductoId = vm.ProductoId,
                Tipo       = vm.Tipo,
                Cantidad   = vm.Cantidad,
                Motivo     = vm.Motivo
            };

            var error = await _stockService.RegistrarMovimientoAsync(movimiento);
            if (error is not null)
            {
                ModelState.AddModelError(nameof(vm.Cantidad), error);
                var producto = await _productoService.GetByIdAsync(vm.ProductoId);
                vm.NombreProducto = producto?.Nombre ?? string.Empty;
                vm.StockActual    = producto?.StockActual ?? 0;
                return View(vm);
            }

            return RedirectToAction(nameof(Index), new { productoId = vm.ProductoId });
        }
    }
}
