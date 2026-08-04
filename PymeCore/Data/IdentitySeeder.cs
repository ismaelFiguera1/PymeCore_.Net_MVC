using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;

namespace PymeCore.Data
{
    public static class IdentitySeeder
    {
        public static async Task SeedAsync(RoleManager<IdentityRole> roleManager)
        {
            foreach (var rol in AppRoles.Todos)
            {
                if (!await roleManager.RoleExistsAsync(rol))
                {
                    var resultado = await roleManager.CreateAsync(new IdentityRole(rol));
                    if (!resultado.Succeeded)
                    {
                        var errores = string.Join("; ", resultado.Errors.Select(e => e.Description));
                        throw new InvalidOperationException($"No se pudo crear el rol '{rol}': {errores}");
                    }
                }
            }
        }

        public static async Task SeedAdminAsync(UserManager<IdentityUser> userManager, IConfiguration configuration)
        {
            var email = configuration["SeedAdmin:Email"];
            var password = configuration["SeedAdmin:Password"];

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException(
                    "Faltan las claves de configuración 'SeedAdmin:Email' o 'SeedAdmin:Password' necesarias para crear el administrador inicial.");
            }

            var admin = await userManager.FindByEmailAsync(email);

            if (admin is null)
            {
                admin = new IdentityUser
                {
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true
                };

                var resultadoCreacion = await userManager.CreateAsync(admin, password);
                if (!resultadoCreacion.Succeeded)
                {
                    var errores = string.Join("; ", resultadoCreacion.Errors.Select(e => e.Description));
                    throw new InvalidOperationException($"No se pudo crear el administrador inicial '{email}': {errores}");
                }
            }

            if (!await userManager.IsInRoleAsync(admin, AppRoles.Administrador))
            {
                var resultadoRol = await userManager.AddToRoleAsync(admin, AppRoles.Administrador);
                if (!resultadoRol.Succeeded)
                {
                    var errores = string.Join("; ", resultadoRol.Errors.Select(e => e.Description));
                    throw new InvalidOperationException($"No se pudo asignar el rol '{AppRoles.Administrador}' al administrador inicial '{email}': {errores}");
                }
            }
        }
    }
}
