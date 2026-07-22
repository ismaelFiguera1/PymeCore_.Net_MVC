using Microsoft.EntityFrameworkCore;
using Npgsql;
using PymeCore.Data;
using PymeCore.Models;

namespace PymeCore.Services
{
    public class ClienteService
    {
        private readonly ApplicationDbContext _context;

        public ClienteService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<Cliente>> GetAllAsync(string? buscar = null)
        {
            // AsQueryable() construye una consulta diferida (IQueryable<Cliente>): todavía no viaja a la BD,
            // solo se define/compone. La ejecución real ocurre en el ToListAsync() final.
            var query = _context.Clientes.AsQueryable();

            if (!string.IsNullOrWhiteSpace(buscar))
            {
                // Se guarda el término ya en minúsculas una sola vez, para no repetir ToLower() en cada comparación
                var termino = buscar.ToLower();
                // Where() añade la condición a la query (aún sin ejecutar):
                // compara Nombre y Nif en minúsculas contra el término (case-insensitive),
                // y Contains() hace coincidencia parcial (no exige que el texto empiece o termine igual).
                // Es un OR: basta con que coincida en Nombre o en Nif para que el cliente entre en el resultado.
                query = query.Where(c =>
                    c.Nombre.ToLower().Contains(termino) ||
                    c.Nif.ToLower().Contains(termino));
            }

            return await query.OrderBy(c => c.Nombre).ToListAsync();
        }

        public async Task<Cliente?> GetByIdAsync(int id)
        {
            return await _context.Clientes.FindAsync(id);
        }

        // Comprueba si ya existe otro cliente con el mismo NIF.
        // En Create, excludeId vale 0, así que no se excluye ningún cliente real.
        // En Edit, se pasa el Id del cliente actual para no detectarlo a él mismo como duplicado.
        // Devuelve true solo si encuentra un cliente con ese NIF y con un Id distinto al excluido.
        public async Task<bool> ExisteNifAsync(string nif, int excludeId = 0)
        {
            return await _context.Clientes
                .AnyAsync(c => c.Nif == nif && c.Id != excludeId);
        }

        public async Task<(bool Ok, string? Error)> CreateAsync(Cliente cliente)
        {
            try
            {
                _context.Clientes.Add(cliente);
                await _context.SaveChangesAsync();
                return (true, null);
            }
            // 23505 = unique_violation en Postgres (índice único de Nif). Red de seguridad por si dos
            // requests pasan ExisteNifAsync casi a la vez: aquí es la BD la que impide el NIF duplicado.
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
            {
                return (false, "Ya existe un cliente con este NIF.");
            }
        }

        public async Task<(bool Ok, string? Error)> UpdateAsync(Cliente cliente)
        {
            try
            {
                _context.Clientes.Update(cliente);
                await _context.SaveChangesAsync();
                return (true, null);
            }
            // Solo salta si el UPDATE deja a otra fila distinta con el mismo Nif (p.ej. carrera entre
            // dos ediciones simultáneas). Si el cliente conserva su propio Nif, no hay colisión.
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
            {
                return (false, "Ya existe un cliente con este NIF.");
            }
        }

        public async Task<(bool Ok, string? Error)> DeactivateAsync(int id)
        {
            var cliente = await _context.Clientes.FindAsync(id);
            if (cliente is null) return (false, "Cliente no encontrado.");

            cliente.Activo = false;
            await _context.SaveChangesAsync();
            return (true, null);
        }

        public async Task<(bool Ok, string? Error)> ActivateAsync(int id)
        {
            var cliente = await _context.Clientes.FindAsync(id);
            if (cliente is null) return (false, "Cliente no encontrado.");

            cliente.Activo = true;
            await _context.SaveChangesAsync();
            return (true, null);
        }
    }
}
