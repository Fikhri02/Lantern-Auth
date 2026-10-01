using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lantern.IntegrationTests.Infrastructure;

namespace Lantern.IntegrationTests;

[Collection(KeycloakCollection.Name)]
public sealed class SalesTests(KeycloakFixture kc)
{
    private static readonly object TwoCoffeesOneMilk = new
    {
        items = new object[] { new { sku = "SKU-100", quantity = 2 }, new { sku = "SKU-200", quantity = 1 } }
    };

    private async Task<HttpResponseMessage> RingAsync(string token, string outlet, object? sale = null) =>
        await kc.Api.ClientWithToken(token).PostAsJsonAsync($"/outlets/{outlet}/sales", sale ?? TwoCoffeesOneMilk);

    [Fact]
    public async Task Cashier_rings_a_sale_and_receipt_names_cashier_and_till()
    {
        var till = await kc.OpenTillAsync("/Outlets/Bangsar");
        var token = await kc.CashierTokenAsync(till, "c-1001", "1111");

        var response = await RingAsync(token, "BGS");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var receipt = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Siti Aminah (c-1001)", receipt.GetProperty("servedBy").GetString());
        Assert.Equal(till.Account, receipt.GetProperty("tillAccount").GetString());
        Assert.Equal("BGS", receipt.GetProperty("outletId").GetString());
        Assert.Equal(99.50m, receipt.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task Same_cashier_on_two_tills_gets_each_till_on_its_receipt()
    {
        var tillA = await kc.OpenTillAsync("/Outlets/Bangsar");
        var tillB = await kc.OpenTillAsync("/Outlets/Bangsar");

        var onA = await (await RingAsync(await kc.CashierTokenAsync(tillA, "c-1002", "2222"), "BGS")).Content.ReadFromJsonAsync<JsonElement>();
        var onB = await (await RingAsync(await kc.CashierTokenAsync(tillB, "c-1002", "2222"), "BGS")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(tillA.Account, onA.GetProperty("tillAccount").GetString());
        Assert.Equal(tillB.Account, onB.GetProperty("tillAccount").GetString());
    }

    [Fact]
    public async Task Cashier_cannot_sell_for_another_outlet()
    {
        var till = await kc.OpenTillAsync("/Outlets/Bangsar");
        var response = await RingAsync(await kc.CashierTokenAsync(till, "c-1001", "1111"), "KLC");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("OutletSell", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("policy").GetString());
    }

    [Fact]
    public async Task Outlet_account_itself_cannot_ring_sales()
    {
        var till = await kc.OpenTillAsync("/Outlets/Bangsar");
        Assert.Equal(HttpStatusCode.Forbidden, (await RingAsync(till.AccessToken, "BGS")).StatusCode);
    }

    [Fact]
    public async Task Hq_admin_cannot_ring_sales()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await RingAsync(await kc.GetUserTokenAsync("aisha.admin"), "BGS")).StatusCode);
    }

    [Theory]
    [InlineData("mgr.bangsar", HttpStatusCode.OK)]
    [InlineData("aisha.admin", HttpStatusCode.OK)]
    [InlineData("mgr.klcc", HttpStatusCode.Forbidden)]
    public async Task Receipts_are_readable_by_that_outlets_manager(string reader, HttpStatusCode expected)
    {
        var till = await kc.OpenTillAsync("/Outlets/Bangsar");
        var sale = await (await RingAsync(await kc.CashierTokenAsync(till, "c-1001", "1111"), "BGS")).Content.ReadFromJsonAsync<JsonElement>();

        var response = await (await kc.Api.ClientAsAsync(reader)).GetAsync($"/outlets/BGS/sales/{sale.GetProperty("saleId").GetString()}/receipt");

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_sale_is_404_with_reason()
    {
        var response = await (await kc.Api.ClientAsAsync("mgr.bangsar")).GetAsync($"/outlets/BGS/sales/{Guid.NewGuid()}/receipt");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Sale not found.", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
    }

    public static TheoryData<object> InvalidSales => new()
    {
        new { items = Array.Empty<object>() },
        new { items = new object[] { new { sku = "SKU-999", quantity = 1 } } },
        new { items = new object[] { new { sku = "SKU-100", quantity = 0 } } }
    };

    [Theory]
    [MemberData(nameof(InvalidSales))]
    public async Task Invalid_sale_is_400(object sale)
    {
        var till = await kc.OpenTillAsync("/Outlets/Bangsar");
        var response = await RingAsync(await kc.CashierTokenAsync(till, "c-1001", "1111"), "BGS", sale);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
