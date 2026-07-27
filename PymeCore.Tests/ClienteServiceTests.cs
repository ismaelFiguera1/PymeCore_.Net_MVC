using Microsoft.EntityFrameworkCore;
using PymeCore.Models;
using PymeCore.Services;
using PymeCore.Tests.TestHelpers;
using Xunit;

namespace PymeCore.Tests
{
    public class ClienteServiceTests
    {
        private static Cliente ClienteValido(string nif = "12345678A", string email = "cliente@test.com") => new()
        {
            Nombre = "Cliente de Prueba",
            Nif = nif,
            Email = email,
            Telefono = "600123456",
            Direccion = "Calle Falsa 123",
            Ciudad = "Madrid",
            Activo = true
        };

        // ---------- Creacion correcta ----------

        [Fact]
        public async Task CreateAsync_ConDatosValidos_GuardaClienteYDevuelveOk()
        {
            using var context = DbContextFactory.Create();
            var service = new ClienteService(context);
            var cliente = ClienteValido();

            var (ok, error) = await service.CreateAsync(cliente);

            Assert.True(ok);
            Assert.Null(error);
            Assert.NotEqual(0, cliente.Id);
            Assert.Equal(1, context.Clientes.Count());
        }

        // No existe ninguna regla de unicidad para Email (ni indice en ApplicationDbContext,
        // ni comprobacion en el servicio como la que si tiene el Nif). Este test documenta ese
        // comportamiento actual: sirve de aviso si en el futuro se anade la regla y este test empieza a fallar.
        [Fact]
        public async Task CreateAsync_PermiteEmailDuplicado_PorNoExistirReglaDeUnicidad()
        {
            using var context = DbContextFactory.Create();
            var service = new ClienteService(context);

            var (ok1, _) = await service.CreateAsync(ClienteValido(nif: "12345678A", email: "repetido@test.com"));
            var (ok2, _) = await service.CreateAsync(ClienteValido(nif: "87654321B", email: "repetido@test.com"));

            Assert.True(ok1);
            Assert.True(ok2);
            Assert.Equal(2, context.Clientes.Count());
        }

        // ---------- Duplicado de NIF ----------

        [Fact]
        public async Task ExisteNifAsync_ConNifYaRegistrado_DevuelveTrue()
        {
            using var context = DbContextFactory.Create();
            var service = new ClienteService(context);
            await service.CreateAsync(ClienteValido(nif: "12345678A"));

            var existe = await service.ExisteNifAsync("12345678A");

            Assert.True(existe);
        }

        [Fact]
        public async Task ExisteNifAsync_ConNifNoRegistrado_DevuelveFalse()
        {
            using var context = DbContextFactory.Create();
            var service = new ClienteService(context);
            await service.CreateAsync(ClienteValido(nif: "12345678A"));

            var existe = await service.ExisteNifAsync("87654321B");

            Assert.False(existe);
        }

        // Escenario de edicion: al excluir el propio Id, un cliente no debe detectarse
        // a si mismo como duplicado al conservar su NIF actual.
        [Fact]
        public async Task ExisteNifAsync_AlExcluirElPropioId_NoSeDetectaComoDuplicado()
        {
            using var context = DbContextFactory.Create();
            var service = new ClienteService(context);
            var cliente = ClienteValido(nif: "12345678A");
            await service.CreateAsync(cliente);

            var existe = await service.ExisteNifAsync("12345678A", excludeId: cliente.Id);

            Assert.False(existe);
        }

        // ---------- Busqueda ----------

        [Fact]
        public async Task GetAllAsync_SinFiltro_DevuelveTodosOrdenadosPorNombre()
        {
            using var context = DbContextFactory.Create();
            var service = new ClienteService(context);
            await service.CreateAsync(ClienteValido(nif: "11111111A"));
            var clienteZ = ClienteValido(nif: "22222222B");
            clienteZ.Nombre = "Zeta SL";
            var clienteA = ClienteValido(nif: "33333333C");
            clienteA.Nombre = "Alfa SL";
            await service.CreateAsync(clienteZ);
            await service.CreateAsync(clienteA);

            var resultado = await service.GetAllAsync();

            Assert.Equal(3, resultado.Count);
            Assert.Equal("Alfa SL", resultado[0].Nombre);
        }

        [Fact]
        public async Task GetAllAsync_ConBuscarPorNombreParcialYMinusculas_FiltraCoincidencias()
        {
            using var context = DbContextFactory.Create();
            var service = new ClienteService(context);
            var construcciones = ClienteValido(nif: "11111111A");
            construcciones.Nombre = "Construcciones Perez";
            var otra = ClienteValido(nif: "22222222B");
            otra.Nombre = "Otra Empresa";
            await service.CreateAsync(construcciones);
            await service.CreateAsync(otra);

            var resultado = await service.GetAllAsync("construc");

            Assert.Single(resultado);
            Assert.Equal("Construcciones Perez", resultado[0].Nombre);
        }

        [Fact]
        public async Task GetAllAsync_ConBuscarPorNif_FiltraCoincidencias()
        {
            using var context = DbContextFactory.Create();
            var service = new ClienteService(context);
            await service.CreateAsync(ClienteValido(nif: "12345678A"));
            await service.CreateAsync(ClienteValido(nif: "87654321B"));

            var resultado = await service.GetAllAsync("12345678a");

            Assert.Single(resultado);
            Assert.Equal("12345678A", resultado[0].Nif);
        }

