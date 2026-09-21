using System.Net;
using Sequence.Terminal;

namespace Sequence.Tests;

/// <summary>
/// Locks down the command-line configuration rules used by both the server (bind address,
/// port) and the client (host shape, port, timeout). Pure parsing - no sockets touched.
/// </summary>
public class ConfigValidationTests
{
    [Theory]
    [InlineData("--port", "5000", 5000)]
    [InlineData("--port", "1", 1)]
    [InlineData("--port", "65535", 65535)]
    [InlineData("--port", "7", 7)]
    public void ReadPort_Returns_The_First_Valid_Value(string flag, string value, int expected) =>
        Assert.Equal(expected, Args.ReadPort(new[] { flag, value }, 5000));

    [Fact]
    public void ReadPort_Falls_Back_To_The_Default_When_The_Flag_Is_Absent() =>
        Assert.Equal(5000, Args.ReadPort(Array.Empty<string>(), 5000));

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("")]
    public void ReadPort_Rejects_Out_Of_Range_Or_Non_Numeric_Values(string value)
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() => Args.ReadPort(new[] { "--port", value }, 5000));
        Assert.Contains("Invalid port", ex.Message);
    }

    [Fact]
    public void ReadBindAddress_Defaults_To_Any() =>
        Assert.Equal(IPAddress.Any, Args.ReadBindAddress(Array.Empty<string>()));

    [Theory]
    [InlineData("any")]
    [InlineData("0.0.0.0")]
    [InlineData("*")]
    public void ReadBindAddress_Accepts_All_Interfaces_Aliases(string value) =>
        Assert.Equal(IPAddress.Any, Args.ReadBindAddress(new[] { "--bind", value }));

    [Theory]
    [InlineData("loopback")]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    public void ReadBindAddress_Accepts_Loopback_Aliases(string value) =>
        Assert.Equal(IPAddress.Loopback, Args.ReadBindAddress(new[] { "--bind", value }));

    [Theory]
    [InlineData("::", "::")]
    [InlineData("::1", "::1")]
    public void ReadBindAddress_Accepts_Ipv6_Keywords(string value, string expected) =>
        Assert.Equal(IPAddress.Parse(expected), Args.ReadBindAddress(new[] { "--bind", value }));

    [Fact]
    public void ReadBindAddress_Accepts_A_Specific_Adapter_Ip() =>
        Assert.Equal(IPAddress.Parse("192.168.1.50"), Args.ReadBindAddress(new[] { "--bind", "192.168.1.50" }));

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("999.999.999.999")]
    [InlineData("2601:zzzz::")]
    public void ReadBindAddress_Rejects_Unknown_Values(string value) =>
        Assert.Throws<ArgumentException>(() => Args.ReadBindAddress(new[] { "--bind", value }));

    [Theory]
    [InlineData("192.168.1.5")]
    [InlineData("55.66.77.88")]
    [InlineData("::1")]
    [InlineData("play.example.com")]
    public void IsPlausibleHost_Accepts_Addresses_And_Hostnames(string host) =>
        Assert.True(Args.IsPlausibleHost(host));

    [Theory]
    [InlineData("")]
    [InlineData("http://10.0.0.1")]
    [InlineData("server:8000")]
    [InlineData("two words")]
    [InlineData("host/path")]
    public void IsPlausibleHost_Rejects_Schemes_Ports_And_Whitespace(string host) =>
        Assert.False(Args.IsPlausibleHost(host));

    [Fact]
    public void ArgInt_And_ArgString_Are_Case_Insensitive_And_First_Occurrence_Wins()
    {
        Assert.Equal(42, Args.ArgInt(new[] { "--PORT", "42", "--port", "7" }, "--port", 1));
        Assert.Equal("alice", Args.ArgString(new[] { "--Host", "alice", "--host", "bob" }, "--host"));
    }

    [Fact]
    public void ArgString_Returns_Null_For_Empty_Or_Absent_Values()
    {
        Assert.Null(Args.ArgString(new[] { "--host", "" }, "--host"));
        Assert.Null(Args.ArgString(new[] { "--server" }, "--host"));
        Assert.Equal("  ", Args.ArgString(new[] { "--host", "  " }, "--host"));
    }
}