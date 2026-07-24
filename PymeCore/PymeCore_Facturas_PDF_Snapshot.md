# PymeCore — Generación de facturas PDF con histórico inmutable

## Objetivo

Quiero añadir a PymeCore la opción de descargar una factura en PDF usando **QuestPDF Community**, que es gratuita para este proyecto personal.

Además, quiero conservar una copia histórica e inmutable de la factura en el momento de emitirla, para evitar que una factura antigua cambie si posteriormente se modifican el cliente, los productos, los precios, los impuestos o los datos fiscales de la empresa.

La solución debe usar:

- ASP.NET Core MVC.
- Entity Framework Core.
- PostgreSQL.
- QuestPDF Community.
- Una instantánea histórica de la factura almacenada como JSONB.

No quiero utilizar servicios externos, APIs de pago ni herramientas que necesiten Chromium u otros programas instalados en el servidor.

---

## Reglas de trabajo

1. Antes de modificar nada, revisa el módulo completo de Facturas.
2. Revisa modelos, ViewModels, servicios, controlador, vistas, relaciones de Entity Framework Core y configuración de la base de datos.
3. No ejecutes comandos, instalaciones, migraciones, actualizaciones de base de datos ni scripts.
4. Dame todos los comandos necesarios para que los ejecute manualmente.
5. No realices cambios hasta haber explicado primero la solución y haber recibido mi autorización.
6. Trabaja en cambios pequeños y controlados.
7. No cambies nombres, arquitectura o lógica existente sin necesidad.
8. No sobreingenierices la solución.
9. Respeta la estructura actual de PymeCore.
10. Al finalizar, lista todos los archivos creados y modificados, los cambios realizados y las pruebas manuales necesarias.

---

# 1. Análisis previo obligatorio

Antes de implementar, revisa cómo funciona actualmente el módulo de Facturas y responde:

1. Cómo se crea una factura.
2. Qué estados puede tener.
3. En qué momento se considera emitida o definitiva.
4. Qué datos guarda directamente la entidad `Factura`.
5. Qué datos guarda cada línea de factura.
6. Qué datos se leen actualmente desde `Cliente`.
7. Qué datos se leen actualmente desde `Producto`.
8. Si el precio aplicado se almacena en la línea o se consulta desde el producto.
9. Si el porcentaje de IVA aplicado se almacena en la línea.
10. Si la descripción del producto se almacena históricamente.
11. Si los datos fiscales del cliente quedan guardados en la factura.
12. Si los datos fiscales de la empresa están almacenados en algún modelo o configuración.
13. Si una factura emitida todavía puede editarse o eliminarse.
14. Qué datos faltarían para generar una factura histórica fiable.

No implementes nada durante este análisis.

---

# 2. Librería PDF

Usa exclusivamente:

```text
QuestPDF Community
```

No uses:

- Servicios externos para generar PDF.
- APIs de pago.
- PDFMonkey.
- DocRaptor.
- Playwright.
- Chromium.
- wkhtmltopdf.
- DinkToPdf.
- iText.
- Librerías que requieran una licencia comercial para este proyecto.
- Procesos externos instalados en el servidor.

La configuración de QuestPDF Community debe añadirse una sola vez durante el inicio de la aplicación:

```csharp
using QuestPDF.Infrastructure;

QuestPDF.Settings.License = LicenseType.Community;
```

Indica el comando de instalación del paquete, pero no lo ejecutes:

```powershell
Install-Package QuestPDF
```

Comprueba primero el tipo de proyecto y la ubicación correcta para configurar la licencia.

---

# 3. Funcionamiento del PDF

La factura continuará almacenada en PostgreSQL.

El PDF debe generarse en memoria cuando el usuario pulse un botón como:

```text
Descargar PDF
```

Flujo esperado:

```text
Usuario pulsa “Descargar PDF”
        ↓
FacturasController recibe el identificador
        ↓
Se busca la instantánea histórica de la factura
        ↓
Se deserializa el JSON
        ↓
QuestPDF construye el documento
        ↓
ASP.NET Core devuelve el archivo PDF
```

No quiero guardar inicialmente el archivo `.pdf`:

