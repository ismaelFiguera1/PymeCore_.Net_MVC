using System.ComponentModel.DataAnnotations;
using PymeCore.ViewModels.Presupuestos;
using Xunit;

namespace PymeCore.Tests
{
    public class PresupuestoFormViewModelValidationTests
    {
        private static PresupuestoFormViewModel VmValido() => new()
        {
            ClienteId = 1,
            Observaciones = "Observaciones de prueba"
        };

        private static List<ValidationResult> Validar(PresupuestoFormViewModel vm)
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
        public void Vm_SinObservaciones_NoProduceErrores()
        {
            var vm = VmValido();
            vm.Observaciones = null;

            var errores = Validar(vm);

            Assert.Empty(errores);
        }

        [Fact]
        public void Vm_ConClienteIdNulo_ProduceErrorRequired()
        {
            var vm = VmValido();
            vm.ClienteId = null;

            var errores = Validar(vm);

            Assert.Contains(errores, e => e.MemberNames.Contains(nameof(vm.ClienteId)));
        }

        [Fact]
        public void Vm_ConObservacionesDemasiadoLargas_ProduceErrorMaxLength()
        {
            var vm = VmValido();
            vm.Observaciones = new string('A', 501);

            var errores = Validar(vm);

            Assert.Contains(errores, e => e.MemberNames.Contains(nameof(vm.Observaciones)));
        }

        [Fact]
        public void Vm_ConObservacionesEnElLimiteDeLongitud_NoProduceErrores()
        {
            var vm = VmValido();
            vm.Observaciones = new string('A', 500);

            var errores = Validar(vm);

            Assert.Empty(errores);
        }

        // Numero no tiene ningun atributo de validacion (solo [Display]): el ViewModel lo muestra
        // en el formulario de edicion pero no lo valida ni lo usa para nada al enviar el POST
        // (el controlador nunca lee vm.Numero).
        [Fact]
        public void Vm_ConNumeroNuloOCualquierValor_NoProduceErrores()
        {
            var vm = VmValido();
            vm.Numero = null;

            var errores = Validar(vm);

            Assert.Empty(errores);
        }
    }
}
