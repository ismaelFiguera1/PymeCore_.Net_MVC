using Microsoft.EntityFrameworkCore;
using Npgsql;
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

            // 2) Trae de la BD solo el campo Numero, y solo de los presupuestos de ESTE año
            // (StartsWith(prefix) se traduce a un LIKE 'PRES-2026-%' en SQL, así que filtra en la propia BD,
            // no trae todos los presupuestos de todos los años para descartarlos después).
            var numeros = await _context.Presupuestos
                .Where(p => p.Numero.StartsWith(prefix))
                .Select(p => p.Numero)
                .ToListAsync();

            // 3) De cada número ya guardado (ej. "PRES-2026-004"), se queda con lo que hay después del prefijo
            // (n[prefix.Length..] es "004") y lo intenta convertir a int; si algún Numero no tuviera ese formato
            // (dato corrupto o antiguo), int.TryParse fallaría y se usa 0 en su lugar en vez de reventar.
            // DefaultIfEmpty(0) cubre el caso de que sea el primer presupuesto del año (la lista numeros está vacía,
            // y Max() sobre una secuencia vacía lanzaría excepción sin esto).
            // Al máximo encontrado se le suma 1: ese es el siguiente correlativo a usar.
            var siguiente = numeros
                .Select(n => int.TryParse(n[prefix.Length..], out var num) ? num : 0)
                .DefaultIfEmpty(0)
                .Max() + 1;

            // 4) Se compone el número final con el correlativo en 3 dígitos (D3 → "004"), ej. "PRES-2026-004"
            return $"{prefix}{siguiente:D3}";
        }

        public async Task CreateAsync(Presupuesto presupuesto)
        {
            await GuardarConNumeroUnicoAsync(presupuesto);
        }

        // Genera el número y lo guarda; si otra petición se adelantó con el mismo número
        // (choque detectado por el índice único IX_Presupuestos_Numero), regenera y reintenta
        // en vez de dejar reventar la petición con un error de base de datos.
        private async Task GuardarConNumeroUnicoAsync(Presupuesto presupuesto)
        {
            const int maxIntentos = 3;

            for (var intento = 1; ; intento++)
            {
                presupuesto.Numero = await GenerarNumeroAsync();
                _context.Presupuestos.Add(presupuesto);

                try
                {
                    await _context.SaveChangesAsync();
                    return;
                }
                catch (DbUpdateException ex) when (EsNumeroDuplicado(ex) && intento < maxIntentos)
                {
                    _context.Entry(presupuesto).State = EntityState.Detached;
                }
            }
        }

        private static bool EsNumeroDuplicado(DbUpdateException ex) =>
            ex.InnerException is PostgresException { SqlState: "23505", ConstraintName: "IX_Presupuestos_Numero" };

        public async Task UpdateAsync(Presupuesto presupuesto)
        {
            _context.Presupuestos.Update(presupuesto);
            await _context.SaveChangesAsync();
        }

        // Igual que en PedidoService.CrearDesdePresupuestoAsync: el presupuesto nuevo, sus
        // líneas copiadas y el total recalculado se guardan en una única transacción, y el
        // reintento por choque de número (IX_Presupuestos_Numero) repite la operación entera.
        public async Task<Presupuesto> DuplicarAsync(int id)
        {
            var original = await GetByIdAsync(id);
            if (original is null) throw new InvalidOperationException("Presupuesto no encontrado.");

            const int maxIntentos = 3;

            for (var intento = 1; ; intento++)
            {
                var nuevo = new Presupuesto
                {
                    Numero        = await GenerarNumeroAsync(),
                    ClienteId     = original.ClienteId,
                    Fecha         = DateTime.UtcNow,
                    Estado        = EstadoPresupuesto.Borrador,
                    Observaciones = original.Observaciones,
                    Total         = 0
                };

                await using var tx = await _context.Database.BeginTransactionAsync();

                _context.Presupuestos.Add(nuevo);

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateException ex) when (EsNumeroDuplicado(ex) && intento < maxIntentos)
                {
                    _context.Entry(nuevo).State = EntityState.Detached;
                    continue;
                }

                foreach (var linea in original.Lineas)
                {
                    _context.LineasPresupuesto.Add(new LineaPresupuesto
                    {
                        PresupuestoId  = nuevo.Id,
                        ProductoId     = linea.ProductoId,
                        Descripcion    = linea.Descripcion,
                        Cantidad       = linea.Cantidad,
                        PrecioUnitario = linea.PrecioUnitario,
                        Subtotal       = linea.Subtotal
                    });
                }

                nuevo.Total = original.Lineas.Sum(l => l.Subtotal);
                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                return nuevo;
            }
        }

        public async Task<(bool Ok, string? Error)> EnviarAsync(int id)
        {
            var presupuesto = await GetByIdAsync(id);
            if (presupuesto is null) return (false, "Presupuesto no encontrado.");
            if (presupuesto.Estado != EstadoPresupuesto.Borrador)
                return (false, $"Solo se puede enviar un presupuesto en estado Borrador (estado actual: {presupuesto.Estado}).");
            if (!presupuesto.Lineas.Any())
                return (false, "No se puede enviar un presupuesto sin líneas. Añade al menos una.");

            presupuesto.Estado = EstadoPresupuesto.Enviado;
            await _context.SaveChangesAsync();
            return (true, null);
        }

        public async Task<(bool Ok, string? Error)> AceptarAsync(int id)
        {
            var presupuesto = await GetByIdAsync(id);
            if (presupuesto is null) return (false, "Presupuesto no encontrado.");
            if (presupuesto.Estado != EstadoPresupuesto.Enviado)
                return (false, $"Solo se puede aceptar un presupuesto en estado Enviado (estado actual: {presupuesto.Estado}).");

            presupuesto.Estado = EstadoPresupuesto.Aceptado;
            await _context.SaveChangesAsync();
            return (true, null);
        }

        public async Task<(bool Ok, string? Error)> RechazarAsync(int id)
        {
            var presupuesto = await GetByIdAsync(id);
            if (presupuesto is null) return (false, "Presupuesto no encontrado.");
            if (presupuesto.Estado != EstadoPresupuesto.Enviado)
                return (false, $"Solo se puede rechazar un presupuesto en estado Enviado (estado actual: {presupuesto.Estado}).");

            presupuesto.Estado = EstadoPresupuesto.Rechazado;
            await _context.SaveChangesAsync();
            return (true, null);
        }

        // Guardar la línea y recalcular el Total ocurren dentro de la misma transacción:
        // si falla el recálculo, tampoco queda guardada la línea (todo o nada).
        public async Task AgregarLineaAsync(LineaPresupuesto linea)
        {
            linea.Subtotal = linea.Cantidad * linea.PrecioUnitario;

            await using var tx = await _context.Database.BeginTransactionAsync();

            _context.LineasPresupuesto.Add(linea);
            await _context.SaveChangesAsync();

            await RecalcularTotalAsync(linea.PresupuestoId);
            await tx.CommitAsync();
        }

        public async Task EliminarLineaAsync(int lineaId, int presupuestoId)
        {
            var linea = await _context.LineasPresupuesto
                .FirstOrDefaultAsync(l => l.Id == lineaId && l.PresupuestoId == presupuestoId);
            if (linea is null) return;

            await using var tx = await _context.Database.BeginTransactionAsync();

            _context.LineasPresupuesto.Remove(linea);
            await _context.SaveChangesAsync();

            await RecalcularTotalAsync(presupuestoId);
            await tx.CommitAsync();
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