- Ni dentro de PostgreSQL.
- Ni en una carpeta del servidor.
- Ni en almacenamiento externo.

El PDF se generará bajo demanda a partir de la instantánea histórica.

---

# 4. Histórico de la factura

## Problema que debe resolverse

Una factura emitida no puede cambiar si posteriormente se modifica:

- El nombre del cliente.
- El NIF o CIF del cliente.
- La dirección fiscal del cliente.
- El correo o teléfono del cliente, si aparecen en la factura.
- El nombre o descripción del producto.
- El precio actual del producto.
- El porcentaje de IVA.
- Los datos fiscales de la empresa.
- Los cálculos o totales históricos.

El PDF nunca debe calcular sus importes usando el precio actual de `Producto`.

Debe utilizar los valores aplicados y almacenados cuando la factura fue emitida.

---

## Tabla histórica propuesta

Crea una entidad y tabla llamada preferiblemente:

```text
FacturaSnapshots
```

No la llames `FacturasToPdf`, porque su finalidad no es guardar un PDF, sino conservar una instantánea histórica de los datos de la factura.

Estructura orientativa:

```csharp
public class FacturaSnapshot
{
    public int Id { get; set; }

    public int FacturaId { get; set; }
    public Factura Factura { get; set; } = null!;

    public string DatosJson { get; set; } = string.Empty;

    public DateTime FechaCreacion { get; set; }

    public int Version { get; set; } = 1;

    public string? Hash { get; set; }
}
```

Esta estructura es orientativa. Antes de crearla, revisa las convenciones, tipos de identificadores, nombres de propiedades, auditoría y relaciones utilizadas actualmente en PymeCore.

La relación esperada inicialmente es:

```text
Factura 1 — 0..1 FacturaSnapshot
```

Una factura emitida debería tener una única instantánea histórica activa.

Configura `DatosJson` como `jsonb` en PostgreSQL.

No prepares todavía la migración hasta haber mostrado y explicado los cambios necesarios.

---

# 5. DTO de la instantánea

No serialices directamente la entidad `Factura` de Entity Framework Core.

No quiero guardar el objeto completo de EF porque puede contener:

- Propiedades de navegación.
- Referencias circulares.
- Campos internos innecesarios.
- Dependencias con la estructura actual de las entidades.
- Datos que no forman parte del documento histórico.

Crea un DTO específico para la instantánea, por ejemplo:

```csharp
public class FacturaSnapshotDto
{
    public string NumeroFactura { get; set; } = string.Empty;

    public DateTime FechaEmision { get; set; }

    public EmpresaSnapshotDto Empresa { get; set; } = new();

    public ClienteSnapshotDto Cliente { get; set; } = new();

    public List<FacturaLineaSnapshotDto> Lineas { get; set; } = [];

    public decimal BaseImponible { get; set; }

    public decimal TotalIva { get; set; }

    public decimal Total { get; set; }
}
```

Una línea histórica podría tener:

```csharp
public class FacturaLineaSnapshotDto
{
    public string Descripcion { get; set; } = string.Empty;

    public decimal Cantidad { get; set; }

    public decimal PrecioUnitario { get; set; }

    public decimal PorcentajeIva { get; set; }

    public decimal BaseImponible { get; set; }

    public decimal ImporteIva { get; set; }

    public decimal Total { get; set; }
}
```

También se necesitarán DTO específicos para:

- Datos históricos de la empresa.
- Datos históricos del cliente.
- Datos generales de la factura.
- Líneas.
- Totales.
- Información adicional que aparezca actualmente en las facturas.

Adapta estas clases a los datos reales del proyecto. No inventes campos que PymeCore no utilice sin explicar primero por qué serían necesarios.

---

# 6. Momento de creación del snapshot

La instantánea debe crearse cuando la factura pase al estado que actualmente represente que está emitida, finalizada o confirmada.

Antes de implementarlo, identifica exactamente cuál es ese estado y dónde se realiza actualmente esa transición.

Flujo esperado:

```text
Factura editable
        ↓
Se solicita emitir o finalizar
        ↓
Se validan los datos
        ↓
Se crea FacturaSnapshotDto
        ↓
Se serializa a JSON
        ↓
Se guarda en FacturaSnapshots
        ↓
La factura queda emitida
```

