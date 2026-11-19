# Vertcoin Core and your node

Bitch Blast can be a control panel for a real Vertcoin Core process. Core validates the blockchain and manages its own wallet files; VerthashMiner still performs the GPU work. Pool mining works independently of the node. Running a node supports your own validation and the network, but does not by itself earn VTC or change a pool's payout terms.

## Get Core

The official project's latest stable release checked on **2026-11-18** was **v23.2**, published on **2023-10-02**. Use the [official release page](https://github.com/vertcoin-project/vertcoin-core/releases/tag/v23.2). The portable Windows x64 archive includes `vertcoind.exe`, `vertcoin-cli.exe`, and `vertcoin-qt.exe`.

| Verified download | Value |
|---|---|
| File | `vertcoin-23.2-win64.zip` |
| Size | 32,237,596 bytes |
| SHA-256 | `fc710f8603a0c8366e08b78ee1dd569ac5fff3ea485317f9172ffefe140ab5e2` |

The downloaded file was checked against the release's [SHA256SUMS](https://github.com/vertcoin-project/vertcoin-core/releases/download/v23.2/SHA256SUMS). An independent GPG verification of the release signatures was not performed.

In Bitch Blast's Core panel, choose **1 · Prepare Core** to download that official package and verify its pinned SHA-256 before extraction. You can also select an existing `vertcoind.exe` or `vertcoin-qt.exe`. Then choose **2 · Start node** when you are ready to begin synchronization. Preparation alone does not start Core; launching the miner does not automatically start a node.

**Connect / refresh** attaches to an already-running local Core instance using its data folder, RPC port and cookie. Such an instance remains under your control: Bitch Blast does not take ownership of it or stop it. By default, a node started by Bitch Blast is stopped gracefully when the app closes. You can turn that preference off to keep it running. A node left running is treated as an existing instance on the next app launch; use Core itself to stop that instance. If graceful shutdown takes too long, the app leaves Core to finish rather than force-killing its database process.

## Storage, syncing and wallets

The default managed data folder is `%LOCALAPPDATA%\VertcoinBitchBlast\Core`. This keeps your existing Core configuration and wallet separate. A different data folder is a different wallet environment: it does not automatically contain the coins or keys from another installation.

