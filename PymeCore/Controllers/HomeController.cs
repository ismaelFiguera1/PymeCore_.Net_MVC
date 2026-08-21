using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PymeCore.Models;
using PymeCore.Services;
using PymeCore.ViewModels;

namespace PymeCore.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly ClienteService _clienteService;
        private readonly ProductoService _productoService;
        private readonly PresupuestoService _presupuestoService;
        private readonly PedidoService _pedidoService;
        private readonly FacturaService _facturaService;

        public HomeController(
            ILogger<HomeController> logger,
            ClienteService clienteService,
            ProductoService productoService,
            PresupuestoService presupuestoService,
            PedidoService pedidoService,
            FacturaService facturaService)
        {
            _logger = logger;
            _clienteService = clienteService;
            _productoService = productoService;
            _presupuestoService = presupuestoService;
            _pedidoService = pedidoService;
            _facturaService = facturaService;
        }

        public async Task<IActionResult> Index()
        {
            var clientes = await _clienteService.GetAllAsync();
            var productosBajoStock = await _productoService.GetBajoStockAsync();
            var presupuestos = await _presupuestoService.GetAllAsync();
            var pedidos = await _pedidoService.GetAllAsync();
            var facturas = await _facturaService.GetAllAsync();

            var facturasPendientes = facturas
                .Where(f => f.Estado == EstadoFactura.Pendiente)
                .OrderByDescending(f => f.FechaEmision)
                .ToList();

            var dashboard = new DashboardViewModel
            {
                ClientesActivos = clientes.Count(c => c.Activo),

                ProductosBajoStockTotal = productosBajoStock.Count,
                ProductosBajoStock = productosBajoStock
                    .OrderBy(p => p.StockActual)
                    .Take(5)
                    .ToList(),

                PresupuestosEnviadosTotal = presupuestos.Count(p => p.Estado == EstadoPresupuesto.Enviado),

                FacturasPendientesTotal = facturasPendientes.Count,
                FacturasPendientesImporteTotal = facturasPendientes.Sum(f => f.Total),
                FacturasPendientes = facturasPendientes.Take(5).ToList(),

                UltimosPedidos = pedidos.Take(5).ToList()
            };

            return View(dashboard);
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        [AllowAnonymous]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
