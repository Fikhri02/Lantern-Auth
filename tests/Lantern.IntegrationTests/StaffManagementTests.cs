using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lantern.IntegrationTests.Infrastructure;

namespace Lantern.IntegrationTests;

[Collection(KeycloakCollection.Name)]
public sealed class StaffManagementTests(KeycloakFixture kc)
{
    private Task<HttpClient> Admin() => kc.Api.ClientAsAsync("aisha.admin");

    private async Task<JsonElement> FindStaffAsync(string username)
    {
        var all = await (await Admin()).GetFromJsonAsync<JsonElement>("/staff");
        return all.EnumerateArray().Single(s => s.GetProperty("username").GetString() == username);
    }

    private static string[] RolesOf(JsonElement staff) =>
        staff.GetProperty("roles").EnumerateArray().Select(r => r.GetString()!).ToArray();

    private static object NewStaff(string username, params string[] departments) => new
    {
        username,
        firstName = "New",
        lastName = "Starter",
        email = $"{username}@lantern.test",
        departments
    };

    private static string NewUsername() => $"new.{Guid.NewGuid():N}"[..16];

    [Fact]
    public async Task Non_admin_cannot_list_staff()
    {
        var response = await (await kc.Api.ClientAsAsync("dina.marketing")).GetAsync("/staff");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Listing_shows_effective_roles_from_groups()
    {
        var eric = await FindStaffAsync("eric.procure");
        Assert.Equal(new[] { "hq-staff", "procurement" }, RolesOf(eric));
        Assert.Contains("/HQ/Procurement", eric.GetProperty("groups").EnumerateArray().Select(g => g.GetString()));
    }

    [Fact]
    public async Task Creating_staff_assigns_department_roles_and_emails_an_invite()
    {
        var username = NewUsername();

        var response = await (await Admin()).PostAsJsonAsync("/staff", NewStaff(username, "Marketing"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(new[] { "hq-staff", "marketing" }, RolesOf(await FindStaffAsync(username)));
        Assert.True(await kc.WaitForEmailCountAsync($"{username}@lantern.test", 1) >= 1);
    }

    [Fact]
    public async Task Unknown_department_is_400()
    {
        var response = await (await Admin()).PostAsJsonAsync("/staff", NewStaff(NewUsername(), "Kitchen"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Duplicate_username_is_409()
    {
        var response = await (await Admin()).PostAsJsonAsync("/staff", NewStaff("eric.procure", "Marketing"));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Changing_departments_replaces_roles()
    {
        var username = NewUsername();
        var created = await (await Admin()).PostAsJsonAsync("/staff", NewStaff(username, "Marketing"));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        var response = await (await Admin()).PutAsJsonAsync($"/staff/{id}/departments", new { departments = new[] { "Finance" } });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(new[] { "finance", "hq-staff" }, RolesOf(await FindStaffAsync(username)));
    }

    [Fact]
    public async Task Empty_departments_is_400_and_leaves_memberships_alone()
    {
        var username = NewUsername();
        var created = await (await Admin()).PostAsJsonAsync("/staff", NewStaff(username, "Marketing"));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        var response = await (await Admin()).PutAsJsonAsync($"/staff/{id}/departments", new { departments = Array.Empty<string>() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(new[] { "hq-staff", "marketing" }, RolesOf(await FindStaffAsync(username)));
    }

    [Fact]
    public async Task Deactivate_locks_out_and_reactivate_restores()
    {
        var (id, username, _) = await kc.CreateTempUserAsync("/HQ/Marketing");
        var userClient = kc.Api.ClientWithToken(await kc.GetFreshUserTokenAsync(username));
        Assert.Equal(HttpStatusCode.OK, (await userClient.GetAsync("/me")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await (await Admin()).PostAsync($"/staff/{id}/deactivate", null)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await userClient.GetAsync("/me")).StatusCode);
        await Assert.ThrowsAsync<InvalidOperationException>(() => kc.GetFreshUserTokenAsync(username));

        Assert.Equal(HttpStatusCode.NoContent, (await (await Admin()).PostAsync($"/staff/{id}/reactivate", null)).StatusCode);
        var again = kc.Api.ClientWithToken(await kc.GetFreshUserTokenAsync(username));
        Assert.Equal(HttpStatusCode.OK, (await again.GetAsync("/me")).StatusCode);
    }

    [Fact]
    public async Task Admin_cannot_deactivate_self()
    {
        var admin = await Admin();
        var me = await admin.GetFromJsonAsync<JsonElement>("/me");

        var response = await admin.PostAsync($"/staff/{me.GetProperty("id").GetString()}/deactivate", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await FindStaffAsync("aisha.admin")).GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task Reset_password_emails_the_user()
    {
        var (id, _, email) = await kc.CreateTempUserAsync("/HQ/Marketing");

        var response = await (await Admin()).PostAsync($"/staff/{id}/reset-password", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.True(await kc.WaitForEmailCountAsync(email, 1) >= 1);
    }

    [Fact]
    public async Task Unknown_user_is_404()
    {
        var response = await (await Admin()).PostAsync($"/staff/{Guid.NewGuid()}/deactivate", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Staff member not found.", problem.GetProperty("title").GetString());
    }
}
