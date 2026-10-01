using DrinkIt.Application.Staff;

namespace DrinkIt.Api.Features.Staff;

/// <summary>What the staff administration endpoints need. Scoped: one per request, same as the DbContext below them.</summary>
internal static class StaffHandlers
{
    public static IServiceCollection AddStaffHandlers(this IServiceCollection services) => services
        .AddScoped<CreateStaffUserHandler>()
        .AddScoped<ChangeStaffUserRoleHandler>()
        .AddScoped<ResetStaffUserPasswordHandler>()
        .AddScoped<DeactivateStaffUserHandler>()
        .AddScoped<ReactivateStaffUserHandler>();
}
