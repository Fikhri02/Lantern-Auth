using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lantern.IntegrationTests.Infrastructure;

namespace Lantern.IntegrationTests;

[Collection(KeycloakCollection.Name)]
public sealed class PurchaseOrderApprovalTests(KeycloakFixture kc)
{
    private async Task<JsonElement> RaiseAsync(string user, string supplierId = "SUP-01", decimal amount = 1200.50m)
    {
        var client = await kc.Api.ClientAsAsync(user);
        var response = await client.PostAsJsonAsync("/purchase-orders", new { supplierId, amount });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<HttpResponseMessage> ApproveAsync(string user, string id) =>
        await (await kc.Api.ClientAsAsync(user)).PostAsync($"/purchase-orders/{id}/approve", null);

    [Fact]
    public async Task Procurement_raises_and_finance_approves()
    {
        var po = await RaiseAsync("eric.procure");
        Assert.Equal("Pending", po.GetProperty("status").GetString());
        Assert.Equal("Eric Tan", po.GetProperty("createdByName").GetString());

        var response = await ApproveAsync("farah.finance", po.GetProperty("id").GetString()!);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var approved = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Approved", approved.GetProperty("status").GetString());
        Assert.Equal("Farah Ismail", approved.GetProperty("approvedByName").GetString());
    }

    [Fact]
    public async Task Dual_role_user_cannot_approve_own_order()
    {
        var po = await RaiseAsync("hana.dual");

        var response = await ApproveAsync("hana.dual", po.GetProperty("id").GetString()!);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("FinanceApprove", problem.GetProperty("policy").GetString());
    }

    [Fact]
    public async Task Dual_role_user_can_approve_someone_elses_order()
    {
        var po = await RaiseAsync("eric.procure");
        var response = await ApproveAsync("hana.dual", po.GetProperty("id").GetString()!);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Procurement_without_finance_cannot_approve()
    {
        var po = await RaiseAsync("eric.procure");

        var response = await ApproveAsync("eric.procure", po.GetProperty("id").GetString()!);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("FinanceRole", problem.GetProperty("policy").GetString());
    }

    [Fact]
    public async Task Second_approval_is_409_and_keeps_first_approver()
    {
        var po = await RaiseAsync("eric.procure");
        var id = po.GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await ApproveAsync("farah.finance", id)).StatusCode);

        var second = await ApproveAsync("hana.dual", id);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var list = await (await kc.Api.ClientAsAsync("farah.finance")).GetFromJsonAsync<JsonElement>("/purchase-orders");
        var stored = list.EnumerateArray().Single(p => p.GetProperty("id").GetString() == id);
        Assert.Equal("Farah Ismail", stored.GetProperty("approvedByName").GetString());
    }

    [Fact]
    public async Task Unknown_order_is_404()
    {
        var response = await ApproveAsync("farah.finance", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Purchase order not found.", problem.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("SUP-99", 100)]
    [InlineData("SUP-01", 0)]
    [InlineData("SUP-01", -5)]
    public async Task Invalid_order_is_400(string supplierId, decimal amount)
    {
        var client = await kc.Api.ClientAsAsync("eric.procure");
        var response = await client.PostAsJsonAsync("/purchase-orders", new { supplierId, amount });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("dina.marketing", HttpStatusCode.Forbidden)]
    [InlineData("farah.finance", HttpStatusCode.OK)]
    [InlineData("aisha.admin", HttpStatusCode.OK)]
    public async Task Listing_orders_follows_roles(string user, HttpStatusCode expected)
    {
        var response = await (await kc.Api.ClientAsAsync(user)).GetAsync("/purchase-orders");
        Assert.Equal(expected, response.StatusCode);
    }
}