La creación del snapshot y el cambio de estado deberían formar parte de una operación consistente.

Analiza si debe utilizarse una transacción para evitar que ocurra alguno de estos casos:

- Factura emitida sin snapshot.
- Snapshot creado pero factura no emitida.
- Dos snapshots para la misma emisión.
- Datos parciales guardados.

No implementes versiones o rectificaciones complejas todavía.

Para la primera versión, busca una solución sencilla:

```text
Una factura emitida → un snapshot inmutable
```

---

# 7. Inmutabilidad

Una vez creado el snapshot:

- No debe modificarse.
- No debe regenerarse automáticamente.
- No debe actualizarse si cambia el cliente.
- No debe actualizarse si cambia un producto.
- No debe actualizarse si cambia el IVA.
- No debe eliminarse al modificar datos relacionados.
- No debe depender de las propiedades de navegación actuales.

El PDF debe generarse siempre desde el snapshot cuando exista.

Si una factura todavía no está emitida y no tiene snapshot, analiza cuál debe ser el comportamiento:

- Bloquear la descarga.
- Permitir una previsualización no definitiva.
- Generar un PDF marcado como borrador.

No implementes esa decisión sin explicarme primero las alternativas y recomendar una.

---

# 8. Serialización

Utiliza las herramientas estándar de .NET, preferiblemente:

```csharp
System.Text.Json
```

La serialización debe hacerse sobre `FacturaSnapshotDto`, no sobre entidades de EF Core.

Centraliza la serialización y deserialización en un servicio o componente adecuado.

No repartas llamadas a `JsonSerializer.Serialize` y `JsonSerializer.Deserialize` por controladores y vistas.

Analiza si es necesario configurar:

- Convención de nombres JSON.
- Decimales.
- Fechas.
- Valores nulos.
- Enumeraciones.
- Compatibilidad de versiones.

Incluye un campo `Version` en `FacturaSnapshot` para poder reconocer en el futuro la estructura del JSON.

El campo `Hash` es opcional en esta primera versión. Explica si merece la pena calcular un SHA-256 del JSON o si conviene dejarlo para una mejora posterior.

---

# 9. Estructura orientativa

La estructura debe adaptarse al proyecto existente. Como referencia:

```text
Facturas/
├── Models/
│   ├── Factura.cs
│   └── FacturaSnapshot.cs
├── DTOs/
│   └── Snapshots/
│       ├── FacturaSnapshotDto.cs
│       ├── FacturaLineaSnapshotDto.cs
│       ├── ClienteSnapshotDto.cs
│       └── EmpresaSnapshotDto.cs
├── Services/
│   ├── FacturaService.cs
│   ├── IFacturaSnapshotService.cs
│   ├── FacturaSnapshotService.cs
│   ├── IFacturaPdfService.cs
│   └── FacturaPdfService.cs
├── Pdf/
│   └── FacturaPdfDocument.cs
├── Controllers/
│   └── FacturasController.cs
└── Views/
    └── Facturas/
        └── Details.cshtml
```

No crees carpetas nuevas si la arquitectura actual utiliza otra organización.

Antes de implementar, lista:

- Archivos que habría que crear.
- Archivos que habría que modificar.
- Responsabilidad de cada archivo.
- Dependencias necesarias.
- Cambios en inyección de dependencias.
- Cambios en `ApplicationDbContext`.
- Configuración de Entity Framework Core.
- Migración que sería necesaria.

---

# 10. Servicios

Mantén separadas estas responsabilidades:

## FacturaService

Responsable de:

- Consultar la factura.
- Cargar sus relaciones.
- Aplicar la lógica existente.
- Coordinar la emisión si actualmente corresponde a este servicio.

## FacturaSnapshotService

Responsable de:

- Construir `FacturaSnapshotDto`.
- Copiar los datos históricos.
- Serializar el DTO.
- Guardar el snapshot.
- Obtener y deserializar un snapshot.
- Evitar duplicados.
- Garantizar que no se modifique después de crearse.

## FacturaPdfService

Responsable de:

- Recibir los datos históricos.
- Crear `FacturaPdfDocument`.
- Generar el PDF en memoria.
- Devolver un `byte[]`.

