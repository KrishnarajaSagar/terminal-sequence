# Playing sequence across networks

The server owns the authoritative `GameState`; clients send actions and receive only the
`PlayerView` the server allows. This guide covers playing against a server on another
machine/network. The terminal client must run on each player's machine.

There are two ways to get the game onto another machine. **Copying the published exe is
fastest**; the one-click script below is for when each player would rather build from the
pulled source.

## One-click build (after pulling the source)

`source\publish-game.cmd` builds the terminal client and writes it to `source\publish\`
next to itself. Double-click it on the machine that will host. Add a trailing argument on
the command line if you need the no-runtime bundle instead:

```
publish-game.cmd selfcontained     :: ~60 MB single exe, no .NET install needed on players
publish-game.cmd                   :: small exe, players install .NET 8 Runtime
```

Then copy `source\publish\Sequence.Terminal.exe` to each player's machine (just that one
file; skip the `.pdb`). If the same machine also hosts the server, run the exe there too.

## Build and copy the client (by hand, equivalent to the script)

Framework-dependent (small; players need the .NET 8 Runtime):

```
dotnet publish source/src/Sequence.Terminal/Sequence.Terminal.csproj -c Release -o publish
```

Self-contained single file (no runtime on the players; ~60 MB):

```
dotnet publish source/src/Sequence.Terminal/Sequence.Terminal.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Copy only `publish\Sequence.Terminal.exe` to each player's machine (ignore the `.pdb`
files).

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

## Who starts the game

The game does **not** start automatically. Every seat runs the game with an interactive
menu; the workflow (same for LAN and the public internet) is:

1. The host machine runs the server (see below). The host window shows its bind address,
   port, and the two seats.
2. Each player runs the client and picks **Join**, entering the host's IP, the port, and
   their own ID.
3. As each seat connects, the lobby on every machine shows the connected seats. The host
   window changes to "Both players connected. Press Enter to start the game."
4. The host presses **Enter**; both clients leave the lobby and the board appears on all
   three screens at once.

While waiting, **Esc** returns that machine's client to the main menu; the host can **Esc**
out of the lobby to stop the server and return to its menu too.

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