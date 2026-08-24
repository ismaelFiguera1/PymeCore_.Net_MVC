# PymeCore

Mini ERP web para pequeñas y medianas empresas, construido con **ASP.NET Core MVC (.NET 8)** y **PostgreSQL**.

Gestiona el ciclo completo de venta de una PYME: clientes, proveedores, catálogo de productos, control de stock por movimientos, presupuestos, pedidos y facturación con generación de PDF y envío por correo, con acceso protegido por autenticación de usuarios (ASP.NET Core Identity) y algunas acciones restringidas por rol.

**Demo desplegada:** [Añadir URL de Railway]

---

## Índice

- [Problema que resuelve](#problema-que-resuelve)
- [Capturas](#capturas)
- [Arquitectura](#arquitectura)
- [Módulos funcionales](#módulos-funcionales)
- [Flujo de negocio: Presupuesto → Pedido → Factura](#flujo-de-negocio-presupuesto--pedido--factura)
- [Gestión de stock](#gestión-de-stock)
- [Autenticación, usuarios y roles](#autenticación-usuarios-y-roles)
- [Generación de PDF de facturas](#generación-de-pdf-de-facturas)
- [Envío de correos](#envío-de-correos)
- [Tecnologías](#tecnologías)
- [Estructura del proyecto](#estructura-del-proyecto)
- [Requisitos previos](#requisitos-previos)
- [Configuración](#configuración)
- [Puesta en marcha](#puesta-en-marcha)
- [Despliegue](#despliegue)
- [Tests](#tests)
- [Autor](#autor)

---

## Problema que resuelve

Muchas PYMEs gestionan presupuestos, pedidos, facturas y stock en hojas de cálculo sueltas, sin trazabilidad ni control de quién hizo qué. PymeCore centraliza ese flujo en una única aplicación web:

- Un presupuesto se convierte en pedido solo si el cliente lo acepta.
- Un pedido no se puede completar si no hay stock suficiente.
- Una factura se genera desde un pedido completado y queda con su propio histórico (snapshot), aunque el catálogo cambie después.
- Todos los módulos exigen usuario autenticado; algunas acciones concretas (gestión de usuarios, borrado de clientes y proveedores) exigen además el rol Administrador.

---

## Capturas

_Pendiente de incorporar. Rutas previstas (añadir las imágenes y descomentar cuando estén disponibles):_

```markdown
<!-- ![Dashboard](docs/screenshots/dashboard.png) -->
<!-- ![Listado de presupuestos](docs/screenshots/presupuestos.png) -->
<!-- ![Detalle de pedido](docs/screenshots/pedido-detalle.png) -->
<!-- ![Factura en PDF](docs/screenshots/factura-pdf.png) -->
<!-- ![Login](docs/screenshots/login.png) -->
```

---

## Arquitectura

Arquitectura MVC clásica de ASP.NET Core, sin capas adicionales (sin Clean Architecture, sin CQRS, sin microservicios):

```
Controllers  →  Services  →  Data (ApplicationDbContext, EF Core)  →  PostgreSQL
     ↓              ↓
  ViewModels      Models
     ↓
   Views (Razor + Bootstrap + AdminLTE)
```

- **Controllers**: reciben la petición HTTP, validan el `ModelState` y delegan en un `Service`. No contienen lógica de negocio.
- **Services**: concentran las reglas de negocio (transiciones de estado, cálculo de totales, movimientos de stock, envío de email, generación de PDF).
- **Models**: entidades persistidas por Entity Framework Core (`Cliente`, `Producto`, `Presupuesto`, `Pedido`, `Factura`, `MovimientoStock`...).
- **ViewModels**: forma de los datos que necesita cada pantalla (formularios, listados), separados de las entidades.
- **Data**: `ApplicationDbContext` (EF Core + Identity) y las migraciones.
- **Views**: Razor Views con Bootstrap y la plantilla AdminLTE para el layout, sidebar y navbar (el login y el registro, en `Areas/Identity`, sí usan Razor Pages, propio de ASP.NET Core Identity).

---

## Módulos funcionales

| Módulo | Qué gestiona |
|---|---|
| **Clientes** | Alta, edición y desactivación (no se borran si tienen histórico). |
| **Proveedores** | Igual que clientes: desactivación en lugar de borrado cuando hay histórico. |
| **Productos** | Catálogo, precios y proveedor asociado (obligatorio). |
| **Stock** | Historial de movimientos (Entrada, Salida, Devolución, Ajuste) por producto. |
| **Presupuestos** | Creación, líneas, envío por email al cliente con respuesta pública (aceptar/rechazar). |
| **Pedidos** | Se generan desde un presupuesto aceptado; transiciones de estado con control de stock. |
| **Facturas** | Se generan desde un pedido completado; snapshot histórico, PDF y envío por email. |
| **Usuarios** | Listado, cambio de rol, activación/desactivación de cuentas (solo Administrador). |
| **Dashboard** | Panel con métricas clave: clientes activos, stock bajo, presupuestos enviados, facturas pendientes, últimos pedidos. |

---

## Flujo de negocio: Presupuesto → Pedido → Factura

```
Presupuesto                Pedido                    Factura
Borrador                                                
   │ EnviarAsync()
   ▼
Enviado ──(cliente responde por email, sin login)──┐
   │                                                │
   ▼                                                ▼
Aceptado                                       Rechazado
   │ CrearDesdePresupuestoAsync()
   ▼
                          Pendiente
                             │ CambiarEstadoAsync()
                             ▼
                        EnPreparacion ──► Cancelado
                             │
                             │ (valida stock de TODAS las líneas
                             │  antes de descontar nada)
                             ▼
                        Completado
                             │ GenerarDesdePedidoAsync()
                             ▼
                                                  Pendiente
                                                     │
                                            ┌────────┴────────┐
                                            ▼                 ▼
                                         Pagada            Anulada
```

Reglas verificadas en el código (`PresupuestoService`, `PedidoService`, `FacturaService`):

- Un presupuesto solo se puede enviar en estado **Borrador** y con al menos una línea. Al enviarlo se genera un **token de un solo uso** (32 bytes aleatorios, hash SHA-256 almacenado, caduca a los 7 días) que permite al cliente aceptar o rechazar desde el correo **sin tener cuenta** en la aplicación.
- Un pedido solo se crea a partir de un presupuesto en estado **Aceptado** que no haya sido convertido antes (`PresupuestoId` único por pedido).
- Un pedido solo puede pasar a **Completado** si **todas** sus líneas tienen stock suficiente; si falta stock en alguna, no se descuenta nada. El descuento de stock de todas las líneas y el cambio de estado ocurren en una única transacción (si algo falla a mitad de camino, se deshace todo).
- Una factura solo se genera desde un pedido **Completado** y **no puede haber más de una factura por pedido**.
- La factura guarda un **snapshot** (datos de empresa, cliente y líneas en el momento de facturar) para que el histórico no cambie aunque el catálogo o los datos del cliente se modifiquen después.
- Los números de documento (`PRES-2026-001`, `PED-2026-001`, `FAC-2026-001`) son correlativos por año y protegidos con un índice único en base de datos; si dos peticiones compiten por el mismo número, se reintenta automáticamente con el siguiente.

---

## Gestión de stock

Cada movimiento de stock (Entrada, Salida, Devolución, Ajuste) se registra en `MovimientoStock` y actualiza `Producto.StockActual`.

La resta de stock se hace de forma **atómica** directamente en PostgreSQL (`ExecuteUpdateAsync` con condición `StockActual + delta >= 0`), no leyendo el valor en memoria y restando después. Esto evita que dos peticiones simultáneas dejen el stock en negativo por una condición de carrera.

---

## Autenticación, usuarios y roles

Implementado con **ASP.NET Core Identity** sobre PostgreSQL.

- Roles: **Administrador** y **Usuario**.
- Por defecto, **toda la aplicación exige usuario autenticado** (`FallbackPolicy` global en `Program.cs`); las páginas públicas (login, registro, respuesta a presupuestos) están marcadas explícitamente con `[AllowAnonymous]`.
- El rol **Administrador** protege: gestión de usuarios (`UsuariosController`), y el borrado de Clientes y Proveedores.
- Un administrador inicial se siembra automáticamente al arrancar la aplicación (`IdentitySeeder`) a partir de la configuración `SeedAdmin:Email` / `SeedAdmin:Password`; esa cuenta no puede ser desactivada ni modificada desde el propio módulo de usuarios.
- Un usuario no puede desactivar ni editar el rol de su propia cuenta.

---

## Generación de PDF de facturas

Los PDF de factura se generan con **QuestPDF** a partir del snapshot histórico de la factura (no de los datos "en vivo" de cliente/producto), garantizando que el documento no cambie aunque el catálogo se actualice después.

---

## Envío de correos

Implementado con **MailKit**, con dos flujos distintos:

- **Presupuestos**: correo HTML con el detalle de líneas y dos botones (aceptar / rechazar) que enlazan a una página pública protegida por el token de un solo uso.
- **Facturas**: correo HTML con el PDF de la factura adjunto.

---

## Tecnologías

| Categoría | Tecnología |
|---|---|
| Backend | .NET 8, ASP.NET Core MVC |
| ORM / BD | Entity Framework Core 8, PostgreSQL (Npgsql) |
| Autenticación | ASP.NET Core Identity (roles) |
| PDF | QuestPDF |
| Email | MailKit |
| Frontend | Razor Views, Bootstrap, plantilla AdminLTE, jQuery |
| Tests | xUnit, EF Core InMemory / Sqlite |
| Despliegue | Docker (rama `production`), Railway |

---

## Estructura del proyecto

```
PymeCore/
├── PymeCore/                  Proyecto principal (ASP.NET Core MVC)
│   ├── Areas/Identity/        Páginas de login y registro (Identity)
│   ├── Controllers/           Clientes, Proveedores, Productos, Stock,
│   │                          Presupuestos, Pedidos, Facturas, Usuarios, Home
│   ├── Data/                  ApplicationDbContext, IdentitySeeder, AppRoles
│   ├── Dtos/Facturas/         DTOs del snapshot histórico de factura
│   ├── Migrations/            Migraciones de EF Core
│   ├── Models/                Entidades: Cliente, Producto, Presupuesto,
│   │                          Pedido, Factura, MovimientoStock...
│   ├── Pdf/                   Documento QuestPDF de la factura
│   ├── Services/               Lógica de negocio (un servicio por módulo)
│   ├── ViewModels/            Modelos específicos de cada pantalla
│   ├── Views/                 Vistas Razor por controlador
│   ├── wwwroot/                Bootstrap, AdminLTE, jQuery, CSS/JS propios
│   └── Program.cs             Configuración de servicios y pipeline HTTP
├── PymeCore.Tests/            Tests xUnit (servicios, controladores, validaciones)
└── PymeCore.sln
```

---

## Requisitos previos

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- PostgreSQL en ejecución (local o remoto)
- Una cuenta SMTP para el envío de correos (por ejemplo, Gmail con contraseña de aplicación)

---

## Configuración

La configuración sensible **no** está en el repositorio. `PymeCore/appsettings.example.json` documenta las claves necesarias, sin valores reales:

```
ConnectionStrings:DefaultConnection

EmpresaOptions:Nombre
EmpresaOptions:Cif
EmpresaOptions:Direccion

EmailSettings:Host
EmailSettings:Port
EmailSettings:Username
EmailSettings:Password
EmailSettings:FromEmail
EmailSettings:FromName
```

Además, para que arranque la siembra del administrador inicial (`IdentitySeeder`) hace falta:

```
SeedAdmin:Email
SeedAdmin:Password
```

Pasos:

1. Copia `PymeCore/appsettings.example.json` como `PymeCore/appsettings.json` (ignorado por git) y rellena tus propios valores.
2. Añade `SeedAdmin:Email` y `SeedAdmin:Password` mediante [User Secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) (recomendado, no van en `appsettings.json`):

```powershell
cd PymeCore
dotnet user-secrets set "SeedAdmin:Email" "admin@tuempresa.com"
dotnet user-secrets set "SeedAdmin:Password" "UnaContraseñaSegura123!"
```

---

## Puesta en marcha

Desde la raíz del repositorio:

```powershell
dotnet restore
dotnet tool restore
```

`dotnet tool restore` instala `dotnet-ef` como herramienta local (declarada en `.config/dotnet-tools.json`); sin este paso, el comando `dotnet ef` de más abajo no existe en una instalación limpia.

Desde `PymeCore/` (aplica las migraciones y crea las tablas en PostgreSQL):

```powershell
cd PymeCore
dotnet ef database update
```

Arrancar la aplicación:

```powershell
dotnet run
```

Al iniciar por primera vez, `Program.cs` crea automáticamente los roles `Administrador`/`Usuario` y el usuario administrador definido en `SeedAdmin`.

---

## Despliegue

> El `Dockerfile`, el `.dockerignore` y la configuración de runtime para Railway viven en la rama `production` de este repositorio (commit `feat(deploy): preparar despliegue en Railway con Dockerfile y config de runtime`), no en `dev-ismael`.

En esa rama, el `Dockerfile` de la raíz define cómo se construye la imagen de la aplicación, pero Railway **no** construye esa imagen directamente desde el repositorio. El flujo real es:

1. Se construye la imagen localmente a partir del `Dockerfile` (`docker build`).
2. Esa imagen se sube a Docker Hub (`docker push`).
3. Railway se configura para desplegar esa imagen ya construida desde Docker Hub, no para hacer build desde el código fuente.

Sobre el `Dockerfile` en sí:

- **Build multi-stage**: la etapa de build usa `mcr.microsoft.com/dotnet/sdk:8.0` para hacer `dotnet restore` y `dotnet publish` de `PymeCore.csproj`; la etapa final usa la imagen ligera `mcr.microsoft.com/dotnet/aspnet:8.0` y arranca con `dotnet PymeCore.dll`.
- **`.dockerignore`**: excluye `bin/`, `obj/`, `node_modules/`, `.git/`, `.vs/`, `graphify-out/` y, explícitamente, `PymeCore/appsettings.json` y `PymeCore/appsettings.Development.json`, para que ningún archivo de configuración local llegue a la imagen.

Actualizar el despliegue significa repetir el ciclo build → push a Docker Hub → redeploy en Railway de la nueva imagen; no hay ningún workflow de CI/CD en el repositorio que automatice este proceso, por lo que hoy es un paso manual.
- **`Program.cs`** adapta el arranque al entorno de Railway solo si detecta estas variables (si no existen, se comporta igual que en local):
  - `PORT`: si está definida, la app escucha en `http://0.0.0.0:{PORT}`.
  - `DATABASE_URL`: si está definida, construye la cadena de conexión a PostgreSQL a partir de ella; si no, usa `ConnectionStrings:DefaultConnection` como en local.
  - Cabeceras reenviadas (`X-Forwarded-For` / `X-Forwarded-Proto`) para funcionar correctamente detrás del proxy de Railway.
  - Las migraciones (`Database.Migrate()`) se aplican también en producción al arrancar, no solo en desarrollo.

El resto de configuración (`EmpresaOptions`, `EmailSettings`, `SeedAdmin`) es la misma descrita en [Configuración](#configuración); en Railway se define como variables de entorno usando el separador `__` de ASP.NET Core, por ejemplo `EmailSettings__Host`, `EmailSettings__Password`, `SeedAdmin__Email`, etc. — nunca hardcodeadas en el `Dockerfile` ni en el repositorio.

---

## Tests

Desde la raíz del repositorio:

```powershell
dotnet test
```

La suite (`PymeCore.Tests`) cubre servicios de negocio, controladores y validaciones de ViewModels con xUnit, usando EF Core InMemory y Sqlite para aislar cada test de una base de datos real.

---

## Autor

**Ismael Figuera** — [GitHub @ismaelFiguera1](https://github.com/ismaelFiguera1) · [LinkedIn](https://www.linkedin.com/in/ismael-figuera-farré-70b559376)
