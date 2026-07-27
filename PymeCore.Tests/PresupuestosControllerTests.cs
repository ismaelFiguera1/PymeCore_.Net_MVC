using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using PymeCore.Controllers;
using PymeCore.Data;
using PymeCore.Models;
using PymeCore.Services;
using PymeCore.Tests.TestHelpers;
using PymeCore.ViewModels.Presupuestos;
using Xunit;

namespace PymeCore.Tests
{
    // ITempDataProvider "en memoria", igual que en ClientesControllerTests/ProveedoresControllerTests.
    file sealed class TempDataProviderFalsoPresupuestos : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    // AgregarLineaAsync/EliminarLineaAsync usan Database.BeginTransactionAsync (no soportado por
    // InMemory), asi que todos estos tests usan SqliteContextoDePrueba, igual que en
    // ProductosControllerTests. Cada CrearController crea un ApplicationDbContext nuevo, como en la
    // app real (un DbContext por peticion HTTP).
    public class PresupuestosControllerTests
    {
        private static async Task<Cliente> CrearClienteAsync(ApplicationDbContext context, bool activo = true, string nif = "12345678A")
        {
            var cliente = new Cliente { Nombre = "Cliente de Prueba", Nif = nif, Email = "cliente@test.com", Telefono = "600123456", Activo = activo };
            context.Clientes.Add(cliente);
            await context.SaveChangesAsync();
            return cliente;
        }

        private static async Task<Producto> CrearProductoAsync(ApplicationDbContext context, string sku = "TES-0001", decimal precioVenta = 20m)
        {
            var proveedor = new Proveedor { Nombre = "Proveedor de Prueba", Cif = Guid.NewGuid().ToString("N")[..9], Email = "proveedor@test.com" };
            context.Proveedores.Add(proveedor);
            await context.SaveChangesAsync();
            var producto = new Producto { Nombre = "Producto de Prueba", Sku = sku, PrecioCoste = 10m, PrecioVenta = precioVenta, ProveedorId = proveedor.Id };
            context.Productos.Add(producto);
            await context.SaveChangesAsync();
            return producto;
        }

        private static async Task<Presupuesto> CrearPresupuestoAsync(ApplicationDbContext context, int clienteId, EstadoPresupuesto estado = EstadoPresupuesto.Borrador, string? numero = null)
        {
            var presupuesto = new Presupuesto { Numero = numero ?? $"PRES-{Guid.NewGuid():N}"[..15], ClienteId = clienteId, Estado = estado };
            context.Presupuestos.Add(presupuesto);
            await context.SaveChangesAsync();
            return presupuesto;
        }

        private static PresupuestosController CrearController(SqliteContextoDePrueba db)
        {
            var context = db.NuevoContexto();
            var controller = new PresupuestosController(new PresupuestoService(context), new ClienteService(context), new ProductoService(context, new StockService(context)))
            {
                TempData = new TempDataDictionary(new DefaultHttpContext(), new TempDataProviderFalsoPresupuestos())
            };
            return controller;
        }

        private static PresupuestoFormViewModel VmValido(int clienteId) => new()
        {
            ClienteId = clienteId,
            Observaciones = "Observaciones de prueba"
        };

        // ---------- Index ----------

        [Fact]
        public async Task Index_DevuelveVistaConLaListaYElTerminoDeBusquedaEnViewBag()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            await CrearPresupuestoAsync(db.Context, cliente.Id);
            var controller = CrearController(db);

            var resultado = await controller.Index("algo");

            var view = Assert.IsType<ViewResult>(resultado);
            Assert.IsAssignableFrom<List<Presupuesto>>(view.Model);
            Assert.Equal("algo", controller.ViewBag.Buscar);
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
        public async Task Details_ConIdExistente_CargaProductosYPrecargaLaLineaVmConElId()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id);
            var controller = CrearController(db);

            var resultado = await controller.Details(presupuesto.Id);

