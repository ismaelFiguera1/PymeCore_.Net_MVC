using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using PymeCore.Controllers;
using PymeCore.Models;
using PymeCore.Services;
using PymeCore.Tests.TestHelpers;
using PymeCore.ViewModels.Proveedores;
using Xunit;

namespace PymeCore.Tests
{
    // ITempDataProvider "en memoria" propio de este archivo (mismo patron que TempDataProviderFalso
    // de ClientesControllerTests, pero declarado aqui tambien porque es "file" y no se puede
    // compartir entre archivos sin sacarlo a una clase publica).
    file sealed class TempDataProviderFalsoProveedores : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    // Mismo enfoque que ClientesControllerTests: ProveedorService real conectado a un
    // ApplicationDbContext InMemory, sin necesidad de mocks. La validacion de DataAnnotations se
    // prueba aparte en ProveedorFormViewModelValidationTests.
    public class ProveedoresControllerTests
    {
        private static ProveedoresController CrearController(ProveedorService service)
        {
            var controller = new ProveedoresController(service)
            {
                TempData = new TempDataDictionary(new DefaultHttpContext(), new TempDataProviderFalsoProveedores())
            };
            return controller;
        }

        private static ProveedorFormViewModel VmValido() => new()
        {
            Nombre = "Proveedor de Prueba",
            Cif = "12345678a", // en minusculas a proposito, para comprobar el ToUpperInvariant()
            PersonaContacto = "  Juan Perez  ", // con espacios, para comprobar el Trim()
            Email = "proveedor@test.com",
            Telefono = "600123456"
        };

        // ---------- Details ----------

        [Fact]
        public async Task Details_ConIdInexistente_DevuelveNotFound()
        {
            using var context = DbContextFactory.Create();
            var controller = CrearController(new ProveedorService(context));

            var resultado = await controller.Details(999);

            Assert.IsType<NotFoundResult>(resultado);
        }

        [Fact]
        public async Task Details_ConIdExistente_DevuelveViewConProveedor()
        {
            using var context = DbContextFactory.Create();
            var service = new ProveedorService(context);
            var proveedor = new Proveedor { Nombre = "X", Cif = "12345678A", Email = "x@test.com" };
            await service.CreateAsync(proveedor);
            var controller = CrearController(service);

            var resultado = await controller.Details(proveedor.Id);

            var view = Assert.IsType<ViewResult>(resultado);
            Assert.Equal(proveedor.Id, Assert.IsType<Proveedor>(view.Model).Id);
        }

        // ---------- Create ----------

        [Fact]
        public async Task Create_Post_ConDatosValidos_GuardaProveedorNormalizadoYRedirigeAIndex()
        {
            using var context = DbContextFactory.Create();
            var service = new ProveedorService(context);
            var controller = CrearController(service);

            var resultado = await controller.Create(VmValido());

            var redirect = Assert.IsType<RedirectToActionResult>(resultado);
            Assert.Equal("Index", redirect.ActionName);
            var guardado = Assert.Single(context.Proveedores);
            Assert.Equal("12345678A", guardado.Cif); // normalizado a mayusculas
            Assert.Equal("Juan Perez", guardado.PersonaContacto); // normalizado con Trim
        }

        // Diferencia real frente a ClientesController: aqui Create SI pone TempData["Success"] al
        // guardar. Si no se comprobara, un cambio futuro que quite ese aviso no se detectaria.
        [Fact]
        public async Task Create_Post_ConDatosValidos_EstableceTempDataSuccess()
        {
            using var context = DbContextFactory.Create();
            var controller = CrearController(new ProveedorService(context));

            await controller.Create(VmValido());

            Assert.Equal("Proveedor creado.", controller.TempData["Success"]);
        }

        [Fact]
        public async Task Create_Post_ConCifYaExistente_AnadeErrorModelStateYDevuelveView()
        {
            using var context = DbContextFactory.Create();
            var service = new ProveedorService(context);
            await service.CreateAsync(new Proveedor { Nombre = "Otro", Cif = "12345678A", Email = "otro@test.com" });
            var controller = CrearController(service);

            var resultado = await controller.Create(VmValido()); // mismo CIF, distinto case

            var view = Assert.IsType<ViewResult>(resultado);
            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey(nameof(ProveedorFormViewModel.Cif)));
            Assert.Equal(1, context.Proveedores.Count()); // no se ha creado el segundo proveedor
        }

        // ---------- Edit ----------

