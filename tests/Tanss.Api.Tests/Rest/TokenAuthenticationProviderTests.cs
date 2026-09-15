using Microsoft.Kiota.Abstractions;
using Tanss.Api.Rest;
using Tanss.Api.Tests.Fakes;
using Xunit;

namespace Tanss.Api.Tests.Rest;

/// <summary>Die Kopfzeile <c>apiToken</c>: genau ein <c>Bearer</c>, bei jedem Aufruf frisch.</summary>
public sealed class TokenAuthenticationProviderTests
{
    [Theory]
    // Das Token aus POST /api/v1/login traegt das Praefix schon: unveraendert senden.
    [InlineData("Bearer abc.def.ghi", "Bearer abc.def.ghi")]
    // Ohne Praefix wird es ergaenzt - sonst ueberspringt TANSS die Pruefung und antwortet 403.
    [InlineData("abc.def.ghi", "Bearer abc.def.ghi")]
    [InlineData("  abc.def.ghi \n", "Bearer abc.def.ghi")]
    // Eine andere Schreibweise zaehlt als vorhandenes Praefix, wird aber woertlich gemacht.
    [InlineData("bearer abc.def.ghi", "Bearer abc.def.ghi")]
    public void Die_Kopfzeile_traegt_genau_ein_Bearer(string stored, string expected)
    {
        Assert.Equal(expected, TanssApiTokenAuthenticationProvider.HeaderValue(stored));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Bearer ")]
    public void Ohne_Token_gibt_es_keinen_Wert(string? stored)
    {
        Assert.Null(TanssApiTokenAuthenticationProvider.HeaderValue(stored));
    }

    [Fact]
    public async Task Setzt_apiToken_und_ersetzt_einen_von_Hand_gesetzten_Wert()
    {
        FakeTokenProvider tokens = new(n => $"Bearer token.nummer.{n}");
        TanssApiTokenAuthenticationProvider provider = new(tokens);
        RequestInformation request = new()
        {
            HttpMethod = Method.GET,
            UrlTemplate = "{+baseurl}/api/v1/timers",
        };
        request.Headers.Add("apiToken", "Bearer von.hand.gesetzt");

        await provider.AuthenticateRequestAsync(request);
        Assert.True(request.Headers.TryGetValue("apiToken", out IEnumerable<string>? first));
        Assert.Equal(["Bearer token.nummer.1"], first);

        await provider.AuthenticateRequestAsync(request);
        Assert.True(request.Headers.TryGetValue("apiToken", out IEnumerable<string>? second));
        Assert.Equal(["Bearer token.nummer.2"], second);

        Assert.False(request.Headers.ContainsKey("Authorization"));
        Assert.Equal(2, tokens.Reads);
    }

    [Fact]
    public async Task Ohne_Token_bleibt_die_Kopfzeile_weg()
    {
        TanssApiTokenAuthenticationProvider provider = new(new FakeTokenProvider(token: null));
        RequestInformation request = new()
        {
            HttpMethod = Method.POST,
            UrlTemplate = "{+baseurl}/api/v1/login",
        };

        await provider.AuthenticateRequestAsync(request);

        Assert.False(request.Headers.ContainsKey("apiToken"));
    }
}