            var view = Assert.IsType<ViewResult>(resultado);
            Assert.IsType<Presupuesto>(view.Model);
            Assert.IsType<SelectList>(controller.ViewBag.Productos);
            var lineaVm = Assert.IsType<LineaPresupuestoFormViewModel>(controller.ViewBag.LineaVm);
            Assert.Equal(presupuesto.Id, lineaVm.PresupuestoId);
        }

        // ---------- Create (GET) ----------

        [Fact]
        public async Task Create_Get_CargaSoloClientesActivosEnElDesplegable()
        {
            using var db = SqliteContextoDePrueba.Create();
            await CrearClienteAsync(db.Context, activo: true, nif: "11111111A");
            await CrearClienteAsync(db.Context, activo: false, nif: "22222222B");
            var controller = CrearController(db);

            await controller.Create();

            var lista = Assert.IsType<SelectList>(controller.ViewBag.Clientes);
            Assert.Single(lista);
        }

        // ---------- Create (POST) ----------

        [Fact]
        public async Task Create_Post_ConModelStateInvalido_DevuelveViewYRecargaClientes()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var controller = CrearController(db);
            controller.ModelState.AddModelError("ClienteId", "El cliente es obligatorio");

            var resultado = await controller.Create(VmValido(cliente.Id));

            Assert.IsType<ViewResult>(resultado);
            Assert.NotNull(controller.ViewBag.Clientes);
            using var lectura = db.NuevoContexto();
            Assert.Empty(lectura.Presupuestos);
        }

        [Fact]
        public async Task Create_Post_ConDatosValidos_CreaElPresupuestoEnBorradorConTotalCeroYRedirigeADetails()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var controller = CrearController(db);

            var resultado = await controller.Create(VmValido(cliente.Id));

            var redirect = Assert.IsType<RedirectToActionResult>(resultado);
            Assert.Equal("Details", redirect.ActionName);
            using var lectura = db.NuevoContexto();
            var guardado = Assert.Single(lectura.Presupuestos);
            Assert.Equal(EstadoPresupuesto.Borrador, guardado.Estado);
            Assert.Equal(0m, guardado.Total);
            Assert.StartsWith("PRES-", guardado.Numero);
            Assert.Equal(redirect.RouteValues!["id"], guardado.Id);
        }

        // ---------- Edit (GET) ----------

        [Fact]
        public async Task Edit_Get_ConIdInexistente_DevuelveNotFound()
        {
            using var db = SqliteContextoDePrueba.Create();
            var controller = CrearController(db);

            var resultado = await controller.Edit(999);

            Assert.IsType<NotFoundResult>(resultado);
        }

        [Fact]
        public async Task Edit_Get_ConEstadoDistintoDeBorrador_RedirigeADetailsConTempDataError()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id, estado: EstadoPresupuesto.Enviado);
            var controller = CrearController(db);

            var resultado = await controller.Edit(presupuesto.Id);

            var redirect = Assert.IsType<RedirectToActionResult>(resultado);
            Assert.Equal("Details", redirect.ActionName);
            Assert.Equal("Un presupuesto Enviado no puede editarse.", controller.TempData["Error"]);
        }

        [Fact]
        public async Task Edit_Get_ConEstadoBorrador_PrecargaElViewModelYCargaClientes()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id);
            var controller = CrearController(db);

            var resultado = await controller.Edit(presupuesto.Id);

            var view = Assert.IsType<ViewResult>(resultado);
            var vm = Assert.IsType<PresupuestoFormViewModel>(view.Model);
            Assert.Equal(presupuesto.Id, vm.Id);
            Assert.Equal(presupuesto.Numero, vm.Numero);
            Assert.NotNull(controller.ViewBag.Clientes);
        }

        // ---------- Edit (POST) ----------

        [Fact]
        public async Task Edit_Post_ConIdDistintoAlDelViewModel_DevuelveBadRequest()
        {
            using var db = SqliteContextoDePrueba.Create();
            var controller = CrearController(db);
            var vm = VmValido(1);
            vm.Id = 5;

            var resultado = await controller.Edit(id: 999, vm: vm);

            Assert.IsType<BadRequestResult>(resultado);
        }

        [Fact]
        public async Task Edit_Post_ConModelStateInvalido_DevuelveViewYRecargaClientes()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id);
            var controller = CrearController(db);
            controller.ModelState.AddModelError("ClienteId", "El cliente es obligatorio");
            var vm = VmValido(cliente.Id);
            vm.Id = presupuesto.Id;

            var resultado = await controller.Edit(presupuesto.Id, vm);

            Assert.IsType<ViewResult>(resultado);
            Assert.NotNull(controller.ViewBag.Clientes);
        }

        [Fact]
        public async Task Edit_Post_ConIdQueNoExisteEnBaseDeDatos_DevuelveNotFound()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var controller = CrearController(db);
            var vm = VmValido(cliente.Id);
            vm.Id = 999;

            var resultado = await controller.Edit(999, vm);

            Assert.IsType<NotFoundResult>(resultado);
        }

        [Fact]
        public async Task Edit_Post_ConEstadoDistintoDeBorrador_DevuelveBadRequest()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id, estado: EstadoPresupuesto.Enviado);
            var controller = CrearController(db);
            var vm = VmValido(cliente.Id);
            vm.Id = presupuesto.Id;

            var resultado = await controller.Edit(presupuesto.Id, vm);

            Assert.IsType<BadRequestResult>(resultado);
        }

        [Fact]
        public async Task Edit_Post_ConDatosValidos_ActualizaClienteYObservacionesYRedirigeADetails()
        {
            using var db = SqliteContextoDePrueba.Create();
            var clienteOriginal = await CrearClienteAsync(db.Context, nif: "11111111A");
            var clienteNuevo = await CrearClienteAsync(db.Context, nif: "22222222B");
            var presupuesto = await CrearPresupuestoAsync(db.Context, clienteOriginal.Id);
            var controller = CrearController(db);
            var vm = VmValido(clienteNuevo.Id);
            vm.Id = presupuesto.Id;
            vm.Observaciones = "Nuevas observaciones";

            var resultado = await controller.Edit(presupuesto.Id, vm);

            Assert.IsType<RedirectToActionResult>(resultado);
            using var lectura = db.NuevoContexto();
            var actualizado = await lectura.Presupuestos.FindAsync(presupuesto.Id);
            Assert.Equal(clienteNuevo.Id, actualizado!.ClienteId);
            Assert.Equal("Nuevas observaciones", actualizado.Observaciones);
        }

        // El Numero y el Estado no forman parte de los campos que el controlador reasigna en Edit,
        // aunque el ViewModel traiga un Numero distinto (el formulario lo muestra pero no lo deja
        // editar realmente).
        [Fact]
        public async Task Edit_Post_NoModificaElNumeroAunqueElFormularioEnvieOtroValor()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id, numero: "PRES-2026-001");
            var controller = CrearController(db);
            var vm = VmValido(cliente.Id);
            vm.Id = presupuesto.Id;
            vm.Numero = "OTRO-NUMERO";

            await controller.Edit(presupuesto.Id, vm);

            using var lectura = db.NuevoContexto();
            var actualizado = await lectura.Presupuestos.FindAsync(presupuesto.Id);
            Assert.Equal("PRES-2026-001", actualizado!.Numero);
        }

        // ---------- Enviar / Aceptar / Rechazar ----------

        [Fact]
        public async Task Enviar_ConExito_PonerTempDataSuccessYRedirigeADetails()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var producto = await CrearProductoAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id);
            var conLinea = db.NuevoContexto();
            conLinea.LineasPresupuesto.Add(new LineaPresupuesto { PresupuestoId = presupuesto.Id, ProductoId = producto.Id, Cantidad = 1, PrecioUnitario = 10m, Subtotal = 10m });
            await conLinea.SaveChangesAsync();
            var controller = CrearController(db);

            var resultado = await controller.Enviar(presupuesto.Id);

            var redirect = Assert.IsType<RedirectToActionResult>(resultado);
            Assert.Equal("Details", redirect.ActionName);
            Assert.Equal("Presupuesto enviado al cliente.", controller.TempData["Success"]);
        }

        [Fact]
        public async Task Enviar_ConError_PonerTempDataErrorYRedirigeADetails()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id); // sin lineas
            var controller = CrearController(db);

            var resultado = await controller.Enviar(presupuesto.Id);

            Assert.IsType<RedirectToActionResult>(resultado);
            Assert.Equal("No se puede enviar un presupuesto sin líneas. Añade al menos una.", controller.TempData["Error"]);
        }

        [Fact]
        public async Task Aceptar_ConExito_PonerTempDataSuccess()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id, estado: EstadoPresupuesto.Enviado);
            var controller = CrearController(db);

            await controller.Aceptar(presupuesto.Id);

            Assert.Equal("Presupuesto aceptado.", controller.TempData["Success"]);
        }

        [Fact]
        public async Task Aceptar_ConError_PonerTempDataError()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id, estado: EstadoPresupuesto.Borrador); // no esta Enviado
            var controller = CrearController(db);

            var resultado = await controller.Aceptar(presupuesto.Id);

            Assert.IsType<RedirectToActionResult>(resultado);
            Assert.Equal("Solo se puede aceptar un presupuesto en estado Enviado (estado actual: Borrador).", controller.TempData["Error"]);
        }

        [Fact]
        public async Task Rechazar_ConExito_PonerTempDataSuccess()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id, estado: EstadoPresupuesto.Enviado);
            var controller = CrearController(db);

            await controller.Rechazar(presupuesto.Id);

            Assert.Equal("Presupuesto rechazado.", controller.TempData["Success"]);
        }

        [Fact]
        public async Task Rechazar_ConError_PonerTempDataError()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id, estado: EstadoPresupuesto.Borrador); // no esta Enviado
            var controller = CrearController(db);

            var resultado = await controller.Rechazar(presupuesto.Id);

            Assert.IsType<RedirectToActionResult>(resultado);
            Assert.Equal("Solo se puede rechazar un presupuesto en estado Enviado (estado actual: Borrador).", controller.TempData["Error"]);
        }

        // ---------- Duplicar ----------

        [Fact]
        public async Task Duplicar_CreaLaCopiaYRedirigeADetailsDelNuevoIdConTempDataSuccess()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id, estado: EstadoPresupuesto.Aceptado);
            var controller = CrearController(db);

            var resultado = await controller.Duplicar(presupuesto.Id);

            var redirect = Assert.IsType<RedirectToActionResult>(resultado);
            Assert.Equal("Details", redirect.ActionName);
            using var lectura = db.NuevoContexto();
            var copia = lectura.Presupuestos.Single(p => p.Id != presupuesto.Id);
            Assert.Equal(redirect.RouteValues!["id"], copia.Id);
            Assert.Contains(copia.Numero, (string)controller.TempData["Success"]!);
        }

        // DuplicarAsync lanza InvalidOperationException si el id no existe, y el controlador no la
        // captura (no hay try/catch en Duplicar): se propaga tal cual, igual que ya se documenta a
        // nivel de servicio en PresupuestoServiceTests.DuplicarAsync_ConIdInexistente_LanzaExcepcion.
        [Fact]
        public async Task Duplicar_ConIdInexistente_PropagaLaExcepcionSinCapturarla()
        {
            using var db = SqliteContextoDePrueba.Create();
            var controller = CrearController(db);

            await Assert.ThrowsAsync<InvalidOperationException>(() => controller.Duplicar(999));
        }

        // ---------- AgregarLinea ----------

        [Fact]
        public async Task AgregarLinea_ConPresupuestoInexistente_DevuelveNotFound()
        {
            using var db = SqliteContextoDePrueba.Create();
            var controller = CrearController(db);

            var resultado = await controller.AgregarLinea(new LineaPresupuestoFormViewModel { PresupuestoId = 999, ProductoId = 1, Cantidad = 1, PrecioUnitario = 10m });

            Assert.IsType<NotFoundResult>(resultado);
        }

        [Fact]
        public async Task AgregarLinea_ConEstadoDistintoDeBorrador_PonerTempDataErrorYNoAñadeLinea()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var producto = await CrearProductoAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id, estado: EstadoPresupuesto.Enviado);
            var controller = CrearController(db);

            var resultado = await controller.AgregarLinea(new LineaPresupuestoFormViewModel { PresupuestoId = presupuesto.Id, ProductoId = producto.Id, Cantidad = 1, PrecioUnitario = 10m });

            Assert.IsType<RedirectToActionResult>(resultado);
            Assert.Equal("Solo se pueden añadir líneas a un presupuesto en estado Borrador.", controller.TempData["Error"]);
            using var lectura = db.NuevoContexto();
            Assert.Empty(lectura.LineasPresupuesto);
        }

        [Fact]
        public async Task AgregarLinea_ConModelStateInvalido_PonerTempDataErrorYNoAñadeLinea()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var producto = await CrearProductoAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id);
            var controller = CrearController(db);
            controller.ModelState.AddModelError("Cantidad", "La cantidad debe ser mayor que 0");

            var resultado = await controller.AgregarLinea(new LineaPresupuestoFormViewModel { PresupuestoId = presupuesto.Id, ProductoId = producto.Id, Cantidad = 0, PrecioUnitario = 10m });

            Assert.IsType<RedirectToActionResult>(resultado);
            Assert.Equal("Revisa los datos de la línea.", controller.TempData["Error"]);
            using var lectura = db.NuevoContexto();
            Assert.Empty(lectura.LineasPresupuesto);
        }

        [Fact]
        public async Task AgregarLinea_ConDatosValidos_AñadeLaLineaYRecalculaElTotal()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var producto = await CrearProductoAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id);
            var controller = CrearController(db);

            var resultado = await controller.AgregarLinea(new LineaPresupuestoFormViewModel { PresupuestoId = presupuesto.Id, ProductoId = producto.Id, Cantidad = 3, PrecioUnitario = 15m });

            Assert.IsType<RedirectToActionResult>(resultado);
            using var lectura = db.NuevoContexto();
            var linea = Assert.Single(lectura.LineasPresupuesto);
            Assert.Equal(45m, linea.Subtotal);
            Assert.Equal(45m, (await lectura.Presupuestos.FindAsync(presupuesto.Id))!.Total);
        }

        // Hueco real: el controlador toma PrecioUnitario tal cual lo envia el formulario, sin
        // volver a consultar el precio real del producto en el servidor (GetPrecioProducto solo
        // sirve para el auto-relleno via JS en el cliente, no se vuelve a comprobar en el POST).
        // Quien manipule el formulario puede guardar una linea con un precio distinto al de
        // ProductoService.GetByIdAsync(producto.Id).PrecioVenta.
        [Fact]
        public async Task AgregarLinea_ConPrecioUnitarioDistintoAlRealDelProducto_LoAceptaSinValidarloContraElServidor()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var producto = await CrearProductoAsync(db.Context, precioVenta: 100m);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id);
            var controller = CrearController(db);

            await controller.AgregarLinea(new LineaPresupuestoFormViewModel { PresupuestoId = presupuesto.Id, ProductoId = producto.Id, Cantidad = 1, PrecioUnitario = 1m });

            using var lectura = db.NuevoContexto();
            var linea = Assert.Single(lectura.LineasPresupuesto);
            Assert.Equal(1m, linea.PrecioUnitario); // se acepta el precio manipulado, no el real (100)
        }

        // ---------- EliminarLinea ----------

        [Fact]
        public async Task EliminarLinea_ConPresupuestoInexistente_DevuelveNotFound()
        {
            using var db = SqliteContextoDePrueba.Create();
            var controller = CrearController(db);

            var resultado = await controller.EliminarLinea(lineaId: 1, presupuestoId: 999);

            Assert.IsType<NotFoundResult>(resultado);
        }

        [Fact]
        public async Task EliminarLinea_ConEstadoDistintoDeBorrador_PonerTempDataErrorYNoElimina()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var producto = await CrearProductoAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id);
            var contextoLinea = db.NuevoContexto();
            contextoLinea.LineasPresupuesto.Add(new LineaPresupuesto { PresupuestoId = presupuesto.Id, ProductoId = producto.Id, Cantidad = 1, PrecioUnitario = 10m, Subtotal = 10m });
            await contextoLinea.SaveChangesAsync();
            var lineaId = contextoLinea.LineasPresupuesto.Select(l => l.Id).Single();
            // Cambia el estado a Enviado despues de crear la linea, para poder probar el bloqueo.
            var contextoEstado = db.NuevoContexto();
            var presupuestoEnviado = await contextoEstado.Presupuestos.FindAsync(presupuesto.Id);
            presupuestoEnviado!.Estado = EstadoPresupuesto.Enviado;
            await contextoEstado.SaveChangesAsync();
            var controller = CrearController(db);

            var resultado = await controller.EliminarLinea(lineaId, presupuesto.Id);

            Assert.IsType<RedirectToActionResult>(resultado);
            Assert.Equal("Solo se pueden eliminar líneas de un presupuesto en estado Borrador.", controller.TempData["Error"]);
            using var lectura = db.NuevoContexto();
            Assert.Single(lectura.LineasPresupuesto); // sigue existiendo
        }

        [Fact]
        public async Task EliminarLinea_ConEstadoBorrador_EliminaLaLineaYRecalculaElTotal()
        {
            using var db = SqliteContextoDePrueba.Create();
            var cliente = await CrearClienteAsync(db.Context);
            var producto = await CrearProductoAsync(db.Context);
            var presupuesto = await CrearPresupuestoAsync(db.Context, cliente.Id);
            var contextoLinea = db.NuevoContexto();
            contextoLinea.LineasPresupuesto.Add(new LineaPresupuesto { PresupuestoId = presupuesto.Id, ProductoId = producto.Id, Cantidad = 1, PrecioUnitario = 10m, Subtotal = 10m });
            await contextoLinea.SaveChangesAsync();
            var lineaId = contextoLinea.LineasPresupuesto.Select(l => l.Id).Single();
            var controller = CrearController(db);

            var resultado = await controller.EliminarLinea(lineaId, presupuesto.Id);

            Assert.IsType<RedirectToActionResult>(resultado);
            using var lectura = db.NuevoContexto();
            Assert.Empty(lectura.LineasPresupuesto);
            Assert.Equal(0m, (await lectura.Presupuestos.FindAsync(presupuesto.Id))!.Total);
        }

        // ---------- GetPrecioProducto ----------

        [Fact]
        public async Task GetPrecioProducto_ConProductoInexistente_DevuelveNotFound()
        {
            using var db = SqliteContextoDePrueba.Create();
            var controller = CrearController(db);

            var resultado = await controller.GetPrecioProducto(999);

            Assert.IsType<NotFoundResult>(resultado);
        }

        [Fact]
        public async Task GetPrecioProducto_ConProductoExistente_DevuelveElPrecioDeVentaEnJson()
        {
            using var db = SqliteContextoDePrueba.Create();
            var producto = await CrearProductoAsync(db.Context, precioVenta: 42.5m);
            var controller = CrearController(db);

            var resultado = await controller.GetPrecioProducto(producto.Id);

            var json = Assert.IsType<JsonResult>(resultado);
            var precio = (decimal)json.Value!.GetType().GetProperty("precio")!.GetValue(json.Value)!;
            Assert.Equal(42.5m, precio);
        }
    }
}
