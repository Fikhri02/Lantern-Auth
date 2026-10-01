using Microsoft.AspNetCore.Authorization;

namespace Lantern.Api.Auth;

public static class Policies
{
    public const string HqRead = "HqRead";
    public const string PromotionsRead = "PromotionsRead";
    public const string Marketing = "Marketing";
    public const string ProcurementRead = "ProcurementRead";
    public const string Procurement = "Procurement";
    public const string PurchaseOrdersRead = "PurchaseOrdersRead";
    public const string SalesReports = "SalesReports";
    public const string FinanceRole = "FinanceRole";
    public const string FinanceApprove = "FinanceApprove";
    public const string OutletManage = "OutletManage";
    public const string OutletSell = "OutletSell";
    public const string OutletReceipts = "OutletReceipts";
    public const string StaffAdmin = "StaffAdmin";

    public static IServiceCollection AddLanternPolicies(this IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationHandler, NotCreatorHandler>();
        services.AddSingleton<IAuthorizationHandler, OutletRequirementHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ForbiddenResultHandler>();

        services.AddAuthorizationBuilder()
            .AddPolicy(HqRead, p => p.RequireRole(Roles.HqStaff, Roles.HqAdmin))
            .AddPolicy(PromotionsRead, p => p.RequireRole(Roles.HqStaff, Roles.HqAdmin, Roles.OutletManager))
            .AddPolicy(Marketing, p => p.RequireRole(Roles.Marketing))
            .AddPolicy(ProcurementRead, p => p.RequireRole(Roles.Procurement, Roles.HqAdmin))
            .AddPolicy(Procurement, p => p.RequireRole(Roles.Procurement))
            .AddPolicy(PurchaseOrdersRead, p => p.RequireRole(Roles.Procurement, Roles.Finance, Roles.HqAdmin))
            .AddPolicy(SalesReports, p => p.RequireRole(Roles.Finance, Roles.HqAdmin))
            .AddPolicy(FinanceRole, p => p.RequireRole(Roles.Finance))
            .AddPolicy(FinanceApprove, p => p.RequireRole(Roles.Finance).AddRequirements(new NotCreatorRequirement()))
            .AddPolicy(OutletManage, p => p.RequireAuthenticatedUser().AddRequirements(new OutletRequirement([Roles.OutletManager], HqAdminBypass: true)))
            .AddPolicy(OutletSell, p => p.RequireAuthenticatedUser().AddRequirements(new OutletRequirement([Roles.Cashier], HqAdminBypass: false)))
            .AddPolicy(OutletReceipts, p => p.RequireAuthenticatedUser().AddRequirements(new OutletRequirement([Roles.Cashier, Roles.OutletManager], HqAdminBypass: true)))
            .AddPolicy(StaffAdmin, p => p.RequireRole(Roles.HqAdmin));

        return services;
    }
}
