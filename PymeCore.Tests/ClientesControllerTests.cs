using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using PymeCore.Controllers;
using PymeCore.Models;
using PymeCore.Services;
using PymeCore.Tests.TestHelpers;
using PymeCore.ViewModels.Clientes;
using Xunit;

namespace PymeCore.Tests
{
    // ITempDataProvider "en memoria" para poder usar TempData en el controlador sin
    // necesidad de una sesion HTTP real ni de una libreria de mocks.
    file sealed class TempDataProviderFalso : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    // Estos tests cubren el flujo del controlador (redirecciones, NotFound, BadRequest, TempData,
    // normalizacion de campos y el error de NIF duplicado anadido a mano). Usan un ClienteService
    // real conectado a un ApplicationDbContext InMemory: no hace falta mockear nada porque
    // ClienteService no es una interfaz. La validacion de DataAnnotations (campos invalidos) se
    // prueba aparte en ClienteFormViewModelValidationTests, ya que el enlazado de modelos que la
    // dispara automaticamente no ocurre al invocar la accion directamente.
    public class ClientesControllerTests
    {
        private static ClientesController CrearController(ClienteService service)
        {
            var controller = new ClientesController(service)
            {
                TempData = new TempDataDictionary(new DefaultHttpContext(), new TempDataProviderFalso())
            };
            return controller;
        }

        private static ClienteFormViewModel VmValido() => new()
        {
            Nombre = "Cliente de Prueba",
            Nif = "12345678a", // en minusculas a proposito, para comprobar el ToUpperInvariant()
            Email = "cliente@test.com",
            Telefono = "600123456",
            Direccion = "  Calle Falsa 123  ", // con espacios, para comprobar el Trim()
            Ciudad = "Madrid"
        };

        // ---------- Details ----------

        [Fact]
        public async Task Details_ConIdInexistente_DevuelveNotFound()
        {
            using var context = DbContextFactory.Create();
            var controller = CrearController(new ClienteService(context));

            var resultado = await controller.Details(999);

            Assert.IsType<NotFoundResult>(resultado);
        }

        [Fact]
        public async Task Details_ConIdExistente_DevuelveViewConCliente()
        {
            using var context = DbContextFactory.Create();
            var service = new ClienteService(context);
            var cliente = new Cliente { Nombre = "X", Nif = "12345678A", Email = "x@test.com", Telefono = "600123456" };
            await service.CreateAsync(cliente);
            var controller = CrearController(service);

            var resultado = await controller.Details(cliente.Id);

            var view = Assert.IsType<ViewResult>(resultado);
            Assert.Equal(cliente.Id, Assert.IsType<Cliente>(view.Model).Id);
        }

        // ---------- Create ----------

        [Fact]
        public async Task Create_Post_ConDatosValidos_GuardaClienteNormalizadoYRedirigeAIndex()
        {
            using var context = DbContextFactory.Create();
            var service = new ClienteService(context);
            var controller = CrearController(service);

            var resultado = await controller.Create(VmValido());

            var redirect = Assert.IsType<RedirectToActionResult>(resultado);
            Assert.Equal("Index", redirect.ActionName);
            var guardado = Assert.Single(context.Clientes);
            Assert.Equal("12345678A", guardado.Nif); // normalizado a mayusculas
            Assert.Equal("Calle Falsa 123", guardado.Direccion); // normalizado con Trim
        }

        [Fact]
        public async Task Create_Post_ConNifYaExistente_AnadeErrorModelStateYDevuelveView()
        {
            using var context = DbContextFactory.Create();
            var service = new ClienteService(context);
            await service.CreateAsync(new Cliente { Nombre = "Otro", Nif = "12345678A", Email = "otro@test.com", Telefono = "600111222" });
            var controller = CrearController(service);

            var resultado = await controller.Create(VmValido()); // mismo NIF, distinto case

            var view = Assert.IsType<ViewResult>(resultado);
            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey(nameof(ClienteFormViewModel.Nif)));
            Assert.Equal(1, context.Clientes.Count()); // no se ha creado el segundo cliente
        }

        // ---------- Edit ----------

        [Fact]
        public async Task Edit_Get_ConIdInexistente_DevuelveNotFound()
        {
            using var context = DbContextFactory.Create();
            var controller = CrearController(new ClienteService(context));

            var resultado = await controller.Edit(999);

            Assert.IsType<NotFoundResult>(resultado);
        }

