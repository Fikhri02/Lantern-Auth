using Lantern.Api.Auth;
using Lantern.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddLanternJwt(builder.Configuration);
builder.Services.AddLanternPolicies();
builder.Services.AddSingleton<Lantern.Api.Data.DemoStore>();
builder.Services.AddHttpClient<Lantern.Api.Keycloak.KeycloakAdminClient>();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapMeEndpoints();
app.MapHqEndpoints();
app.MapPurchaseOrderEndpoints();
app.MapOutletEndpoints();
app.MapStaffEndpoints();
app.MapSalesEndpoints();

app.Run();

public partial class Program;
