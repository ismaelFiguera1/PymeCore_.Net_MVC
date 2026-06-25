using Microsoft.EntityFrameworkCore;
using PymeCore.Data;
using PymeCore.Models;

namespace PymeCore.Services
{
    public class PresupuestoService
    {
        private readonly ApplicationDbContext _context;

        public PresupuestoService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<Presupuesto>> GetAllAsync(string? buscar = null)
        {
            var query = _context.Presupuestos.Include(p => p.Cliente).AsQueryable();

            if (!string.IsNullOrWhiteSpace(buscar))
            {
                var termino = buscar.ToLower();
                query = query.Where(p =>
                    p.Numero.ToLower().Contains(termino) ||
                    p.Cliente!.Nombre.ToLower().Contains(termino));
            }

            return await query.OrderByDescending(p => p.Fecha).ToListAsync();
        }

        public async Task<Presupuesto?> GetByIdAsync(int id)
        {
            return await _context.Presupuestos
                .Include(p => p.Cliente)
                .Include(p => p.Lineas)
                    .ThenInclude(l => l.Producto)
                .FirstOrDefaultAsync(p => p.Id == id);
        }

        public async Task<string> GenerarNumeroAsync()
        {
            var año = DateTime.UtcNow.Year;
            var prefix = $"PRES-{año}-";

            var numeros = await _context.Presupuestos
                .Where(p => p.Numero.StartsWith(prefix))
                .Select(p => p.Numero)
                .ToListAsync();

            var siguiente = numeros
                .Select(n => int.TryParse(n[prefix.Length..], out var num) ? num : 0)
                .DefaultIfEmpty(0)
                .Max() + 1;

            return $"{prefix}{siguiente:D3}";
        }

        public async Task CreateAsync(Presupuesto presupuesto)
        {
            _context.Presupuestos.Add(presupuesto);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(Presupuesto presupuesto)
        {
            _context.Presupuestos.Update(presupuesto);
            await _context.SaveChangesAsync();
        }

        public async Task AgregarLineaAsync(LineaPresupuesto linea)
        {
            linea.Subtotal = linea.Cantidad * linea.PrecioUnitario;
            _context.LineasPresupuesto.Add(linea);
            await _context.SaveChangesAsync();
            await RecalcularTotalAsync(linea.PresupuestoId);
        }

        public async Task EliminarLineaAsync(int lineaId)
        {
            var linea = await _context.LineasPresupuesto.FindAsync(lineaId);
            if (linea is null) return;

            var presupuestoId = linea.PresupuestoId;
            _context.LineasPresupuesto.Remove(linea);
            await _context.SaveChangesAsync();
            await RecalcularTotalAsync(presupuestoId);
        }

        private async Task RecalcularTotalAsync(int presupuestoId)
        {
            var presupuesto = await _context.Presupuestos
                .Include(p => p.Lineas)
                .FirstOrDefaultAsync(p => p.Id == presupuestoId);

            if (presupuesto is null) return;

            presupuesto.Total = presupuesto.Lineas.Sum(l => l.Subtotal);
            await _context.SaveChangesAsync();
        }
    }
}
