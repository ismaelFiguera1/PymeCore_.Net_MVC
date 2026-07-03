using Microsoft.EntityFrameworkCore;
using Npgsql;
using PymeCore.Data;
using PymeCore.Models;

namespace PymeCore.Services
{
    public class ProveedorService
    {
        private readonly ApplicationDbContext _context;

        public ProveedorService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<Proveedor>> GetAllAsync(string? buscar = null)
        {
            var query = _context.Proveedores.AsQueryable();

            if (!string.IsNullOrWhiteSpace(buscar))
            {
                var termino = buscar.ToLower();
                query = query.Where(p =>
                    p.Nombre.ToLower().Contains(termino) ||
                    p.Cif.ToLower().Contains(termino));
            }

            return await query.OrderBy(p => p.Nombre).ToListAsync();
        }

        public async Task<Proveedor?> GetByIdAsync(int id)
        {
            return await _context.Proveedores.FindAsync(id);
        }

        public async Task<bool> ExisteCifAsync(string cif, int excludeId = 0)
        {
            return await _context.Proveedores
                .AnyAsync(p => p.Cif == cif && p.Id != excludeId);
        }

        public async Task<(bool Ok, string? Error)> CreateAsync(Proveedor proveedor)
        {
            try
            {
                _context.Proveedores.Add(proveedor);
                await _context.SaveChangesAsync();
                return (true, null);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
            {
                return (false, "Ya existe un proveedor con este CIF.");
            }
        }

        public async Task<(bool Ok, string? Error)> UpdateAsync(Proveedor proveedor)
        {
            try
            {
                _context.Proveedores.Update(proveedor);
                await _context.SaveChangesAsync();
                return (true, null);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
            {
                return (false, "Ya existe un proveedor con este CIF.");
            }
        }

        public async Task<(bool Ok, string? Error)> DeactivateAsync(int id)
        {
            var proveedor = await _context.Proveedores.FindAsync(id);
            if (proveedor is null) return (false, "Proveedor no encontrado.");

            proveedor.Activo = false;
            await _context.SaveChangesAsync();
            return (true, null);
        }

        public async Task<(bool Ok, string? Error)> ActivateAsync(int id)
        {
            var proveedor = await _context.Proveedores.FindAsync(id);
            if (proveedor is null) return (false, "Proveedor no encontrado.");

            proveedor.Activo = true;
            await _context.SaveChangesAsync();
            return (true, null);
        }
    }
}