        [Fact]
        public async Task Edit_Get_ConIdInexistente_DevuelveNotFound()
        {
            using var context = DbContextFactory.Create();
            var controller = CrearController(new ProveedorService(context));

            var resultado = await controller.Edit(999);

            Assert.IsType<NotFoundResult>(resultado);
        }

        [Fact]
        public async Task Edit_Post_ConIdDistintoAlDelViewModel_DevuelveBadRequest()
        {
            using var context = DbContextFactory.Create();
            var controller = CrearController(new ProveedorService(context));
            var vm = VmValido();
            vm.Id = 5;

            var resultado = await controller.Edit(id: 999, vm: vm);

            Assert.IsType<BadRequestResult>(resultado);
        }

        [Fact]
        public async Task Edit_Post_ConDatosValidos_ActualizaProveedorYRedirigeAIndex()
        {
            using var context = DbContextFactory.Create();
            var service = new ProveedorService(context);
            var proveedor = new Proveedor { Nombre = "Original", Cif = "12345678A", Email = "original@test.com" };
            await service.CreateAsync(proveedor);
            var controller = CrearController(service);

            var vm = VmValido();
            vm.Id = proveedor.Id;
            vm.Nombre = "Nombre Editado";

            var resultado = await controller.Edit(proveedor.Id, vm);

            Assert.IsType<RedirectToActionResult>(resultado);
            var actualizado = await service.GetByIdAsync(proveedor.Id);
            Assert.Equal("Nombre Editado", actualizado!.Nombre);
        }

        [Fact]
        public async Task Edit_Post_ConCifDeOtroProveedorExistente_AnadeErrorModelState()
        {
            using var context = DbContextFactory.Create();
            var service = new ProveedorService(context);
            await service.CreateAsync(new Proveedor { Nombre = "Uno", Cif = "12345678A", Email = "uno@test.com" });
            var otro = new Proveedor { Nombre = "Dos", Cif = "87654321B", Email = "dos@test.com" };
            await service.CreateAsync(otro);
            var controller = CrearController(service);

            var vm = VmValido(); // Cif "12345678a" -> normalizado a "12345678A", ya usado por "Uno"
            vm.Id = otro.Id;

            var resultado = await controller.Edit(otro.Id, vm);

            Assert.IsType<ViewResult>(resultado);
            Assert.False(controller.ModelState.IsValid);
            var sinCambios = await service.GetByIdAsync(otro.Id);
            Assert.Equal("87654321B", sinCambios!.Cif); // no se sobrescribe con el CIF duplicado
        }

        // ---------- Deactivate / Activate (no existe borrado fisico) ----------

        [Fact]
        public async Task Deactivate_ConIdExistente_PonerActivoFalseYRedirigeConTempDataWarning()
        {
            using var context = DbContextFactory.Create();
            var service = new ProveedorService(context);
            var proveedor = new Proveedor { Nombre = "X", Cif = "12345678A", Email = "x@test.com" };
            await service.CreateAsync(proveedor);
            var controller = CrearController(service);

            var resultado = await controller.Deactivate(proveedor.Id);

            Assert.IsType<RedirectToActionResult>(resultado);
            Assert.Equal("Proveedor desactivado.", controller.TempData["Warning"]);
            var actualizado = await service.GetByIdAsync(proveedor.Id);
            Assert.False(actualizado!.Activo);
        }

        [Fact]
        public async Task Deactivate_ConIdInexistente_RedirigeConTempDataError()
        {
            using var context = DbContextFactory.Create();
            var controller = CrearController(new ProveedorService(context));

            var resultado = await controller.Deactivate(999);

            Assert.IsType<RedirectToActionResult>(resultado);
            Assert.Equal("Proveedor no encontrado.", controller.TempData["Error"]);
        }

        [Fact]
        public async Task Activate_ConIdExistente_PonerActivoTrueYRedirigeConTempDataSuccess()
        {
            using var context = DbContextFactory.Create();
            var service = new ProveedorService(context);
            var proveedor = new Proveedor { Nombre = "X", Cif = "12345678A", Email = "x@test.com" };
            await service.CreateAsync(proveedor);
            await service.DeactivateAsync(proveedor.Id);
            var controller = CrearController(service);

            var resultado = await controller.Activate(proveedor.Id);

            Assert.IsType<RedirectToActionResult>(resultado);
            Assert.Equal("Proveedor activado.", controller.TempData["Success"]);
            var actualizado = await service.GetByIdAsync(proveedor.Id);
            Assert.True(actualizado!.Activo);
        }
    }
}
