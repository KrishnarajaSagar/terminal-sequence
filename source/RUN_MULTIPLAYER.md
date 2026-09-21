# Playing sequence across networks

The server owns the authoritative `GameState`; clients send actions and receive only the
`PlayerView` the server allows. This guide covers playing against a server on another
machine/network. The terminal client must run on each player's machine; nothing needs to be
**installed** beyond the files described below (no SDK required).

## Build and copy the client

`dotnet publish` produces a single self-contained exe that runs on any 64-bit Windows
without a .NET install:

```
dotnet publish source/src/Sequence.Terminal/Sequence.Terminal.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Copy only `publish\Sequence.Terminal.exe` to each player's machine (ignore the `.pdb`
files). For a much smaller file you can instead copy
`source\src\Sequence.Terminal\bin\Release\net8.0\Sequence.Terminal.exe` and install the
.NET 8 Runtime there. Copy the server side too if the same machine will host.

## Server flags

```
Sequence.Terminal.exe --server --bind 0.0.0.0 --port 5000 --seed 7
```

* `--bind 0.0.0.0` (default) - listen on all interfaces so non-local clients can connect.
  Use `--bind 127.0.0.1` for same-PC play only.
* `--port 5000` - TCP port (1-65535, default 5000).
* `--seed 7` - optional deck seed for reproducible games.

## Client flags

```
Sequence.Terminal.exe --client --host <server-ip> --port 5000 --player your-id
```

* `--host` - IP (IPv4/IPv6) or host name; no scheme or port.
* `--player` - your identity (seat binding/reconnect); each player must use a different id.
* `--timeout <seconds>` - connect timeout (default 10) before a "timeout" report.

## Same LAN

1. Server PC: run the server (above).
2. Find its LAN IP: `ipconfig`, look for the active adapter's IPv4 (e.g. `192.168.1.50`);
   the server banner also prints discovered addresses.
3. Each client: `--client --host 192.168.1.50 --port 5000 --player your-id`.

## Different networks (public internet)

1. Server PC: run the server with `--bind 0.0.0.0`.
2. Router (admin page, usually `192.168.1.1`): add a TCP port-forward rule
   `external 5000 -> <server LAN IP>:5000`. TCP only.
3. Server PC (admin PowerShell):
   ```
   New-NetFirewallRule -DisplayName "Sequence 5000" -Direction Inbound -Protocol TCP -LocalPort 5000 -Action Allow
   ```
   Remove later with `Remove-NetFirewallRule -DisplayName "Sequence 5000"`.
4. Find the public IP on the server PC: `curl ifconfig.me`.
5. From the remote network, verify the path before starting the game:
   ```
   Test-NetConnection <public-ip> -Port 5000
   ```
   Want `TcpTestSucceeded : True`.
6. Clients from the remote network: `--client --host <public-ip> --port 5000 --player your-id`.

### CGNAT fallback

If `Test-NetConnection` times out from another network but LAN play works, your ISP likely
uses CGNAT (the `ifconfig.me` IP is shared, so port forwarding cannot reach you). Skip
steps 2-4 and use a private tunnel instead: install Tailscale (or similar) on both
machines, then point clients at the server's tunnel IP with `--host <tunnel-ip>`.

## Troubleshooting

| Symptom on client | Meaning | Fix |
|-------------------|---------|-----|
| `Connection to ... was refused.` | Nothing listening / port closed | Start `--server`; check forward + firewall |
| `Could not connect ... within Ns (timeout).` | Packets dropped (firewall/CGNAT) | Verify `0.0.0.0` bind, rule, forward |
| `The server host '...' could not be resolved.` | Bad host name | Use the correct public IP / valid host name |
| Same-PC works, remote does not | Forward/firewall not open | Fix router forward + firewall rule |