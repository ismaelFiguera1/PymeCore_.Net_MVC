using System.ComponentModel.DataAnnotations;
using PymeCore.ViewModels.Proveedores;
using Xunit;

namespace PymeCore.Tests
{
    // Mismo enfoque que ClienteFormViewModelValidationTests: se valida el ViewModel directamente
    // con Validator.TryValidateObject, porque el binding automatico de ASP.NET Core (que dispara
    // esta validacion y rellena ModelState) no ocurre al llamar a la accion del controlador a mano.
    public class ProveedorFormViewModelValidationTests
    {
        private static ProveedorFormViewModel VmValido() => new()
        {
            Nombre = "Proveedor de Prueba",
            Cif = "12345678A",
            PersonaContacto = "Juan Perez",
            Email = "proveedor@test.com",
            Telefono = "600123456"
        };

        private static List<ValidationResult> Validar(ProveedorFormViewModel vm)
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
        public void Vm_ConNombreVacio_ProduceErrorRequired()
        {
            var vm = VmValido();
            vm.Nombre = "";

            var errores = Validar(vm);

            Assert.Contains(errores, e => e.MemberNames.Contains(nameof(vm.Nombre)));
        }

        [Fact]
        public void Vm_ConNombreDemasiadoLargo_ProduceErrorMaxLength()
        {
            var vm = VmValido();
            vm.Nombre = new string('A', 101); // limite real es 100, distinto del de Cliente (50)

            var errores = Validar(vm);

            Assert.Contains(errores, e => e.MemberNames.Contains(nameof(vm.Nombre)));
        }

        [Fact]
        public void Vm_ConCifVacio_ProduceErrorRequired()
        {
            var vm = VmValido();
            vm.Cif = "";

            var errores = Validar(vm);

            Assert.Contains(errores, e => e.MemberNames.Contains(nameof(vm.Cif)));
        }

        [Theory]
        [InlineData("1234567A")]   // solo 7 digitos
        [InlineData("12345678")]   // sin letra final
        [InlineData("1234567AB")]  // dos letras
        [InlineData("ABCDEFGHA")]  // sin digitos
        public void Vm_ConCifFormatoInvalido_ProduceErrorRegex(string cifInvalido)
        {
            var vm = VmValido();
            vm.Cif = cifInvalido;

            var errores = Validar(vm);

            Assert.Contains(errores, e => e.MemberNames.Contains(nameof(vm.Cif)));
        }

        // A diferencia de Cliente, PersonaContacto es un campo especifico de Proveedor: opcional,
        // pero con MaxLength(100) real.
        [Fact]
        public void Vm_ConPersonaContactoNula_NoProduceErrores()
        {
            var vm = VmValido();
            vm.PersonaContacto = null;

            var errores = Validar(vm);

            Assert.Empty(errores);
        }

        [Fact]
        public void Vm_ConPersonaContactoDemasiadoLarga_ProduceErrorMaxLength()
        {
            var vm = VmValido();
            vm.PersonaContacto = new string('A', 101);

            var errores = Validar(vm);

            Assert.Contains(errores, e => e.MemberNames.Contains(nameof(vm.PersonaContacto)));
        }

        [Fact]
        public void Vm_ConEmailVacio_ProduceErrorRequired()
        {
            var vm = VmValido();
            vm.Email = "";

            var errores = Validar(vm);

            Assert.Contains(errores, e => e.MemberNames.Contains(nameof(vm.Email)));
        }

        [Theory]
        [InlineData("no-es-un-email")]
        [InlineData("falta-arroba.com")]
        [InlineData("@sin-usuario.com")]
        public void Vm_ConEmailFormatoInvalido_ProduceErrorEmailAddress(string emailInvalido)
        {
            var vm = VmValido();
            vm.Email = emailInvalido;

            var errores = Validar(vm);

            Assert.Contains(errores, e => e.MemberNames.Contains(nameof(vm.Email)));
        }

        [Fact]
        public void Vm_ConEmailDemasiadoLargo_ProduceErrorMaxLength()
        {
            var vm = VmValido();
            vm.Email = new string('a', 95) + "@test.com"; // 104 caracteres, formato valido, limite real es 100

            var errores = Validar(vm);

            Assert.Contains(errores, e => e.MemberNames.Contains(nameof(vm.Email)));
        }

        // A diferencia de Cliente (donde Telefono es obligatorio), en Proveedor es opcional:
        // este test confirma que dejarlo en null no produce ningun error.
        [Fact]
        public void Vm_ConTelefonoNulo_NoProduceErrores()
        {
            var vm = VmValido();
            vm.Telefono = null;

            var errores = Validar(vm);

            Assert.Empty(errores);
        }

        [Theory]
        [InlineData("60012345")]    // 8 digitos, falta uno
        [InlineData("6001234567")]  // 10 digitos, sobra uno
        [InlineData("60012345A")]   // contiene una letra
        public void Vm_ConTelefonoFormatoInvalido_ProduceErrorRegex(string telefonoInvalido)
        {
            var vm = VmValido();
            vm.Telefono = telefonoInvalido;

            var errores = Validar(vm);

            Assert.Contains(errores, e => e.MemberNames.Contains(nameof(vm.Telefono)));
        }
    }
}
