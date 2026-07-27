using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using PymeCore.Controllers;
using PymeCore.Data;
using PymeCore.Models;
using PymeCore.Services;
using PymeCore.Tests.TestHelpers;
using Xunit;

namespace PymeCore.Tests
{
    // ITempDataProvider "en memoria", igual que en el resto de *ControllerTests.
    file sealed class TempDataProviderFalsoPedidos : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    // CrearDesdePresupuestoAsync y CambiarEstadoAsync(Completado) abren transaccion (no soportado
    // por InMemory), asi que todos estos tests usan SqliteContextoDePrueba.
    public class PedidosControllerTests
    {
        private static async Task<Cliente> CrearClienteAsync(ApplicationDbContext context, string nif = "12345678A")
        {
            var cliente = new Cliente { Nombre = "Cliente de Prueba", Nif = nif, Email = "cliente@test.com", Telefono = "600123456" };
            context.Clientes.Add(cliente);
            await context.SaveChangesAsync();
            return cliente;
        }

        private static async Task<Presupuesto> CrearPresupuestoAsync(ApplicationDbContext context, int clienteId, EstadoPresupuesto estado = EstadoPresupuesto.Aceptado)
        {
            var presupuesto = new Presupuesto { Numero = $"PRES-{Guid.NewGuid():N}"[..15], ClienteId = clienteId, Estado = estado };
            context.Presupuestos.Add(presupuesto);
            await context.SaveChangesAsync();
            return presupuesto;
        }

        private static async Task<Pedido> CrearPedidoAsync(ApplicationDbContext context, int clienteId, int presupuestoOrigenId, EstadoPedido estado = EstadoPedido.Pendiente)
        {
            var pedido = new Pedido { Numero = $"PED-{Guid.NewGuid():N}"[..12], ClienteId = clienteId, PresupuestoOrigenId = presupuestoOrigenId, Estado = estado };
            context.Pedidos.Add(pedido);
            await context.SaveChangesAsync();
            return pedido;
        }

        private static PedidosController CrearController(SqliteContextoDePrueba db)
        {
            var context = db.NuevoContexto();
            var controller = new PedidosController(new PedidoService(context, new StockService(context)))
            {
                TempData = new TempDataDictionary(new DefaultHttpContext(), new TempDataProviderFalsoPedidos())
            };
            return controller;
        }

        // ---------- Index ----------

        [Fact]
        public async Task Index_DevuelveVistaConLaListaDePedidos()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id);
            await CrearPedidoAsync(db.Context, cliente.Id, presupuesto.Id);
            var controller = CrearController(db);

            var resultado = await controller.Index();

            var view = Assert.IsType<ViewResult>(resultado);
            Assert.IsAssignableFrom<List<Pedido>>(view.Model);
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
        public async Task Details_ConIdExistente_DevuelveVistaConElPedido()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id);
            var pedido = await CrearPedidoAsync(db.Context, cliente.Id, presupuesto.Id);
            var controller = CrearController(db);

            var resultado = await controller.Details(pedido.Id);

            var view = Assert.IsType<ViewResult>(resultado);
            var modelo = Assert.IsType<Pedido>(view.Model);
            Assert.Equal(pedido.Id, modelo.Id);
        }

        // ---------- CambiarEstado ----------

        [Fact]
        public async Task CambiarEstado_ConExitoYNuevoEstadoCancelado_PonerTempDataWarning()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id);
            var pedido = await CrearPedidoAsync(db.Context, cliente.Id, presupuesto.Id, EstadoPedido.Pendiente);
            var controller = CrearController(db);

            var resultado = await controller.CambiarEstado(pedido.Id, EstadoPedido.Cancelado);

            var redirect = Assert.IsType<RedirectToActionResult>(resultado);
            Assert.Equal("Details", redirect.ActionName);
            Assert.Equal(pedido.Id, redirect.RouteValues!["id"]);
            Assert.Equal("Pedido cancelado.", controller.TempData["Warning"]);
        }

        [Fact]
        public async Task CambiarEstado_ConExitoYOtroEstado_PonerTempDataSuccessConElEstado()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id);
            var pedido = await CrearPedidoAsync(db.Context, cliente.Id, presupuesto.Id, EstadoPedido.Pendiente);
            var controller = CrearController(db);

            await controller.CambiarEstado(pedido.Id, EstadoPedido.EnPreparacion);

            Assert.Equal("Estado actualizado a EnPreparacion.", controller.TempData["Success"]);
        }

        [Fact]
        public async Task CambiarEstado_ConError_PonerTempDataError()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id);
            var pedido = await CrearPedidoAsync(db.Context, cliente.Id, presupuesto.Id, EstadoPedido.Cancelado); // ya no admite transiciones
            var controller = CrearController(db);

            var resultado = await controller.CambiarEstado(pedido.Id, EstadoPedido.EnPreparacion);

            Assert.IsType<RedirectToActionResult>(resultado);
            Assert.Equal("No se puede cambiar de Cancelado a EnPreparacion.", controller.TempData["Error"]);
        }

        // ---------- ConvertirDesdePresupuesto ----------

        [Fact]
        public async Task ConvertirDesdePresupuesto_ConError_PonerTempDataErrorYRedirigeAPresupuestosDetails()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id, estado: EstadoPresupuesto.Borrador); // no esta Aceptado
            var controller = CrearController(db);

            var resultado = await controller.ConvertirDesdePresupuesto(presupuesto.Id);

            var redirect = Assert.IsType<RedirectToActionResult>(resultado);
            Assert.Equal("Details", redirect.ActionName);
            Assert.Equal("Presupuestos", redirect.ControllerName);
            Assert.Equal(presupuesto.Id, redirect.RouteValues!["id"]);
            Assert.Contains("Solo se puede convertir", (string)controller.TempData["Error"]!);
        }

        [Fact]
        public async Task ConvertirDesdePresupuesto_ConExito_PonerTempDataSuccessYRedirigeADetailsDelPedido()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id, estado: EstadoPresupuesto.Aceptado);
            var controller = CrearController(db);

            var resultado = await controller.ConvertirDesdePresupuesto(presupuesto.Id);

            var redirect = Assert.IsType<RedirectToActionResult>(resultado);
            Assert.Equal("Details", redirect.ActionName);
            Assert.Null(redirect.ControllerName); // se queda en el propio PedidosController
            using var lectura = db.NuevoContexto();
            var pedidoCreado = lectura.Pedidos.Single();
            Assert.Equal(pedidoCreado.Id, redirect.RouteValues!["id"]);
            Assert.Contains(pedidoCreado.Numero, (string)controller.TempData["Success"]!);
        }
    }
}
