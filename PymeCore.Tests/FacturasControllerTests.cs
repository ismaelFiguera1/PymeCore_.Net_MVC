using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Options;
using PymeCore.Controllers;
using PymeCore.Data;
using PymeCore.Models;
using PymeCore.Services;
using PymeCore.Tests.TestHelpers;
using Xunit;

namespace PymeCore.Tests
{
    // ITempDataProvider "en memoria", igual que en el resto de *ControllerTests.
    file sealed class TempDataProviderFalsoFacturas : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    // No se prueba la generacion real de PDF aqui (decision explicita): DescargarPdf solo se
    // cubre en las ramas que NO llegan a invocar FacturaPdfService.Generar (factura no encontrada,
    // snapshot no encontrado, snapshot con error). El camino de exito y la rama catch de esa accion
    // quedan fuera de esta suite a proposito.
    //
    // GenerarDesdePedidoAsync abre transaccion (no soportado por InMemory), asi que todos estos
    // tests usan SqliteContextoDePrueba.
    public class FacturasControllerTests
    {
        private static IOptions<EmpresaOptions> EmpresaValida() => Options.Create(new EmpresaOptions
        {
            Nombre = "PymeCore S.L.",
            Cif = "J16793500",
            Direccion = "C. Caleruega 31, Burgos"
        });

        private static async Task<Cliente> CrearClienteAsync(ApplicationDbContext context)
        {
            var cliente = new Cliente { Nombre = "Cliente de Prueba", Nif = "12345678A", Email = "cliente@test.com", Telefono = "600123456" };
            context.Clientes.Add(cliente);
            await context.SaveChangesAsync();
            return cliente;
        }

        private static async Task<Producto> CrearProductoAsync(ApplicationDbContext context)
        {
            var proveedor = new Proveedor { Nombre = "Proveedor de Prueba", Cif = Guid.NewGuid().ToString("N")[..9], Email = "proveedor@test.com" };
            context.Proveedores.Add(proveedor);
            await context.SaveChangesAsync();
            var producto = new Producto { Nombre = "Producto de Prueba", Sku = "TES-0001", PrecioCoste = 10m, PrecioVenta = 20m, ProveedorId = proveedor.Id };
            context.Productos.Add(producto);
            await context.SaveChangesAsync();
            return producto;
        }

        private static async Task<Presupuesto> CrearPresupuestoAsync(ApplicationDbContext context, int clienteId)
        {
            var presupuesto = new Presupuesto { Numero = $"PRES-{Guid.NewGuid():N}"[..15], ClienteId = clienteId, Estado = EstadoPresupuesto.Aceptado };
            context.Presupuestos.Add(presupuesto);
            await context.SaveChangesAsync();
            return presupuesto;
        }

        private static async Task<Pedido> CrearPedidoAsync(ApplicationDbContext context, int clienteId, int presupuestoOrigenId, EstadoPedido estado, int? facturaId = null, params (Producto Producto, int Cantidad)[] lineas)
        {
            var pedido = new Pedido { Numero = $"PED-{Guid.NewGuid():N}"[..12], ClienteId = clienteId, PresupuestoOrigenId = presupuestoOrigenId, Estado = estado, FacturaId = facturaId };
            context.Pedidos.Add(pedido);
            await context.SaveChangesAsync();
            foreach (var (producto, cantidad) in lineas)
                context.LineasPedido.Add(new LineaPedido { PedidoId = pedido.Id, ProductoId = producto.Id, Cantidad = cantidad, PrecioUnitario = producto.PrecioVenta, Subtotal = producto.PrecioVenta * cantidad });
            if (lineas.Length > 0)
                await context.SaveChangesAsync();
            return pedido;
        }

        private static async Task<Factura> CrearFacturaAsync(ApplicationDbContext context, int clienteId, int pedidoId, EstadoFactura estado = EstadoFactura.Pendiente)
        {
            var factura = new Factura { Numero = $"FAC-{Guid.NewGuid():N}"[..12], ClienteId = clienteId, PedidoId = pedidoId, Estado = estado };
            context.Facturas.Add(factura);
            await context.SaveChangesAsync();
            return factura;
        }