        [Fact]
        public async Task Edit_Post_ConIdDistintoAlDelViewModel_DevuelveBadRequest()
        {
            using var context = DbContextFactory.Create();
            var controller = CrearController(new ClienteService(context));
            var vm = VmValido();
            vm.Id = 5;

            var resultado = await controller.Edit(id: 999, vm: vm);

            Assert.IsType<BadRequestResult>(resultado);
        }

        [Fact]
        public async Task Edit_Post_ConDatosValidos_ActualizaClienteYRedirigeAIndex()
        {
            using var context = DbContextFactory.Create();
            var service = new ClienteService(context);
            var cliente = new Cliente { Nombre = "Original", Nif = "12345678A", Email = "original@test.com", Telefono = "600111222" };
            await service.CreateAsync(cliente);
            var controller = CrearController(service);

            var vm = VmValido();
            vm.Id = cliente.Id;
            vm.Nombre = "Nombre Editado";

            var resultado = await controller.Edit(cliente.Id, vm);

            Assert.IsType<RedirectToActionResult>(resultado);
            var actualizado = await service.GetByIdAsync(cliente.Id);
            Assert.Equal("Nombre Editado", actualizado!.Nombre);
        }

        [Fact]
        public async Task Edit_Post_ConNifDeOtroClienteExistente_AnadeErrorModelState()
        {
            using var context = DbContextFactory.Create();
            var service = new ClienteService(context);
            await service.CreateAsync(new Cliente { Nombre = "Uno", Nif = "12345678A", Email = "uno@test.com", Telefono = "600111222" });
            var otro = new Cliente { Nombre = "Dos", Nif = "87654321B", Email = "dos@test.com", Telefono = "600333444" };
            await service.CreateAsync(otro);
            var controller = CrearController(service);

            var vm = VmValido(); // Nif "12345678a" -> normalizado a "12345678A", ya usado por "Uno"
            vm.Id = otro.Id;

            var resultado = await controller.Edit(otro.Id, vm);

            Assert.IsType<ViewResult>(resultado);
            Assert.False(controller.ModelState.IsValid);
            var sinCambios = await service.GetByIdAsync(otro.Id);
            Assert.Equal("87654321B", sinCambios!.Nif); // no se sobrescribe con el NIF duplicado
        }

        // ---------- Deactivate / Activate (no existe borrado fisico) ----------

        [Fact]
        public async Task Deactivate_ConIdExistente_PonerActivoFalseYRedirigeConTempDataWarning()
        {
            using var context = DbContextFactory.Create();
            var service = new ClienteService(context);
            var cliente = new Cliente { Nombre = "X", Nif = "12345678A", Email = "x@test.com", Telefono = "600123456" };
            await service.CreateAsync(cliente);
            var controller = CrearController(service);

            var resultado = await controller.Deactivate(cliente.Id);

            Assert.IsType<RedirectToActionResult>(resultado);
            Assert.Equal("Cliente desactivado.", controller.TempData["Warning"]);
            var actualizado = await service.GetByIdAsync(cliente.Id);
            Assert.False(actualizado!.Activo);
        }

        [Fact]
        public async Task Deactivate_ConIdInexistente_RedirigeConTempDataError()
        {
            using var context = DbContextFactory.Create();
            var controller = CrearController(new ClienteService(context));

            var resultado = await controller.Deactivate(999);

            Assert.IsType<RedirectToActionResult>(resultado);
            Assert.Equal("Cliente no encontrado.", controller.TempData["Error"]);
        }

        [Fact]
        public async Task Activate_ConIdExistente_PonerActivoTrueYRedirigeConTempDataSuccess()
        {
            using var context = DbContextFactory.Create();
            var service = new ClienteService(context);
            var cliente = new Cliente { Nombre = "X", Nif = "12345678A", Email = "x@test.com", Telefono = "600123456" };
            await service.CreateAsync(cliente);
            await service.DeactivateAsync(cliente.Id);
            var controller = CrearController(service);

            var resultado = await controller.Activate(cliente.Id);

            Assert.IsType<RedirectToActionResult>(resultado);
            Assert.Equal("Cliente activado.", controller.TempData["Success"]);
            var actualizado = await service.GetByIdAsync(cliente.Id);
            Assert.True(actualizado!.Activo);
        }
    }
}
