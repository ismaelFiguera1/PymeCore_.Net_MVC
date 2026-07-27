using Microsoft.EntityFrameworkCore;
using PymeCore.Models;
using PymeCore.Services;
using PymeCore.Tests.TestHelpers;
using Xunit;

namespace PymeCore.Tests
{
    public class ProveedorServiceTests
    {
        private static Proveedor ProveedorValido(string cif = "12345678A", string email = "proveedor@test.com") => new()
        {
            Nombre = "Proveedor de Prueba",
            Cif = cif,
            PersonaContacto = "Juan Perez",
            Email = email,
            Telefono = "600123456",
            Activo = true
        };

        // ---------- Creacion correcta ----------

        [Fact]
        public async Task CreateAsync_ConDatosValidos_GuardaProveedorYDevuelveOk()
        {
            using var context = DbContextFactory.Create();
            var service = new ProveedorService(context);
            var proveedor = ProveedorValido();

            var (ok, error) = await service.CreateAsync(proveedor);

            Assert.True(ok);
            Assert.Null(error);
            Assert.NotEqual(0, proveedor.Id);
            Assert.Equal(1, context.Proveedores.Count());
        }

        // Igual que con Cliente, no existe ninguna regla de unicidad para Email en Proveedor
        // (ni indice en ApplicationDbContext, ni comprobacion en el servicio). Este test documenta
        // ese comportamiento actual.
        [Fact]
        public async Task CreateAsync_PermiteEmailDuplicado_PorNoExistirReglaDeUnicidad()
        {
            using var context = DbContextFactory.Create();
            var service = new ProveedorService(context);

            var (ok1, _) = await service.CreateAsync(ProveedorValido(cif: "12345678A", email: "repetido@test.com"));
            var (ok2, _) = await service.CreateAsync(ProveedorValido(cif: "87654321B", email: "repetido@test.com"));

            Assert.True(ok1);
            Assert.True(ok2);
            Assert.Equal(2, context.Proveedores.Count());
        }

        // ---------- Duplicado de CIF ----------

        [Fact]
        public async Task ExisteCifAsync_ConCifYaRegistrado_DevuelveTrue()
        {
            using var context = DbContextFactory.Create();
            var service = new ProveedorService(context);
            await service.CreateAsync(ProveedorValido(cif: "12345678A"));

            var existe = await service.ExisteCifAsync("12345678A");

            Assert.True(existe);
        }

        [Fact]
        public async Task ExisteCifAsync_ConCifNoRegistrado_DevuelveFalse()
        {
            using var context = DbContextFactory.Create();
            var service = new ProveedorService(context);
            await service.CreateAsync(ProveedorValido(cif: "12345678A"));

            var existe = await service.ExisteCifAsync("87654321B");

            Assert.False(existe);
        }

        [Fact]
        public async Task ExisteCifAsync_AlExcluirElPropioId_NoSeDetectaComoDuplicado()
        {
            using var context = DbContextFactory.Create();
            var service = new ProveedorService(context);
            var proveedor = ProveedorValido(cif: "12345678A");
            await service.CreateAsync(proveedor);

            var existe = await service.ExisteCifAsync("12345678A", excludeId: proveedor.Id);

            Assert.False(existe);
        }

        // ---------- Busqueda ----------

        [Fact]
        public async Task GetAllAsync_SinFiltro_DevuelveTodosOrdenadosPorNombre()
        {
            using var context = DbContextFactory.Create();
            var service = new ProveedorService(context);
            var proveedorZ = ProveedorValido(cif: "11111111A");
            proveedorZ.Nombre = "Zeta SL";
            var proveedorA = ProveedorValido(cif: "22222222B");
            proveedorA.Nombre = "Alfa SL";
            await service.CreateAsync(proveedorZ);
            await service.CreateAsync(proveedorA);

            var resultado = await service.GetAllAsync();

            Assert.Equal(2, resultado.Count);
            Assert.Equal("Alfa SL", resultado[0].Nombre);
        }

        [Fact]
        public async Task GetAllAsync_ConBuscarPorNombreParcialYMinusculas_FiltraCoincidencias()
        {
            using var context = DbContextFactory.Create();
            var service = new ProveedorService(context);
            var construcciones = ProveedorValido(cif: "11111111A");
            construcciones.Nombre = "Suministros Perez";
            var otro = ProveedorValido(cif: "22222222B");
            otro.Nombre = "Otra Empresa";
            await service.CreateAsync(construcciones);
            await service.CreateAsync(otro);

            var resultado = await service.GetAllAsync("suminis");

            Assert.Single(resultado);
            Assert.Equal("Suministros Perez", resultado[0].Nombre);
        }