Pruning reduces the space used by old block files after validation. A **2,048 MiB** pruning target is roughly 2 GiB of block data, plus the chain-state database, indexes, wallet files, logs and working space. It is not a limit on the whole folder or the initial download. A pruned node still downloads and validates the chain; an archival node retains all of its block data. Switching an already-pruned node back to archival operation requires downloading the discarded history again. See [Core's pruning option](https://github.com/vertcoin-project/vertcoin-core/blob/v23.2/src/init.cpp#L427).

Core v23.2 also requires a **1,283,457,024-byte Verthash data file** in its own network data folder, even for an isolated regtest node. This is additional disk space outside the pruning target. On **Start node**, Bitch Blast can copy an existing miner data file after verifying its hash; otherwise Core generates the file itself. First startup can therefore spend several minutes generating and verifying data before the node answers normally. Core still verifies a copied file. The app launches Core in disk-only Verthash mode to avoid loading that whole file into RAM, with a 256 MiB database-cache setting. This saves memory for other work but can slow validation; 256 MiB is a cache setting, not a cap on all Core memory. These startup behaviors are defined in [Core's initialization](https://github.com/vertcoin-project/vertcoin-core/blob/v23.2/src/init.cpp#L1400).

Initial synchronization uses bandwidth, CPU and disk activity and can take substantial time. Keep the computer awake and let Core catch up before treating its balance as current. The sync percentage is an estimate; blocks, headers, peer counts and the initial-sync state provide context. Give the data folder room to grow, including when pruning is enabled.

You can select a loaded wallet, refresh its balances, and request a **New receiving address**. **Create encrypted wallet** creates a wallet in Core with your chosen name and a matching passphrase entered twice; the app requires at least 12 characters. Keep that passphrase safe: Bitch Blast does not save it in its settings or recover it for you. Back up the wallet before directing payouts to it. A receiving address is public; a backup or recovery secret is not.

The miner's payout destination changes only when you choose **Use this address for mining**. Creating a wallet or address does not silently redirect mining. Changing destinations does not move an existing balance held by a pool under an older address.

Choose **Open Core wallet window** for the official Core interface, including backups, recovery, encryption changes and sending coins. For a node managed by Bitch Blast, this button stops the daemon gracefully and opens the official graphical wallet with the same data folder. It does not run two Core processes against one folder. For an existing instance that Bitch Blast merely connected to, use that instance's own wallet window; the app will not stop it to replace it.

## Internet access: start without router changes

A node can make outbound connections, download the chain and validate it without forwarding a router port. Inbound connections are optional and let other nodes initiate connections to yours. If your node has outbound peers and sync is progressing, a zero inbound count does not mean the node is broken. It also does not by itself establish whether a port is reachable.

Two ports have very different jobs:

| Mainnet port | Purpose | Router rule |
|---|---|---|
| **TCP 5889** | Core peer-to-peer traffic | Optional inbound forwarding to the node PC |
| **TCP 5888** | Local RPC control, including wallet operations | **Never forward; keep on localhost** |

These defaults come from the tagged [P2P parameters](https://github.com/vertcoin-project/vertcoin-core/blob/v23.2/src/chainparams.cpp#L100) and [RPC parameters](https://github.com/vertcoin-project/vertcoin-core/blob/v23.2/src/chainparamsbase.cpp#L40). The app uses local cookie authentication for RPC. Treat `.cookie` as a credential and do not share it. Core's [RPC documentation](https://github.com/vertcoin-project/vertcoin-core/blob/v23.2/doc/JSON-RPC-interface.md#security) explains why its control interface belongs on a trusted local connection.

## Optional inbound IPv4 setup

Do these steps only on a router you administer. Bitch Blast does not change your router or firewall automatically.

1. Enable incoming peer connections in the app's node settings, then start Core. Leave its default mainnet P2P port at **5889**.
2. Find the active Ethernet or Wi-Fi adapter's IPv4 address and default gateway in Windows network details, or run `ipconfig`. Ignore disconnected adapters and VPN/virtual adapters. The gateway is normally the router's local address.
3. Open your router's administration page using its documented address. In its LAN/DHCP section, reserve the node PC's current LAN address so the rule will keep pointing to the same machine.
4. Find **Port forwarding**, **NAT forwarding** or **Virtual server**. Add one rule: name `Vertcoin node`, protocol **TCP**, external port **5889**, internal address **the reserved address of this PC**, internal port **5889**. Save it. UDP, a DMZ, disabling the firewall and enabling UPnP are not needed for this rule. Menu names vary; [TP-Link's illustrated guide](https://www.tp-link.com/us/support/faq/1379/) is an example, not a claim about your router model.
5. If Windows Firewall blocks the listener, open **Windows Security → Firewall & network protection → Advanced settings → Inbound Rules → New Rule → Custom**. Limit the rule to the actual `vertcoind.exe` path and **TCP local port 5889**, allow the connection, and apply it only to the network profile used by your trusted home connection. Keep other firewall protections enabled. [Microsoft's rule guide](https://learn.microsoft.com/en-us/windows/security/operating-system-security/network-security/windows-firewall/configure) explains combining program and port restrictions.
6. Keep Core running and inspect incoming peers over time. A successful connection from outside your LAN, while Core is listening, provides stronger reachability evidence than the inbound count alone. Testing your public address from the same Wi-Fi can fail on routers without NAT loopback even when outside connections work. Disable the forwarding and firewall rule when you no longer want to accept inbound peers.

## If forwarding does not work

Your router needs a publicly reachable WAN IPv4 address. A private WAN address or carrier-grade NAT (often **100.64.0.0–100.127.255.255**) can prevent a forwarding rule on your own router from being reachable. With two home routers, both NAT layers must be handled; with ISP-managed CGNAT, ask the ISP about a public address or use outbound-only operation. Do not assume a paid static address is necessary: a reachable dynamic public address can work. See the [router manufacturer's troubleshooting guide](https://www.tp-link.com/us/support/faq/785/).

With native public IPv6, the router normally needs an IPv6 firewall allowance for the node's IPv6 address and TCP 5889, rather than an IPv4 NAT forwarding rule. Windows Firewall must allow it too. Router support varies; use the manufacturer's IPv6 instructions, keep RPC local, and do not disable the IPv6 firewall wholesale. An [example router guide](https://www.tp-link.com/us/document/111858/) describes IPv6 firewall rules.

If the PC sleeps, Core is stopped, the LAN address changes, or a VPN takes over routing, incoming connections may stop. The router instructions are guidance, not an automatic external-port test.
