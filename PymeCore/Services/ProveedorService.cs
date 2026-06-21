using Microsoft.EntityFrameworkCore;
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
                    (p.Cif != null && p.Cif.ToLower().Contains(termino)));
            }

            return await query.OrderBy(p => p.Nombre).ToListAsync();
        }

        public async Task<Proveedor?> GetByIdAsync(int id)
        {
            return await _context.Proveedores.FindAsync(id);
        }

        public async Task CreateAsync(Proveedor proveedor)
        {
            _context.Proveedores.Add(proveedor);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(Proveedor proveedor)
        {
            _context.Proveedores.Update(proveedor);
            await _context.SaveChangesAsync();
        }

        public async Task DeactivateAsync(int id)
        {
            var proveedor = await _context.Proveedores.FindAsync(id);
            if (proveedor is null) return;

            proveedor.Activo = false;
            await _context.SaveChangesAsync();
        }

        public async Task ActivateAsync(int id)
        {
            var proveedor = await _context.Proveedores.FindAsync(id);
            if (proveedor is null) return;

            proveedor.Activo = true;
            await _context.SaveChangesAsync();
        }
    }
}
