using System.Text.Json.Nodes;

namespace Lantern.IntegrationTests.Infrastructure;

public static class Jwt
{
    public static JsonNode Payload(string token)
    {
        var part = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        part = part.PadRight(part.Length + (4 - part.Length % 4) % 4, '=');
        return JsonNode.Parse(Convert.FromBase64String(part))!;
    }
}
