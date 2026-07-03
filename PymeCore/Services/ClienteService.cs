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
            var query = _context.Clientes.AsQueryable();

            if (!string.IsNullOrWhiteSpace(buscar))
            {
                var termino = buscar.ToLower();
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
