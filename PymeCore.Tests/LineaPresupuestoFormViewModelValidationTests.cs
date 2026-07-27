using System.ComponentModel.DataAnnotations;
using PymeCore.ViewModels.Presupuestos;
using Xunit;

namespace PymeCore.Tests
{
    // Cantidad y PrecioUnitario son tipos de valor no anulables (int/decimal), asi que su
    // [Required] no hace nada (RequiredAttribute solo falla con null o cadena vacia): la unica
    // regla real y comprobable en esos dos campos es el [Range]. Misma asimetria ya documentada
    // en ProductoFormViewModelValidationTests/MovimientoStockFormViewModelValidationTests.
    public class LineaPresupuestoFormViewModelValidationTests
    {
        private static LineaPresupuestoFormViewModel VmValido() => new()
        {
            PresupuestoId = 1,
            ProductoId = 1,
            Cantidad = 2,
            PrecioUnitario = 10m
        };

        private static List<ValidationResult> Validar(LineaPresupuestoFormViewModel vm)
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
        public void Vm_ConProductoIdNulo_ProduceErrorRequired()
        {
            var vm = VmValido();
            vm.ProductoId = null;

            var errores = Validar(vm);

            Assert.Contains(errores, e => e.MemberNames.Contains(nameof(vm.ProductoId)));
        }

        [Fact]
        public void Vm_ConCantidadCero_ProduceErrorRange()
        {
            var vm = VmValido();
            vm.Cantidad = 0;

            var errores = Validar(vm);

            Assert.Contains(errores, e => e.MemberNames.Contains(nameof(vm.Cantidad)));
        }

        [Fact]
        public void Vm_ConCantidadNegativa_ProduceErrorRange()
        {
            var vm = VmValido();
            vm.Cantidad = -1;

            var errores = Validar(vm);

            Assert.Contains(errores, e => e.MemberNames.Contains(nameof(vm.Cantidad)));
        }

        [Fact]
        public void Vm_ConPrecioUnitarioCero_ProduceErrorRange()
        {
            var vm = VmValido();
            vm.PrecioUnitario = 0m;

            var errores = Validar(vm);

            Assert.Contains(errores, e => e.MemberNames.Contains(nameof(vm.PrecioUnitario)));
        }

        [Fact]
        public void Vm_ConPrecioUnitarioNegativo_ProduceErrorRange()
        {
            var vm = VmValido();
            vm.PrecioUnitario = -1m;

            var errores = Validar(vm);

            Assert.Contains(errores, e => e.MemberNames.Contains(nameof(vm.PrecioUnitario)));
        }

        [Fact]
        public void Vm_ConDescripcionDemasiadoLarga_ProduceErrorMaxLength()
        {
            var vm = VmValido();
            vm.Descripcion = new string('A', 201);

            var errores = Validar(vm);

            Assert.Contains(errores, e => e.MemberNames.Contains(nameof(vm.Descripcion)));
        }

        [Fact]
        public void Vm_SinDescripcion_NoProduceErrores()
        {
            var vm = VmValido();
            vm.Descripcion = null;

            var errores = Validar(vm);

            Assert.Empty(errores);
        }
    }
}