        [Fact]
        public async Task GetAllAsync_ConBuscarPorCif_FiltraCoincidencias()
        {
            using var context = DbContextFactory.Create();
            var service = new ProveedorService(context);
            await service.CreateAsync(ProveedorValido(cif: "12345678A"));
            await service.CreateAsync(ProveedorValido(cif: "87654321B"));

            var resultado = await service.GetAllAsync("12345678a");

            Assert.Single(resultado);
            Assert.Equal("12345678A", resultado[0].Cif);
        }

        // ---------- Restriccion real de EF/BD: indice unico de Cif ----------
        // Mismo motivo que en ClienteServiceTests: el proveedor InMemory de EF Core no aplica
        // indices unicos secundarios, solo la clave primaria. Se usa SQLite en memoria para
        // comprobar de verdad builder.Entity<Proveedor>().HasIndex(p => p.Cif).IsUnique().

        [Fact]
        public async Task IndiceUnicoDeCif_RechazaDosProveedoresConElMismoCifAlGuardarEnElContexto()
        {
            using var db = SqliteContextoDePrueba.Create();
            db.Context.Proveedores.Add(ProveedorValido(cif: "12345678A"));
            await db.Context.SaveChangesAsync();

            db.Context.Proveedores.Add(ProveedorValido(cif: "12345678A", email: "otro@test.com"));

            await Assert.ThrowsAsync<DbUpdateException>(() => db.Context.SaveChangesAsync());
        }

        // Igual que en Cliente: con SQLite, la excepcion no lleva una PostgresException como
        // InnerException, asi que el filtro del catch en CreateAsync no coincide y la excepcion
        // se propaga sin capturar. Contra Postgres real si devolveria el error controlado.
        [Fact]
        public async Task CreateAsync_SiSeSaltaLaComprobacionPreviaDeCif_PropagaLaExcepcionSinCapturarla()
        {
            using var db = SqliteContextoDePrueba.Create();
            var service = new ProveedorService(db.Context);
            await service.CreateAsync(ProveedorValido(cif: "12345678A"));

            await Assert.ThrowsAsync<DbUpdateException>(
                () => service.CreateAsync(ProveedorValido(cif: "12345678A", email: "otro@test.com")));
        }

        // ---------- Consulta por Id ----------

        [Fact]
        public async Task GetByIdAsync_ConIdInexistente_DevuelveNull()
        {
            using var context = DbContextFactory.Create();
            var service = new ProveedorService(context);

            var resultado = await service.GetByIdAsync(999);

            Assert.Null(resultado);
        }

        // ---------- Edicion ----------

        [Fact]
        public async Task UpdateAsync_ConDatosValidos_PersisteLosCambios()
        {
            using var context = DbContextFactory.Create();
            var service = new ProveedorService(context);
            var proveedor = ProveedorValido();
            await service.CreateAsync(proveedor);

            proveedor.Nombre = "Nombre Actualizado";
            proveedor.Telefono = "611222333";
            var (ok, error) = await service.UpdateAsync(proveedor);

            Assert.True(ok);
            Assert.Null(error);
            var actualizado = await service.GetByIdAsync(proveedor.Id);
            Assert.Equal("Nombre Actualizado", actualizado!.Nombre);
            Assert.Equal("611222333", actualizado.Telefono);
        }

        // ---------- Desactivacion / activacion (no existe borrado fisico) ----------

        [Fact]
        public async Task DeactivateAsync_ConIdExistente_PonerActivoAFalse()
        {
            using var context = DbContextFactory.Create();
            var service = new ProveedorService(context);
            var proveedor = ProveedorValido();
            await service.CreateAsync(proveedor);

            var (ok, error) = await service.DeactivateAsync(proveedor.Id);

            Assert.True(ok);
            Assert.Null(error);
            var actualizado = await service.GetByIdAsync(proveedor.Id);
            Assert.False(actualizado!.Activo);
        }

        [Fact]
        public async Task DeactivateAsync_ConIdInexistente_DevuelveError()
        {
            using var context = DbContextFactory.Create();
            var service = new ProveedorService(context);

            var (ok, error) = await service.DeactivateAsync(999);

            Assert.False(ok);
            Assert.Equal("Proveedor no encontrado.", error);
        }

        [Fact]
        public async Task ActivateAsync_ConIdExistente_PonerActivoATrue()
        {
            using var context = DbContextFactory.Create();
            var service = new ProveedorService(context);
            var proveedor = ProveedorValido();
            await service.CreateAsync(proveedor);
            await service.DeactivateAsync(proveedor.Id);

            var (ok, error) = await service.ActivateAsync(proveedor.Id);

            Assert.True(ok);
            Assert.Null(error);
            var actualizado = await service.GetByIdAsync(proveedor.Id);
            Assert.True(actualizado!.Activo);
        }

        [Fact]
        public async Task ActivateAsync_ConIdInexistente_DevuelveError()
        {
            using var context = DbContextFactory.Create();
            var service = new ProveedorService(context);

            var (ok, error) = await service.ActivateAsync(999);

            Assert.False(ok);
            Assert.Equal("Proveedor no encontrado.", error);
        }
    }
}
