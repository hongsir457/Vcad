using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Vcad.AgentLite;
using Vcad.AgentLite.Providers;
using Xunit;

namespace Vcad.AgentLite.Tests;

[Collection("agent-env")]
public class ProviderConfigForwardingTests
{
    [Fact]
    public async Task Deepseek_provider_uses_request_base_url_model_and_key()
    {
        Environment.SetEnvironmentVariable("VCAD_AGENT_PROVIDER", null);
        Environment.SetEnvironmentVariable("VCAD_AGENT_API_KEY", null);
        Environment.SetEnvironmentVariable("VCAD_AGENT_BASE_URL", null);
        Environment.SetEnvironmentVariable("VCAD_AGENT_MODEL", null);

        using var server = new OneShotHttpServer();
        var provider = new OpenAiProvider();

        var dsl = await provider.ParseAsync(new ParseRequest
        {
            text = "draw a rectangle",
            provider = new ProviderConfig
            {
                name = "deepseek",
                base_url = server.BaseUrl,
                model = "deepseek-v4-flash",
                api_key = "sk-test12345678901234567890",
                strict_json = true,
            },
        });

        Assert.Equal("vcad_dsl_v1", dsl["version"]!.GetValue<string>());
        Assert.StartsWith("POST /chat/completions ", server.RequestLine);
        Assert.Contains("Authorization: Bearer sk-test12345678901234567890", server.Headers);
        Assert.Contains("\"model\":\"deepseek-v4-flash\"", server.Body);
        Assert.Contains("\"response_format\":{\"type\":\"json_object\"}", server.Body);
    }

    private sealed class OneShotHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly Task _task;

        public string BaseUrl { get; }
        public string RequestLine { get; private set; } = "";
        public string Headers { get; private set; } = "";
        public string Body { get; private set; } = "";

        public OneShotHttpServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            BaseUrl = "http://127.0.0.1:" + port;
            _task = Task.Run(HandleOneAsync);
        }

        public void Dispose()
        {
            try { _task.Wait(TimeSpan.FromSeconds(5)); } catch { }
            _listener.Stop();
        }

        private async Task HandleOneAsync()
        {
            using var client = await _listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();

            var buffer = new byte[64 * 1024];
            var read = await stream.ReadAsync(buffer, 0, buffer.Length);
            var request = Encoding.UTF8.GetString(buffer, 0, read);
            var split = request.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            var head = split >= 0 ? request.Substring(0, split) : request;
            Body = split >= 0 ? request.Substring(split + 4) : "";

            var lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
            RequestLine = lines.Length > 0 ? lines[0] : "";
            Headers = head;

            var contentJson = @"{""version"":""vcad_dsl_v1"",""commands"":[{""type"":""draw_text"",""text"":""OK"",""position"":[0,0],""height"":250}]}";
            var payload = JsonSerializer.Serialize(new
            {
                choices = new[]
                {
                    new { message = new { content = contentJson } },
                },
            });
            var responseBody = Encoding.UTF8.GetBytes(payload);
            var responseHead = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: application/json\r\n" +
                "Content-Length: " + responseBody.Length + "\r\n" +
                "Connection: close\r\n\r\n");
            await stream.WriteAsync(responseHead, 0, responseHead.Length);
            await stream.WriteAsync(responseBody, 0, responseBody.Length);
        }
    }
}