        private static FacturasController CrearController(SqliteContextoDePrueba db)
        {
            var context = db.NuevoContexto();
            var controller = new FacturasController(
                new FacturaService(context, new FacturaSnapshotService(context, EmpresaValida())),
                new FacturaSnapshotService(context, EmpresaValida()),
                new FacturaPdfService())
            {
                TempData = new TempDataDictionary(new DefaultHttpContext(), new TempDataProviderFalsoFacturas())
            };
            return controller;
        }

        // ---------- Index ----------

        [Fact]
        public async Task Index_DevuelveVistaConLaListaDeFacturas()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id);
            var pedido = await CrearPedidoAsync(db.Context, cliente.Id, presupuesto.Id, EstadoPedido.Completado);
            await CrearFacturaAsync(db.Context, cliente.Id, pedido.Id);
            var controller = CrearController(db);

            var resultado = await controller.Index();

            var view = Assert.IsType<ViewResult>(resultado);
            Assert.IsAssignableFrom<List<Factura>>(view.Model);
        }

        // ---------- Details ----------

        [Fact]
        public async Task Details_ConIdInexistente_DevuelveNotFound()
        {
            using var db = SqliteContextoDePrueba.Create();
            var controller = CrearController(db);

            var resultado = await controller.Details(999);

            Assert.IsType<NotFoundResult>(resultado);
        }

        [Fact]
        public async Task Details_ConIdExistente_DevuelveVistaConLaFactura()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id);
            var pedido = await CrearPedidoAsync(db.Context, cliente.Id, presupuesto.Id, EstadoPedido.Completado);
            var factura = await CrearFacturaAsync(db.Context, cliente.Id, pedido.Id);
            var controller = CrearController(db);

            var resultado = await controller.Details(factura.Id);

            var view = Assert.IsType<ViewResult>(resultado);
            var modelo = Assert.IsType<Factura>(view.Model);
            Assert.Equal(factura.Id, modelo.Id);
        }

        // ---------- DescargarPdf: solo las ramas que no generan el PDF ----------

        [Fact]
        public async Task DescargarPdf_ConFacturaInexistente_DevuelveNotFound()
        {
            using var db = SqliteContextoDePrueba.Create();
            var controller = CrearController(db);

            var resultado = await controller.DescargarPdf(999);

            Assert.IsType<NotFoundResult>(resultado);
        }

        [Fact]
        public async Task DescargarPdf_SinSnapshotYSinError_DevuelveNotFound()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id);
            var pedido = await CrearPedidoAsync(db.Context, cliente.Id, presupuesto.Id, EstadoPedido.Completado);
            // Factura creada directamente contra el contexto, sin pasar por GenerarDesdePedidoAsync:
            // no tiene ningun FacturaSnapshot asociado.
            var factura = await CrearFacturaAsync(db.Context, cliente.Id, pedido.Id);
            var controller = CrearController(db);

            var resultado = await controller.DescargarPdf(factura.Id);

            Assert.IsType<NotFoundResult>(resultado);
        }

        [Fact]
        public async Task DescargarPdf_ConSnapshotDeVersionNoSoportada_PonerTempDataErrorYRedirigeADetails()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id);
            var pedido = await CrearPedidoAsync(db.Context, cliente.Id, presupuesto.Id, EstadoPedido.Completado);
            var factura = await CrearFacturaAsync(db.Context, cliente.Id, pedido.Id);
            db.Context.FacturaSnapshots.Add(new FacturaSnapshot { FacturaId = factura.Id, DatosJson = "{}", Version = 99 });
            await db.Context.SaveChangesAsync();
            var controller = CrearController(db);

            var resultado = await controller.DescargarPdf(factura.Id);

            var redirect = Assert.IsType<RedirectToActionResult>(resultado);
            Assert.Equal("Details", redirect.ActionName);
            Assert.Contains("versión no soportada", (string)controller.TempData["Error"]!);
        }

        // ---------- GenerarDesdePedido ----------

        [Fact]
        public async Task GenerarDesdePedido_ConError_PonerTempDataErrorYRedirigeAPedidosDetails()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id);
            var pedido = await CrearPedidoAsync(db.Context, cliente.Id, presupuesto.Id, EstadoPedido.Pendiente); // no esta Completado
            var controller = CrearController(db);

            var resultado = await controller.GenerarDesdePedido(pedido.Id);

            var redirect = Assert.IsType<RedirectToActionResult>(resultado);
            Assert.Equal("Details", redirect.ActionName);
            Assert.Equal("Pedidos", redirect.ControllerName);
            Assert.Equal(pedido.Id, redirect.RouteValues!["id"]);
            Assert.Contains("Solo se puede facturar", (string)controller.TempData["Error"]!);
        }

        [Fact]
        public async Task GenerarDesdePedido_ConExito_PonerTempDataSuccessYRedirigeADetailsDeLaFactura()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var producto = await CrearProductoAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id);
            var pedido = await CrearPedidoAsync(db.Context, cliente.Id, presupuesto.Id, EstadoPedido.Completado, lineas: (producto, 2));
            var controller = CrearController(db);

            var resultado = await controller.GenerarDesdePedido(pedido.Id);

            var redirect = Assert.IsType<RedirectToActionResult>(resultado);
            Assert.Equal("Details", redirect.ActionName);
            Assert.Null(redirect.ControllerName); // se queda en el propio FacturasController
            using var lectura = db.NuevoContexto();
            var facturaCreada = lectura.Facturas.Single();
            Assert.Equal(facturaCreada.Id, redirect.RouteValues!["id"]);
            Assert.Contains(facturaCreada.Numero, (string)controller.TempData["Success"]!);
        }

        // ---------- CambiarEstado ----------

        [Fact]
        public async Task CambiarEstado_ConExitoYNuevoEstadoAnulada_PonerTempDataWarning()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id);
            var pedido = await CrearPedidoAsync(db.Context, cliente.Id, presupuesto.Id, EstadoPedido.Completado);
            var factura = await CrearFacturaAsync(db.Context, cliente.Id, pedido.Id);
            var controller = CrearController(db);

            var resultado = await controller.CambiarEstado(factura.Id, EstadoFactura.Anulada);

            var redirect = Assert.IsType<RedirectToActionResult>(resultado);
            Assert.Equal("Details", redirect.ActionName);
            Assert.Equal(factura.Id, redirect.RouteValues!["id"]);
            Assert.Equal("Factura anulada.", controller.TempData["Warning"]);
        }

        [Fact]
        public async Task CambiarEstado_ConExitoYOtroEstado_PonerTempDataSuccessConElEstado()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id);
            var pedido = await CrearPedidoAsync(db.Context, cliente.Id, presupuesto.Id, EstadoPedido.Completado);
            var factura = await CrearFacturaAsync(db.Context, cliente.Id, pedido.Id);
            var controller = CrearController(db);

            await controller.CambiarEstado(factura.Id, EstadoFactura.Pagada);

            Assert.Equal("Estado actualizado a Pagada.", controller.TempData["Success"]);
        }

        [Fact]
        public async Task CambiarEstado_ConError_PonerTempDataError()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id);
            var pedido = await CrearPedidoAsync(db.Context, cliente.Id, presupuesto.Id, EstadoPedido.Completado);
            var factura = await CrearFacturaAsync(db.Context, cliente.Id, pedido.Id, estado: EstadoFactura.Anulada); // estado final
            var controller = CrearController(db);

            var resultado = await controller.CambiarEstado(factura.Id, EstadoFactura.Pagada);

            Assert.IsType<RedirectToActionResult>(resultado);
            Assert.Equal("No se puede cambiar de Anulada a Pagada.", controller.TempData["Error"]);
        }
    }
}