## FacturaPdfDocument

Responsable únicamente del diseño del documento QuestPDF.

No pongas:

- Consultas a la base de datos dentro del documento.
- Lógica de negocio dentro del controlador.
- Toda la generación del PDF directamente en el controlador.
- Serialización en la vista.
- Acceso a EF Core desde QuestPDF.

---

# 11. Contenido del PDF

El PDF debe incluir solamente los datos realmente disponibles o los que se acuerde incorporar al snapshot.

Como mínimo, revisa si pueden mostrarse:

- Nombre y datos fiscales de la empresa.
- Logotipo, si ya existe.
- Número de factura.
- Fecha de emisión.
- Estado, si corresponde.
- Datos fiscales del cliente.
- NIF o CIF.
- Dirección.
- Tabla de líneas.
- Descripción o concepto.
- Cantidad.
- Precio unitario.
- Porcentaje de IVA.
- Base imponible de cada línea.
- Importe de IVA.
- Total de cada línea.
- Base imponible total.
- Total de IVA.
- Total final.
- Observaciones, si existen.
- Número de página.

El diseño debe ser:

- Profesional.
- Sencillo.
- Legible.
- Tamaño A4.
- Sin elementos decorativos innecesarios.
- Compatible con varias páginas.
- Con cabecera de tabla repetida si las líneas ocupan más de una página.
- Con formatos de moneda y fecha coherentes con el proyecto.

No inventes una identidad visual nueva sin revisar antes el estilo actual de PymeCore.

---

# 12. Acción del controlador

Añade una acción GET en `FacturasController` con una responsabilidad mínima.

Ejemplo orientativo:

```csharp
[HttpGet]
public async Task<IActionResult> DescargarPdf(int id)
{
    var snapshot = await _facturaSnapshotService.GetByFacturaIdAsync(id);

    if (snapshot == null)
        return NotFound();

    var pdf = _facturaPdfService.Generar(snapshot);

    return File(
        pdf,
        "application/pdf",
        $"Factura-{snapshot.NumeroFactura}.pdf"
    );
}
```

Adapta nombres, tipos y métodos a la arquitectura real.

La acción debe:

1. Recibir el identificador de la factura.
2. Comprobar que la factura existe.
3. Comprobar que tiene snapshot.
4. Obtener y deserializar los datos históricos.
5. Generar el PDF.
6. Devolver `File`.
7. Usar el MIME type:

```text
application/pdf
```

8. Usar un nombre seguro y legible, por ejemplo:

```text
Factura-{NumeroFactura}.pdf
```

Evita exponer errores internos de serialización o generación directamente al usuario.

---

# 13. Vista

En la vista de detalles de Facturas, añade un botón similar a:

```html
<a asp-action="DescargarPdf"
   asp-route-id="@Model.Id"
   class="btn btn-danger">
    Descargar PDF
</a>
```

Adapta la clase Bootstrap al diseño actual de PymeCore.

El botón debería mostrarse solamente cuando tenga sentido según el estado de la factura y la existencia del snapshot.

Antes de decidir la condición exacta, revisa:

- Estados actuales.
- Propiedades disponibles en el ViewModel.
- Cómo se muestran actualmente las acciones.
- Si el usuario puede descargar facturas en borrador.

---

# 14. Base de datos y Entity Framework Core

Analiza los cambios necesarios en:

- `ApplicationDbContext`.
- `DbSet<FacturaSnapshot>`.
- Relación con `Factura`.
- Índice único sobre `FacturaId`.
- Tipo de columna `jsonb`.
- Restricciones de longitud si corresponden.
- Comportamiento de eliminación.
- Fecha de creación.
- Valor por defecto de `Version`.

La relación debería impedir más de un snapshot por factura.

Revisa cuidadosamente el comportamiento de borrado:

- Una factura emitida probablemente no debería eliminarse físicamente.
- El snapshot histórico no debería perderse accidentalmente.
- No configures borrado en cascada sin justificarlo.

No generes ni ejecutes la migración hasta que apruebe el modelo.

Cuando llegue el momento, dame:

1. El comando para crear la migración.
2. El nombre recomendado de la migración.
3. El comando para actualizar la base de datos.
4. Los cambios esperados en PostgreSQL.
5. Cómo revertir la migración si algo falla.

