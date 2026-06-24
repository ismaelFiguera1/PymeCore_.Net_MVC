using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using PymeCore.Models;
using PymeCore.Services;
using PymeCore.ViewModels.Productos;

namespace PymeCore.Controllers
{
    public class ProductosController : Controller
    {
        private readonly ProductoService _productoService;
        private readonly ProveedorService _proveedorService;
        private readonly StockService _stockService;

        public ProductosController(ProductoService productoService, ProveedorService proveedorService, StockService stockService)
        {
            _productoService = productoService;
            _proveedorService = proveedorService;
            _stockService = stockService;
        }

        public async Task<IActionResult> Index(string? buscar)
        {
            ViewBag.Buscar = buscar;
            var productos = await _productoService.GetAllAsync(buscar);
            return View(productos);
        }

        public async Task<IActionResult> Details(int id)
        {
            var producto = await _productoService.GetByIdAsync(id);
            if (producto is null) return NotFound();
            return View(producto);
        }

        public async Task<IActionResult> Create()
        {
            await CargarProveedoresAsync();
            return View(new ProductoFormViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ProductoFormViewModel vm)
        {
            if (await _productoService.ExisteSkuAsync(vm.Sku))
                ModelState.AddModelError(nameof(vm.Sku), "Ya existe un producto con este SKU.");

            if (!ModelState.IsValid)
            {
                await CargarProveedoresAsync();
                return View(vm);
            }

            var producto = new Producto
            {
                Nombre      = vm.Nombre,
                Sku         = vm.Sku,
                PrecioCoste = vm.PrecioCoste,
                PrecioVenta = vm.PrecioVenta,
                StockActual = 0,
                StockMinimo = vm.StockMinimo,
                ProveedorId = vm.ProveedorId
            };

            await _productoService.CreateAsync(producto);

            if (vm.StockActual > 0)
            {
                await _stockService.RegistrarMovimientoAsync(new Models.MovimientoStock
                {
                    ProductoId = producto.Id,
                    Tipo       = Models.TipoMovimiento.Entrada,
                    Cantidad   = vm.StockActual,
                    Motivo     = "Stock inicial"
                });
            }

            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Edit(int id)
        {
            var producto = await _productoService.GetByIdAsync(id);
            if (producto is null) return NotFound();

            var vm = new ProductoFormViewModel
            {
                Id          = producto.Id,
                Nombre      = producto.Nombre,
                Sku         = producto.Sku,
                PrecioCoste = producto.PrecioCoste,
                PrecioVenta = producto.PrecioVenta,
                StockActual = producto.StockActual,
                StockMinimo = producto.StockMinimo,
                ProveedorId = producto.ProveedorId
            };

            await CargarProveedoresAsync();
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ProductoFormViewModel vm)
        {
            if (id != vm.Id) return BadRequest();

            if (await _productoService.ExisteSkuAsync(vm.Sku, excludeId: vm.Id))
                ModelState.AddModelError(nameof(vm.Sku), "Ya existe un producto con este SKU.");

            if (!ModelState.IsValid)
            {
                await CargarProveedoresAsync();
                return View(vm);
            }

            var producto = await _productoService.GetByIdAsync(id);
            if (producto is null) return NotFound();

            producto.Nombre      = vm.Nombre;
            producto.Sku         = vm.Sku;
            producto.PrecioCoste = vm.PrecioCoste;
            producto.PrecioVenta = vm.PrecioVenta;
            producto.StockMinimo = vm.StockMinimo;
            producto.ProveedorId = vm.ProveedorId;

            await _productoService.UpdateAsync(producto);
            return RedirectToAction(nameof(Index));
        }

        private async Task CargarProveedoresAsync()
        {
            var proveedores = await _proveedorService.GetAllAsync();
            ViewBag.Proveedores = new SelectList(proveedores, "Id", "Nombre");
        }
    }
}
