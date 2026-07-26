using Microsoft.AspNetCore.Mvc;
using PymeCore.Models;
using PymeCore.Services;

namespace PymeCore.Controllers
{
    public class FacturasController : Controller
    {
        private readonly FacturaService _facturaService;
        private readonly FacturaSnapshotService _snapshotService;
        private readonly FacturaPdfService _pdfService;

        public FacturasController(
            FacturaService facturaService,
            FacturaSnapshotService snapshotService,
            FacturaPdfService pdfService)
        {
            _facturaService = facturaService;
            _snapshotService = snapshotService;
            _pdfService = pdfService;
        }

        public async Task<IActionResult> Index()
        {
            var facturas = await _facturaService.GetAllAsync();
            return View(facturas);
        }

        public async Task<IActionResult> Details(int id)
        {
            var factura = await _facturaService.GetByIdAsync(id);
            if (factura is null) return NotFound();
            return View(factura);
        }

        [HttpGet]
        public async Task<IActionResult> DescargarPdf(int id)
        {
            var factura = await _facturaService.GetByIdAsync(id);
            if (factura is null)
                return NotFound();

            var (snapshot, error) = await _snapshotService.GetDtoByFacturaIdAsync(id);

            if (snapshot is null)
            {
                if (error is null)
                    return NotFound();

                TempData["Error"] = error;
                return RedirectToAction(nameof(Details), new { id });
            }

            byte[] pdf;
            try
            {
                // El Estado se lee de la factura en vivo, no del snapshot (que está congelado
                // en el momento de la emisión) — así el PDF siempre refleja si está Anulada.
                pdf = _pdfService.Generar(snapshot, factura.Estado);
            }
            catch (Exception)
            {
                TempData["Error"] = "No se pudo generar el PDF de la factura.";
                return RedirectToAction(nameof(Details), new { id });
            }

            return File(
                pdf,
                "application/pdf",
                $"Factura-{snapshot.NumeroFactura}.pdf"
            );
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GenerarDesdePedido(int pedidoId)
        {
            var (ok, error, factura) = await _facturaService.GenerarDesdePedidoAsync(pedidoId);

            if (!ok)
            {
                TempData["Error"] = error;
                return RedirectToAction("Details", "Pedidos", new { id = pedidoId });
            }

            TempData["Success"] = $"Factura {factura!.Numero} generada correctamente.";
            return RedirectToAction("Details", new { id = factura.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarEstado(int id, EstadoFactura nuevoEstado)
        {
            var (ok, error) = await _facturaService.CambiarEstadoAsync(id, nuevoEstado);

            if (!ok)
                TempData["Error"] = error;
            else if (nuevoEstado == EstadoFactura.Anulada)
                TempData["Warning"] = "Factura anulada.";
            else
                TempData["Success"] = $"Estado actualizado a {nuevoEstado}.";

            return RedirectToAction("Details", new { id });
        }
    }
}