        // ---------- Restriccion real de EF/BD: indice unico de Nif ----------
        // Estos dos tests no pasan por ExisteNifAsync: insertan directamente contra el DbContext
        // para comprobar el indice unico configurado en ApplicationDbContext.OnModelCreating
        // (builder.Entity<Cliente>().HasIndex(c => c.Nif).IsUnique()), que es la restriccion real
        // de persistencia, independiente de que el servicio la compruebe antes de guardar.
        //
        // Usan SqliteContextoDePrueba (SQLite en memoria) en vez de DbContextFactory (InMemory de EF
        // Core), porque el proveedor InMemory NO hace cumplir indices unicos secundarios (solo la
        // clave primaria) y estos tests fallarian sin excepcion aunque la restriccion este bien
        // configurada. SQLite si crea un esquema relacional real y aplica el indice de verdad.

        [Fact]
        public async Task IndiceUnicoDeNif_RechazaDosClientesConElMismoNifAlGuardarEnElContexto()
        {
            using var db = SqliteContextoDePrueba.Create();
            db.Context.Clientes.Add(ClienteValido(nif: "12345678A"));
            await db.Context.SaveChangesAsync();

            db.Context.Clientes.Add(ClienteValido(nif: "12345678A", email: "otro@test.com"));

            await Assert.ThrowsAsync<DbUpdateException>(() => db.Context.SaveChangesAsync());
        }

        // Con SQLite, la excepcion que dispara el indice unico lleva una SqliteException como
        // InnerException, no una PostgresException (eso solo pasa contra Postgres real), asi que el
        // filtro del catch en CreateAsync ("when (ex.InnerException is PostgresException { SqlState:
        // "23505" })") no coincide y la excepcion se propaga sin capturar. Este test documenta esa
        // diferencia: si el llamador se salta ExisteNifAsync, aqui CreateAsync lanza DbUpdateException
        // en vez de devolver (false, "Ya existe un cliente con este NIF."); contra Postgres real si
        // devolveria ese error controlado.
        [Fact]
        public async Task CreateAsync_SiSeSaltaLaComprobacionPreviaDeNif_PropagaLaExcepcionSinCapturarla()
        {
            using var db = SqliteContextoDePrueba.Create();
            var service = new ClienteService(db.Context);
            await service.CreateAsync(ClienteValido(nif: "12345678A"));

            await Assert.ThrowsAsync<DbUpdateException>(
                () => service.CreateAsync(ClienteValido(nif: "12345678A", email: "otro@test.com")));
        }

        // ---------- Consulta por Id ----------

        [Fact]
        public async Task GetByIdAsync_ConIdInexistente_DevuelveNull()
        {
            using var context = DbContextFactory.Create();
            var service = new ClienteService(context);

            var resultado = await service.GetByIdAsync(999);

            Assert.Null(resultado);
        }

        // ---------- Edicion ----------

        [Fact]
        public async Task UpdateAsync_ConDatosValidos_PersisteLosCambios()
        {
            using var context = DbContextFactory.Create();
            var service = new ClienteService(context);
            var cliente = ClienteValido();
            await service.CreateAsync(cliente);

            cliente.Nombre = "Nombre Actualizado";
            cliente.Telefono = "611222333";
            var (ok, error) = await service.UpdateAsync(cliente);

            Assert.True(ok);
            Assert.Null(error);
            var actualizado = await service.GetByIdAsync(cliente.Id);
            Assert.Equal("Nombre Actualizado", actualizado!.Nombre);
            Assert.Equal("611222333", actualizado.Telefono);
        }

        // ---------- Desactivacion / activacion (no existe borrado fisico) ----------

        [Fact]
        public async Task DeactivateAsync_ConIdExistente_PonerActivoAFalse()
        {
            using var context = DbContextFactory.Create();
            var service = new ClienteService(context);
            var cliente = ClienteValido();
            await service.CreateAsync(cliente);

            var (ok, error) = await service.DeactivateAsync(cliente.Id);

            Assert.True(ok);
            Assert.Null(error);
            var actualizado = await service.GetByIdAsync(cliente.Id);
            Assert.False(actualizado!.Activo);
        }

        [Fact]
        public async Task DeactivateAsync_ConIdInexistente_DevuelveError()
        {
            using var context = DbContextFactory.Create();
            var service = new ClienteService(context);

            var (ok, error) = await service.DeactivateAsync(999);

            Assert.False(ok);
            Assert.Equal("Cliente no encontrado.", error);
        }

        [Fact]
        public async Task ActivateAsync_ConIdExistente_PonerActivoATrue()
        {
            using var context = DbContextFactory.Create();
            var service = new ClienteService(context);
            var cliente = ClienteValido();
            await service.CreateAsync(cliente);
            await service.DeactivateAsync(cliente.Id);

            var (ok, error) = await service.ActivateAsync(cliente.Id);

            Assert.True(ok);
            Assert.Null(error);
            var actualizado = await service.GetByIdAsync(cliente.Id);
            Assert.True(actualizado!.Activo);
        }

        [Fact]
        public async Task ActivateAsync_ConIdInexistente_DevuelveError()
        {
            using var context = DbContextFactory.Create();
            var service = new ClienteService(context);

            var (ok, error) = await service.ActivateAsync(999);

            Assert.False(ok);
            Assert.Equal("Cliente no encontrado.", error);
        }
    }
}
