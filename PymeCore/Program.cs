using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using PymeCore.Data;
using PymeCore.Models;
using PymeCore.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<IdentityUser>(options => options.SignIn.RequireConfirmedAccount = true)
    .AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.AddControllersWithViews();
builder.Services.AddScoped<ClienteService>();
builder.Services.AddScoped<ProveedorService>();
builder.Services.AddScoped<ProductoService>();
builder.Services.AddScoped<StockService>();
builder.Services.AddScoped<PresupuestoService>();
builder.Services.AddScoped<PedidoService>();
builder.Services.AddScoped<FacturaService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    context.Database.Migrate();

    context.Productos.RemoveRange(context.Productos);
    context.Clientes.RemoveRange(context.Clientes);
    context.Proveedores.RemoveRange(context.Proveedores);
    context.SaveChanges();

    context.Clientes.AddRange(
        new Cliente { Nombre = "Empresa ABC SL",        Nif = "12345678A", Email = "abc@empresa.com",       Telefono = "612345678", Ciudad = "Madrid",    Activo = true  },
        new Cliente { Nombre = "Comercial XYZ SA",      Nif = "23456789B", Email = "xyz@comercial.com",      Telefono = "623456789", Ciudad = "Barcelona", Activo = true  },
        new Cliente { Nombre = "Servicios Norte SL",    Nif = "34567890C", Email = "info@snorte.com",        Telefono = "634567890", Ciudad = "Bilbao",    Activo = true  },
        new Cliente { Nombre = "Distribuciones Sur SA", Nif = "45678901D", Email = "sur@distribuciones.com", Telefono = "645678901", Ciudad = "Sevilla",   Activo = true  },
        new Cliente { Nombre = "Antigua Empresa SL",    Nif = "56789012E", Email = "antigua@empresa.com",    Telefono = "656789012", Ciudad = "Valencia",  Activo = false }
    );

    context.Proveedores.AddRange(
        new Proveedor { Nombre = "Suministros Industriales SA", Cif = "A12345678", PersonaContacto = "Carlos Ruiz",   Email = "carlos@suministros.com",  Telefono = "911234567", Activo = true  },
        new Proveedor { Nombre = "Materiales del Norte SL",     Cif = "B23456789", PersonaContacto = "Ana Martínez",  Email = "ana@mnorte.com",          Telefono = "922345678", Activo = true  },
        new Proveedor { Nombre = "Tech Components SL",          Cif = "B34567890", PersonaContacto = "Luis García",   Email = "luis@techcomponents.com",  Telefono = "933456789", Activo = true  },
        new Proveedor { Nombre = "Logística Express SA",        Cif = "A45678901", PersonaContacto = "María López",   Email = "maria@logexpress.com",     Telefono = "944567890", Activo = true  },
        new Proveedor { Nombre = "Proveedor Inactivo SL",       Cif = "C56789012", PersonaContacto = null,            Email = "info@inactivo.com",        Telefono = null,        Activo = false }
    );
    context.SaveChanges();

    var pSuministros = context.Proveedores.First(p => p.Nombre == "Suministros Industriales SA");
    var pTech        = context.Proveedores.First(p => p.Nombre == "Tech Components SL");
    var pMateriales  = context.Proveedores.First(p => p.Nombre == "Materiales del Norte SL");

    context.Productos.AddRange(
        new Producto { Nombre = "Tornillo M8 x 30",        Sku = "TOR-M8-30",   PrecioCoste = 0.05m,  PrecioVenta = 0.12m,  StockActual = 500,  StockMinimo = 200, ProveedorId = pSuministros.Id },
        new Producto { Nombre = "Cable USB-C 1m",          Sku = "CAB-USBC-1M", PrecioCoste = 2.50m,  PrecioVenta = 6.99m,  StockActual = 80,   StockMinimo = 50,  ProveedorId = pTech.Id },
        new Producto { Nombre = "Panel de madera 200x100", Sku = "PAN-MAD-200",  PrecioCoste = 15.00m, PrecioVenta = 32.00m, StockActual = 12,   StockMinimo = 20,  ProveedorId = pMateriales.Id },
        new Producto { Nombre = "Teclado inalámbrico",     Sku = "TEC-INAL-01", PrecioCoste = 18.00m, PrecioVenta = 39.99m, StockActual = 3,    StockMinimo = 10,  ProveedorId = pTech.Id },
        new Producto { Nombre = "Cinta de embalaje 50m",  Sku = "CIN-EMB-50",  PrecioCoste = 0.80m,  PrecioVenta = 1.99m,  StockActual = 150,  StockMinimo = 50,  ProveedorId = null }
    );
    context.SaveChanges();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(CultureInfo.InvariantCulture),
    SupportedCultures     = new[] { CultureInfo.InvariantCulture },
    SupportedUICultures   = new[] { CultureInfo.InvariantCulture }
});

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

app.Run();
