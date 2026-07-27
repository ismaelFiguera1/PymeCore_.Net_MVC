using System.ComponentModel.DataAnnotations;
using PymeCore.Models;
using PymeCore.ViewModels.Stock;
using Xunit;

namespace PymeCore.Tests
{
    // Tipo (enum, no anulable) y Cantidad (int, no anulable) tienen [Required], pero RequiredAttribute
    // solo falla con null o cadena vacia: un enum o un int nunca son null, asi que ese atributo no
    // hace nada en ninguno de los dos campos (misma asimetria ya documentada para Producto/Cliente/
    // Proveedor). La validacion real de "Cantidad debe ser mayor que cero" vive en el controlador
    // (MovimientoStockController.Create), no en el ViewModel. Por eso aqui no hay tests
    // "ConTipoVacio"/"ConCantidadVacia_ProduceErrorRequired": no hay nada que probar.
    public class MovimientoStockFormViewModelValidationTests
    {
        private static MovimientoStockFormViewModel VmValido() => new()
        {
            ProductoId = 1,
            Tipo = TipoMovimiento.Entrada,
            Cantidad = 5
        };

        private static List<ValidationResult> Validar(MovimientoStockFormViewModel vm)
        {
            var contexto = new ValidationContext(vm);
            var resultados = new List<ValidationResult>();
            Validator.TryValidateObject(vm, contexto, resultados, validateAllProperties: true);
            return resultados;
        }

        [Fact]
        public void Vm_ConDatosValidos_NoProduceErrores()
        {
            var errores = Validar(VmValido());

            Assert.Empty(errores);
        }

        [Fact]
        public void Vm_SinMotivo_NoProduceErrores()
        {
            var vm = VmValido();
            vm.Motivo = null;

            var errores = Validar(vm);

            Assert.Empty(errores);
        }

        [Fact]
        public void Vm_ConMotivoDemasiadoLargo_ProduceErrorMaxLength()
        {
            var vm = VmValido();
            vm.Motivo = new string('A', 201);

            var errores = Validar(vm);

            Assert.Contains(errores, e => e.MemberNames.Contains(nameof(vm.Motivo)));
        }

        [Fact]
        public void Vm_ConMotivoEnElLimiteDeLongitud_NoProduceErrores()
        {
            var vm = VmValido();
            vm.Motivo = new string('A', 200);

            var errores = Validar(vm);

            Assert.Empty(errores);
        }

        // Cantidad negativa no produce ningun error en el ViewModel: no hay [Range] en este campo
        // (a diferencia de ProductoFormViewModel). El signo solo se valida/interpreta en el
        // controlador y en StockService, segun el Tipo de movimiento.
        [Fact]
        public void Vm_ConCantidadNegativa_NoProduceErroresDeValidacionDeDataAnnotations()
        {
            var vm = VmValido();
            vm.Cantidad = -5;

            var errores = Validar(vm);

            Assert.Empty(errores);
        }
    }
}
