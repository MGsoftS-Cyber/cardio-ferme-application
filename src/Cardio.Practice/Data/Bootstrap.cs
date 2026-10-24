using Cardio.Domain;
using Microsoft.AspNetCore.Identity;

namespace Cardio.Data;

public static class Bootstrap
{
    public static async Task Run(
        IServiceProvider services,
        IConfiguration config)
    {
        using var scope = services.CreateScope();

        var roles = scope.ServiceProvider
            .GetRequiredService<RoleManager<IdentityRole>>();

        var users = scope.ServiceProvider
            .GetRequiredService<UserManager<User>>();

        foreach (var name in new[] { "Doctor", "Nurse", "Receptionist" })
        {
            if (!await roles.RoleExistsAsync(name))
                Check(await roles.CreateAsync(new IdentityRole(name)));
        }

        var email = config["Bootstrap:Email"]
            ?? throw new InvalidOperationException("Set Bootstrap:Email.");

        var password = config["Bootstrap:Password"]
            ?? throw new InvalidOperationException("Set Bootstrap:Password.");

        var existing = await users.FindByEmailAsync(email);

        if (existing is not null)
        {
            if (!await users.IsInRoleAsync(existing, "Doctor"))
            {
                throw new InvalidOperationException(
                    "Refusing to promote an existing account.");
            }

            return;
        }

        var user = new User
        {
            UserName = email,
            Email = email,
            DisplayName = config["Bootstrap:Name"] ?? "Cardiologist",
            EmailConfirmed = true
        };

        Check(await users.CreateAsync(user, password));
        Check(await users.AddToRoleAsync(user, "Doctor"));
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                string.Join("; ", result.Errors.Select(x => x.Description)));
        }
    }
}
