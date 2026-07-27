using System.ComponentModel.DataAnnotations;
using PymeCore.ViewModels.Productos;
using Xunit;

namespace PymeCore.Tests
{
    // Mismo enfoque que en Cliente/Proveedor: se valida el ViewModel con Validator.TryValidateObject,
    // porque el binding automatico de ASP.NET Core no se ejecuta al llamar al controlador a mano.
    //
    // Nota importante que no aplicaba a Cliente/Proveedor: PrecioCoste, PrecioVenta, StockActual y
    // StockMinimo son tipos de valor no anulables (decimal/int). El atributo [Required] en ellos no
    // hace nada (RequiredAttribute solo falla con null, y un decimal/int nunca es null), asi que la
    // unica regla real y comprobable en esos 4 campos es el [Range]. Por eso aqui no hay tests
    // "ConXxxVacio_ProduceErrorRequired" para esos 4 campos, solo para Nombre (string) y ProveedorId
    // (int? anulable), donde Required si tiene efecto.
    public class ProductoFormViewModelValidationTests
    {
        private static ProductoFormViewModel VmValido() => new()
        {
            Nombre = "Producto de Prueba",
            PrecioCoste = 10m,
            PrecioVenta = 20m,
            StockActual = 5,
            StockMinimo = 2,
            ProveedorId = 1
        };

        private static List<ValidationResult> Validar(ProductoFormViewModel vm)
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
            vm.Nombre = new string('A', 101);

            var errores = Validar(vm);

            Assert.Contains(errores, e => e.MemberNames.Contains(nameof(vm.Nombre)));
        }

        // El Range del ViewModel exige "> 0" (0.01 como minimo). Esto es mas estricto que la capa de
        // servicio (ValidarValoresNoNegativos) y que el CHECK de la BD, que solo rechazan valores
        // negativos y SI permiten 0. Es una asimetria real entre capas, no un error de estos tests.
        [Fact]
        public void Vm_ConPrecioCosteCero_ProduceErrorRange()
        {
            var vm = VmValido();
            vm.PrecioCoste = 0m;

            var errores = Validar(vm);

            Assert.Contains(errores, e => e.MemberNames.Contains(nameof(vm.PrecioCoste)));
        }

        [Fact]
        public void Vm_ConPrecioCosteNegativo_ProduceErrorRange()
        {
            var vm = VmValido();
            vm.PrecioCoste = -1m;

            var errores = Validar(vm);

            Assert.Contains(errores, e => e.MemberNames.Contains(nameof(vm.PrecioCoste)));
        }

        [Fact]
        public void Vm_ConPrecioVentaCero_ProduceErrorRange()
        {
            var vm = VmValido();
            vm.PrecioVenta = 0m;

            var errores = Validar(vm);

            Assert.Contains(errores, e => e.MemberNames.Contains(nameof(vm.PrecioVenta)));
        }

        [Fact]
        public void Vm_ConPrecioVentaNegativo_ProduceErrorRange()
        {
            var vm = VmValido();
            vm.PrecioVenta = -1m;

            var errores = Validar(vm);

            Assert.Contains(errores, e => e.MemberNames.Contains(nameof(vm.PrecioVenta)));
        }

        [Fact]
        public void Vm_ConStockActualNegativo_ProduceErrorRange()
        {
            var vm = VmValido();
            vm.StockActual = -1;

            var errores = Validar(vm);

            Assert.Contains(errores, e => e.MemberNames.Contains(nameof(vm.StockActual)));
        }

        [Fact]
        public void Vm_ConStockMinimoNegativo_ProduceErrorRange()
        {
            var vm = VmValido();
            vm.StockMinimo = -1;

            var errores = Validar(vm);

            Assert.Contains(errores, e => e.MemberNames.Contains(nameof(vm.StockMinimo)));
        }

        [Fact]
        public void Vm_ConProveedorIdNulo_ProduceErrorRequired()
        {
            var vm = VmValido();
            vm.ProveedorId = null;

            var errores = Validar(vm);

            Assert.Contains(errores, e => e.MemberNames.Contains(nameof(vm.ProveedorId)));
        }

        // ---------- Validacion cruzada (IValidatableObject): PrecioVenta > PrecioCoste ----------

        [Fact]
        public void Vm_ConPrecioVentaIgualAPrecioCoste_ProduceErrorDeValidacionCruzada()
        {
            var vm = VmValido();
            vm.PrecioCoste = 10m;
            vm.PrecioVenta = 10m;

            var errores = Validar(vm);

            Assert.Contains(errores, e => e.MemberNames.Contains(nameof(vm.PrecioVenta)));
        }

        [Fact]
        public void Vm_ConPrecioVentaMenorQuePrecioCoste_ProduceErrorDeValidacionCruzada()
        {
            var vm = VmValido();
            vm.PrecioCoste = 10m;
            vm.PrecioVenta = 5m;

            var errores = Validar(vm);

            Assert.Contains(errores, e => e.MemberNames.Contains(nameof(vm.PrecioVenta)));
        }

        // Detalle real de System.ComponentModel.DataAnnotations.Validator: IValidatableObject.Validate()
        // solo se ejecuta si NINGUN otro atributo de validacion ha fallado ya en el objeto. Aqui Nombre
        // esta vacio (falla Required) Y el precio de venta es menor que el de coste (violaria la regla
        // cruzada) a la vez; el resultado solo debe traer el error de Nombre, no el de PrecioVenta,
        // porque el motor de validacion nunca llega a invocar Validate().
        [Fact]
        public void Vm_ConOtroCampoInvalidoYPrecioVentaMenorQueCoste_NoEjecutaLaValidacionCruzada()
        {
            var vm = VmValido();
            vm.Nombre = "";
            vm.PrecioCoste = 10m;
            vm.PrecioVenta = 5m;

            var errores = Validar(vm);

            Assert.Contains(errores, e => e.MemberNames.Contains(nameof(vm.Nombre)));
            Assert.DoesNotContain(errores, e => e.MemberNames.Contains(nameof(vm.PrecioVenta)));
        }
    }
}
