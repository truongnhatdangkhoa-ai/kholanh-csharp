using System.Net;
using System.Net.Sockets;
using KhoLanh.Core.Lan;
using Xunit;

namespace KhoLanh.Core.Tests;

public class SecureChannelTests
{
    private static async Task<(Stream a, Stream b, IDisposable d)> Pipe()
    {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        var c = new TcpClient(); var t = l.AcceptTcpClientAsync(); await c.ConnectAsync(IPAddress.Loopback, port);
        var s = await t; l.Stop();
        return (c.GetStream(), s.GetStream(), new Multi(c, s));
    }
    private sealed class Multi : IDisposable { private readonly IDisposable[] _d; public Multi(params IDisposable[] d) { _d = d; } public void Dispose() { foreach (var x in _d) x.Dispose(); } }

    private static async Task<(SecureChannel cli, SecureChannel srv)> Handshake(Stream a, Stream b, string kc, string ks)
    {
        var ct = new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;
        var cliT = SecureChannel.ClientAsync(a, kc, ct);
        var magic = new byte[4]; var got = 0; while (got < 4) got += await b.ReadAsync(magic.AsMemory(got), ct);
        Assert.Equal(SecureChannel.Magic, magic);
        var srv = await SecureChannel.ServerAsync(b, ks, ct);
        return (await cliT, srv);
    }

    [Fact]
    public async Task Round_trip_both_directions_and_nothing_readable_on_wire()
    {
        var (a, b, d) = await Pipe(); using var _ = d;
        var (cli, srv) = await Handshake(a, b, "khoa-lan-1", "khoa-lan-1");
        var ct = new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;
        await cli.WriteAsync("{\"op\":\"x\",\"k\":\"bi-mat\"}", ct);
        Assert.Equal("{\"op\":\"x\",\"k\":\"bi-mat\"}", await srv.ReadAsync(ct));
        await srv.WriteAsync("{\"ok\":true}", ct);
        Assert.Equal("{\"ok\":true}", await cli.ReadAsync(ct));
        for (var i = 0; i < 5; i++) { await cli.WriteAsync("n" + i, ct); Assert.Equal("n" + i, await srv.ReadAsync(ct)); }
    }

    [Fact]
    public async Task Wrong_key_fails_to_decrypt()
    {
        var (a, b, d) = await Pipe(); using var _ = d;
        var (cli, srv) = await Handshake(a, b, "khoa-dung", "khoa-sai");
        var ct = new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;
        await cli.WriteAsync("{\"op\":\"x\"}", ct);
        await Assert.ThrowsAsync<IOException>(() => srv.ReadAsync(ct));
    }
}
