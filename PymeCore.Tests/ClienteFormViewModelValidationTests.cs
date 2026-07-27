using System.ComponentModel.DataAnnotations;
using PymeCore.ViewModels.Clientes;
using Xunit;

namespace PymeCore.Tests
{
    // Estos tests validan las DataAnnotations del ViewModel directamente con Validator.TryValidateObject.
    // No pasan por ClientesController porque el enlazado de modelos de ASP.NET Core (el mecanismo que
    // dispara esta validacion automaticamente y rellena ModelState) no se ejecuta al invocar la accion
    // del controlador a mano desde un test; hay que simularlo aqui, a nivel de ViewModel.
    public class ClienteFormViewModelValidationTests
    {
        private static ClienteFormViewModel VmValido() => new()
        {
            Nombre = "Cliente de Prueba",
            Nif = "12345678A",
            Email = "cliente@test.com",
            Telefono = "600123456",
            Direccion = "Calle Falsa 123",
            Ciudad = "Madrid"
        };

        private static List<ValidationResult> Validar(ClienteFormViewModel vm)
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
            vm.Nombre = new string('A', 51);

            var errores = Validar(vm);

            Assert.Contains(errores, e => e.MemberNames.Contains(nameof(vm.Nombre)));
        }

        [Fact]
        public void Vm_ConNifVacio_ProduceErrorRequired()
        {
            var vm = VmValido();
            vm.Nif = "";

            var errores = Validar(vm);

            Assert.Contains(errores, e => e.MemberNames.Contains(nameof(vm.Nif)));
        }

        [Theory]
        [InlineData("1234567A")]   // solo 7 digitos
        [InlineData("12345678")]   // sin letra final
        [InlineData("1234567AB")]  // dos letras
        [InlineData("ABCDEFGHA")]  // sin digitos
        public void Vm_ConNifFormatoInvalido_ProduceErrorRegex(string nifInvalido)
        {
            var vm = VmValido();
            vm.Nif = nifInvalido;

            var errores = Validar(vm);

            Assert.Contains(errores, e => e.MemberNames.Contains(nameof(vm.Nif)));
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

        // A diferencia del Nif y el Telefono (donde la regex ya exige una longitud exacta, asi que
        // MaxLength nunca llega a ser la regla que falla de forma independiente), el Email si puede
        // tener formato valido y superar los 50 caracteres a la vez: por eso hace falta un test propio.
        [Fact]
        public void Vm_ConEmailDemasiadoLargo_ProduceErrorMaxLength()
        {
            var vm = VmValido();
            vm.Email = new string('a', 45) + "@test.com"; // 54 caracteres, formato valido

            var errores = Validar(vm);

            Assert.Contains(errores, e => e.MemberNames.Contains(nameof(vm.Email)));
        }

        [Fact]
        public void Vm_ConTelefonoVacio_ProduceErrorRequired()
        {
            var vm = VmValido();
            vm.Telefono = "";

            var errores = Validar(vm);

            Assert.Contains(errores, e => e.MemberNames.Contains(nameof(vm.Telefono)));
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

        [Fact]
        public void Vm_ConDireccionYCiudadNulas_NoProduceErrores()
        {
            var vm = VmValido();
            vm.Direccion = null;
            vm.Ciudad = null;

            var errores = Validar(vm);

            Assert.Empty(errores);
        }

        [Fact]
        public void Vm_ConDireccionDemasiadoLarga_ProduceErrorMaxLength()
        {
            var vm = VmValido();
            vm.Direccion = new string('A', 201);

            var errores = Validar(vm);

            Assert.Contains(errores, e => e.MemberNames.Contains(nameof(vm.Direccion)));
        }

        [Fact]
        public void Vm_ConCiudadDemasiadoLarga_ProduceErrorMaxLength()
        {
            var vm = VmValido();
            vm.Ciudad = new string('A', 51);

            var errores = Validar(vm);

            Assert.Contains(errores, e => e.MemberNames.Contains(nameof(vm.Ciudad)));
        }
    }
}
