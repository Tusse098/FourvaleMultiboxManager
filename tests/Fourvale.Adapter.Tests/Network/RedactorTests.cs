using System.Text.Json.Nodes;
using Fourvale.Adapter;
using Fourvale.Adapter.Network;

namespace Fourvale.Adapter.Tests.Network;

public class RedactorTests
{
    private readonly Redactor _redactor = TestSupport.NewRedactor();

    [Theory]
    [InlineData("token")]
    [InlineData("reconnectionToken")]
    [InlineData("accessToken")]
    [InlineData("password")]
    [InlineData("newPassword")]
    [InlineData("Authorization")]
    [InlineData("cookie")]
    [InlineData("email")]
    [InlineData("challenge")]
    public void Secret_keys_are_redacted_at_any_depth(string key)
    {
        var node = JsonNode.Parse($$$"""{"outer":{"list":[{"{{{key}}}":"s3cret-value"}]}}""");
        var count = 0;

        var result = _redactor.Redact(node, isChat: false, ref count)!.ToJsonString(CaptureJson.Options);

        Assert.DoesNotContain("s3cret-value", result);
        Assert.Contains(Redactor.Placeholder, result);
        Assert.Equal(1, count);
    }

    [Fact]
    public void Game_fields_are_kept()
    {
        var node = JsonNode.Parse("""{"hp":120,"maxHp":200,"level":7,"mapId":"town_a1","name":"PlayerA","sessionId":"abc"}""");
        var count = 0;

        var result = _redactor.Redact(node, isChat: false, ref count)!.ToJsonString(CaptureJson.Options);

        Assert.Equal(0, count);
        Assert.Contains("\"hp\":120", result);
        Assert.Contains("town_a1", result);
    }

    [Fact]
    public void Emails_and_jwts_inside_free_text_are_replaced()
    {
        var node = JsonNode.Parse("""{"info":"mail me at someone@example.com, key eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.abcdefgh"}""");
        var count = 0;

        var result = _redactor.Redact(node, isChat: false, ref count)!.ToJsonString(CaptureJson.Options);

        Assert.DoesNotContain("someone@example.com", result);
        Assert.DoesNotContain("eyJhbGci", result);
        Assert.Equal(2, count);
    }

    [Fact]
    public void Chat_messages_lose_text_and_names_but_keep_scope()
    {
        Assert.True(_redactor.IsChatType("chatMsg"));
        Assert.True(_redactor.IsChatType("chat"));
        Assert.False(_redactor.IsChatType("battleState"));

        var node = JsonNode.Parse("""{"scope":"global","text":"hello there","from":"PlayerB","verified":true}""");
        var count = 0;

        var result = _redactor.Redact(node, isChat: true, ref count)!.ToJsonString(CaptureJson.Options);

        Assert.DoesNotContain("hello there", result);
        Assert.DoesNotContain("PlayerB", result);
        Assert.Contains("\"scope\":\"global\"", result);
        Assert.Contains("\"verified\":true", result);
    }

    [Fact]
    public void Non_chat_messages_keep_text_fields()
    {
        var node = JsonNode.Parse("""{"text":"Quest complete"}""");
        var count = 0;

        var result = _redactor.Redact(node, isChat: false, ref count)!.ToJsonString(CaptureJson.Options);

        Assert.Contains("Quest complete", result);
    }

    [Fact]
    public void Url_query_values_are_replaced_and_names_kept()
    {
        var count = 0;

        var url = _redactor.SanitizeUrl("wss://api.fourvale.com/abc123/room9?sessionId=s1&reconnectionToken=SECRET#frag", ref count);

        Assert.Equal("wss://api.fourvale.com/abc123/room9?sessionId=<REDACTED>&reconnectionToken=<REDACTED>", url);
        Assert.Equal(2, count);
    }

    [Theory]
    [InlineData("/api/login", true)]
    [InlineData("/api/login/2fa", true)]
    [InlineData("/api/logout", true)]
    [InlineData("/api/register", true)]
    [InlineData("/api/trial", true)]
    [InlineData("/api/2fa/setup", true)]
    [InlineData("/api/password/forgot", true)]
    [InlineData("/api/rankings", false)]
    [InlineData("/api/guild/create", false)]
    [InlineData("/matchmake/joinOrCreate/town", false)]
    public void Auth_paths_are_detected(string path, bool expected)
    {
        Assert.Equal(expected, _redactor.IsAuthPath(path));
    }

    [Fact]
    public void Matchmake_response_loses_reconnection_token()
    {
        var node = JsonNode.Parse("""{"room":{"roomId":"r1","processId":"p1","name":"town"},"sessionId":"s1","reconnectionToken":"TOPSECRET"}""");
        var count = 0;

        var result = _redactor.Redact(node, isChat: false, ref count)!.ToJsonString(CaptureJson.Options);

        Assert.DoesNotContain("TOPSECRET", result);
        Assert.Contains("\"roomId\":\"r1\"", result);
    }

    [Fact]
    public void Config_without_redaction_patterns_is_rejected()
    {
        Assert.Throws<InvalidDataException>(() => AdapterRules.Parse("""{"redactKeyPattern":""}"""));
    }
}
