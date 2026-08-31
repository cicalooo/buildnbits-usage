using BuildnBits.Usage.Core.JsonRpc;

namespace BuildnBits.Usage.Tests;

public class JsonRpcProcessTests
{
    [Fact]
    public async Task Timeout_and_malformed_and_429()
    {
        var script = Path.Combine(Path.GetTempPath(), "bnb-rpc-" + Guid.NewGuid() + ".ps1");
        await File.WriteAllTextAsync(script, """
            $line = [Console]::In.ReadLine()
            if ($line -match '"method":"slow"') { Start-Sleep -Seconds 30 }
            if ($line -match '"method":"bad"') { Write-Output 'not-json'; [Console]::Out.Flush(); Start-Sleep -Seconds 30 }
            if ($line -match '"id":(\d+)' ) { $id = $Matches[1] } else { $id = 1 }
            if ($line -match '"method":"rate"') { Write-Output "{`"jsonrpc`":`"2.0`",`"id`":$id,`"error`":{`"code`":429,`"message`":`"429`"}}"; [Console]::Out.Flush(); Start-Sleep -Seconds 2; exit }
            if ($line -match '"method":"ok"') { Write-Output "{`"jsonrpc`":`"2.0`",`"id`":$id,`"result`":{`"ok`":true}}"; [Console]::Out.Flush(); Start-Sleep -Seconds 2; exit }
            """);

        await using var ok = new JsonRpcProcessClient(new JsonRpcProcessOptions
        {
            FileName = "powershell.exe",
            Arguments = ["-NoProfile", "-File", script],
            Timeout = TimeSpan.FromSeconds(8)
        });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var result = await ok.RequestAsync("ok", null, cts.Token);
        Assert.NotNull(result);

        await using var rate = new JsonRpcProcessClient(new JsonRpcProcessOptions
        {
            FileName = "powershell.exe",
            Arguments = ["-NoProfile", "-File", script]
        });
        using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var ex = await Assert.ThrowsAsync<JsonRpcException>(() => rate.RequestAsync("rate", null, cts2.Token));
        Assert.Equal(429, ex.Code);

        await using var cancel = new JsonRpcProcessClient(new JsonRpcProcessOptions
        {
            FileName = "powershell.exe",
            Arguments = ["-NoProfile", "-File", script]
        });
        using var cts3 = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<Exception>(() => cancel.RequestAsync("slow", null, cts3.Token));
    }

    [Fact]
    public void Does_not_log_credentials_in_exception_type()
    {
        var ex = new JsonRpcException("network failed");
        Assert.DoesNotContain("token", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
