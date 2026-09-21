# Playing Sequence Across the Public Internet

This stage makes the authoritative server reachable from a client on a completely
different network. No authentication, accounts, databases or encryption are added - it is
a development/test setup, and the existing authoritative architecture is unchanged:

* the **server** owns the `GameState`; clients only send actions and receive the
  player-specific `PlayerView` the server allows them to see;
* the **terminal client** keeps its existing UI and flow.

## What changed in the code

* `Sequence.Server/GameHost.cs` - the TCP listener now takes a `bindAddress`
  (`IPAddress`). The default is `IPAddress.Any` (all interfaces), so remote clients can
  connect at all. Loopback-only mode is still one flag away.
* `Sequence.Terminal/Program.cs` - new `--bind` and `--timeout` flags; `--port` is
  validated (1-65535); server startup prints the bound interface and the PC's LAN IPs.
* `Sequence.Terminal/NetworkGameRunner.cs` - the client connects with `ConnectAsync` under
  a configurable timeout instead of a blocking, unbounded `Connect()`.
* `Sequence.Terminal/Args.cs`, `ConnectFailure.cs` - pure parsing/validation and
  connection-error wording, unit-tested.
* `Sequence.Terminal/Sequence.Terminal.csproj`, `tests/Sequence.Tests/*` - new tests for
  configuration handling and connection failure.
* No changes to `Sequence.Domain`, `Sequence.Engine`, `Sequence.Contracts`, or the
  authoritative action validation in `Sequence.Server/GameServer.cs`.

## 1. Configure the server IP and port

Start the server on the PC that will host the game:

```
Sequence.Terminal.exe --server --bind 0.0.0.0 --port 5000 --seed 7
```

* `--bind 0.0.0.0` (the default) - listen on **all** interfaces so non-local clients can
  connect. Use `--bind 127.0.0.1` if you intentionally want same-PC-play only.
* `--port 5000` - the TCP listening port (default 5000, configure 1-65535).
* `--seed 7` - optional deck seed for reproducible games.

A dedicated public IP is **not** required and one is **not** hardcoded anywhere.

## 2. Find the server PC's local IP

On the server PC (PowerShell or `cmd`):

```
ipconfig
```

Look for the IPv4 address of the Wi-Fi/Ethernet adapter, e.g. `192.168.1.50`. The server
startup banner already prints the discovered addresses, e.g.
`Local address for same-network clients: 192.168.1.50:5000`. A client on the **same** Wi-Fi
can already connect with `--host 192.168.1.50`. For a client on a **different** network,
you must also do steps 3 and 4.

## 3. Router port forwarding (TCP)

On the router's admin page (the exact menu differs by model - search for "port
forwarding", "virtual server", or "NAT") create one rule:

| Field        | Value                        |
|--------------|------------------------------|
| Protocol     | TCP                          |
| External port| 5000                         |
| Internal host| 192.168.1.50 (the server PC's LAN IP from step 2) |
| Internal port| 5000                         |

No router model or IP is assumed here; use your own values. TCP only - do not open UDP.

Notes:
* If your public IP changes, either pick a static/DHCP-reserved LAN IP for the server PC
  (reserve the LAN IP in the router) and/or use a dynamic-DNS host name for the client's
  `--host`.
* If your ISP uses CGNAT (you do not own a public IPv4), port forwarding from the internet
  will not work - see section 6 for how to verify.

## 4. Windows Firewall rule

On the server PC, allow inbound TCP 5000. From an **administrator** PowerShell:

```
New-NetFirewallRule -DisplayName "Sequence 5000" -Direction Inbound -Protocol TCP -LocalPort 5000 -Action Allow
```

Remove the rule later with `Remove-NetFirewallRule -DisplayName "Sequence 5000"`.

## 5. Remote client (public IP and port)

On the remote laptop, start the client pointing at the server's **public** IP:

```
Sequence.Terminal.exe --client --host <SERVER-PUBLIC-IP> --port 5000 --player your-id
```

* `--host` accepts an IP (IPv4/IPv6) or a host name; do not include a scheme or port here
  (port is a separate flag).
* `--player your-id` is your identity for seat binding/reconnect; your opponent must use a
  different one.
* `--timeout <seconds>` (default 10) controls how long the connect attempt may take before
  the client reports a timeout.

To find the server PC's current public IP, run this **on the server PC**:

```
curl ifconfig.me
```

Test port 5000 is reachable from the remote laptop with (PowerShell `Test-NetConnection`):

```
Test-NetConnection <SERVER-PUBLIC-IP> -Port 5000
```

`TcpTestSucceeded : True` means the whole chain (public IP + router forward + firewall)
works before you even start the game.

## 6. Test with two genuinely different networks (recommended)

The simplest real test:

1. Server PC on **home Wi-Fi** - start the server (`--server`), note its public IP.
2. Remote laptop on a **phone hotspot** (different network) - connect with `--client
   --host <SERVER-PUBLIC-IP> --port 5000 --player alice`.
3. Second client, also on the phone hotspot with a different `--player` id.
4. Play to completion.

If `TcpTestSucceeded` is `False` from the hotspot:
* verify the firewall rule (step 4) and the forward (step 3) once more;
* confirm the public IP really is yours and public - a common blocker is **CGNAT**, where
  the "public" IP from `ifconfig.me` is shared. Workaround: test with the same-PC LAN IP
  (step 2) over the home Wi-Fi; for cross-network play in that situation, use a VPN or
  tunnel instead of port forwarding.

## Troubleshooting quick reference

| Symptom on client | Meaning | Fix |
|-------------------|---------|-----|
| `Connection to ... was refused.` | Port closed / nothing listening | Start `--server`; check step 3 and 4 |
| `Could not connect ... within Ns (timeout).` | Packets dropped (firewall/CGNAT) | Check `0.0.0.0` bind, rule, forward |
| `The server host '...' could not be resolved.` | Bad/typo host | Use the correct public IP or a valid host name |
| Client on the same PC works, remote does not | Forward/firewall not open | Fix steps 3 and 4 |