Yo ejecutaré manualmente todos los comandos.

---

# 15. Validaciones y errores

Contempla como mínimo:

- Factura inexistente.
- Factura no emitida.
- Snapshot inexistente.
- Snapshot duplicado.
- JSON vacío.
- JSON inválido.
- Versión de snapshot no compatible.
- Error durante la generación del PDF.
- Número de factura con caracteres no válidos para el nombre del archivo.
- Factura sin líneas.
- Datos fiscales incompletos.
- Totales inconsistentes.

No ocultes errores importantes, pero utiliza el mecanismo de manejo de errores que ya exista en PymeCore.

No crees una infraestructura nueva de excepciones o resultados si el proyecto ya utiliza otro patrón.

---

# 16. Pruebas manuales necesarias

Al finalizar, prepara una lista de pruebas manuales como mínimo para estos casos:

1. Emitir una factura válida.
2. Verificar que se crea un único snapshot.
3. Comprobar el JSON almacenado.
4. Descargar el PDF.
5. Abrir el PDF y verificar su contenido.
6. Comprobar importes, IVA y totales.
7. Modificar después el nombre del cliente.
8. Volver a descargar la factura y confirmar que conserva el nombre histórico.
9. Modificar después el precio del producto.
10. Confirmar que el PDF mantiene el precio histórico.
11. Modificar después el IVA del producto.
12. Confirmar que el PDF mantiene el IVA histórico.
13. Intentar descargar una factura inexistente.
14. Intentar descargar una factura sin snapshot.
15. Comprobar una factura con muchas líneas y varias páginas.
16. Comprobar que no se crea un segundo snapshot al repetir una acción.
17. Comprobar el nombre del archivo descargado.
18. Comprobar el comportamiento con datos fiscales incompletos.
19. Verificar que ninguna acción ejecuta comandos automáticamente.
20. Confirmar que una factura emitida no modifica su snapshot.

---

# 17. Orden de trabajo obligatorio

Trabaja exactamente en este orden:

## Fase 1 — Análisis

1. Lee el módulo completo de Facturas.
2. Explica el flujo actual.
3. Identifica el estado de emisión.
4. Revisa los datos históricos existentes.
5. Identifica dependencias con Cliente y Producto.
6. Lista problemas o datos faltantes.

## Fase 2 — Propuesta

1. Lista los archivos que habría que crear.
2. Lista los archivos que habría que modificar.
3. Propón el modelo `FacturaSnapshot`.
4. Propón los DTO de snapshot.
5. Explica cómo se creará la instantánea.
6. Explica cómo se generará el PDF.
7. Explica los cambios de base de datos.
8. Explica riesgos y decisiones pendientes.

## Fase 3 — Autorización

Detente y espera mi autorización.

No modifiques archivos antes de recibirla.

## Fase 4 — Implementación

Después de mi autorización:

1. Implementa los cambios en pasos pequeños.
2. Muestra qué archivo cambias en cada paso.
3. No ejecutes comandos.
4. No ejecutes migraciones.
5. No actualices la base de datos.
6. No hagas cambios no relacionados.

## Fase 5 — Cierre

Al terminar:

1. Lista archivos creados.
2. Lista archivos modificados.
3. Resume la lógica implementada.
4. Proporciona los comandos manuales.
5. Proporciona las pruebas manuales.
6. Indica cualquier limitación o decisión pendiente.

---

# Decisión arquitectónica principal

La solución deseada es:

```text
Factura editable mientras está en borrador
+
Snapshot JSONB al emitir
+
Snapshot inmutable
+
PDF generado desde el snapshot con QuestPDF Community
+
Sin guardar el archivo PDF
+
Sin servicios externos
+
Sin costes adicionales de licencia
```

La fuente histórica del PDF debe ser `FacturaSnapshotDto`, almacenado como JSONB.

La entidad `Factura` seguirá siendo el registro operativo del sistema, mientras que `FacturaSnapshot` conservará la representación histórica de la factura emitida.

Antes de implementar, confirma mediante el análisis del código si esta solución encaja con la arquitectura real de PymeCore y señala cualquier ajuste necesario.
