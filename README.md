# Vertcoin Bitch Blast! Edition

**Big walnut energy.** A free Windows x64 front end for VerthashMiner, with automatic GPU sharing for local AI, the Emi patch, a flying walnut, and a fully offline jukebox. Independent community preview **v0.4.0**; not an official Vertcoin-project release.

[**Download for Windows x64**](https://github.com/jonacitrusthyng/vertcoin-bitch-blast/releases/download/v0.4.0/Vertcoin-Bitch-Blast-v0.4.0-Windows-x64.zip) · [Release, full source and SHA-256 checksums](https://github.com/jonacitrusthyng/vertcoin-bitch-blast/releases/tag/v0.4.0)

The download is unsigned. Live pool share acceptance and completed payouts still need field testing. [46 automated checks passed](TEST-RESULTS.md); that is not a claim of completed pool payouts.

![Miner dashboard](docs/Miner-Preview.png)

## Emi brought the playlist

- All **21 original Emi recordings**, plus **No Permission Slip**: 22 songs, about 55 minutes, bundled locally.
- Matching cover art, lyrics, play/pause/stop, previous/next, seeking, shuffle and playlist repeat.
- **15 spoken Emi reactions** and **five sound effects**, including the walnut blast and accepted-share chime.
- Separate **Mute music**, **Mute SFX** and **Mute Emi** controls, plus **Mute all** and three volume sliders. Preferences survive restart. Music lowers while Emi speaks.
- Music starts when you press Play. Commentary can greet you on launch unless muted. No account, streaming, microphone, or cloud speech service is used by the app.

![Emi jukebox](docs/Jukebox-Preview.png)

## Start here

1. Extract the Windows ZIP and open **VertcoinBitchBlast.exe**. The .NET runtime is included; no administrator privileges are needed.
2. Choose **Nut fund**, paste your public **Vertcoin mainnet receiving address**, and save. The app checks the address checksum. If you need a wallet, use **I need a wallet** to visit Vertcoin's resources. Keep your recovery phrase and private keys in your wallet.
3. In **Settings**, click **Prepare miner + data**. This downloads VerthashMiner 0.7.2 from its official GitHub release, checks a pinned SHA-256, and generates the canonical 1,283,457,024-byte Verthash file locally. Allow several minutes and at least 1.4 GB of free disk space. Setup can be cancelled. You can instead browse to an existing miner and data file.
4. **Scan GPUs**, choose the GPU to mine with, and save settings. **Test GPU offline** checks hashing for up to 35 seconds without a wallet or pool connection. The default intensity is 75% and the temperature limit is 75°C. Monitoring support depends on the device and driver. This release selects one GPU per mining session.
5. On **Blast deck**, select **WoolyPooly**, **Zpool**, **Suprnova**, or **Custom pool**. **Check pool** tests the Stratum subscription. Click **Blast off · start mining**. In Auto mode, the app waits for 30 seconds of continuous AI idle time first.
6. **Stop mining** releases the worker. Closing the app also stops its worker, including if the app crashes. The app never enables mining automatically on launch.

## Upgrading from an earlier community preview

Close the previous app, extract the new ZIP into its own folder, and launch **VertcoinBitchBlast.exe**. The existing `%LOCALAPPDATA%\VertcoinGarden` data directory and single-instance lock are retained for compatibility, so saved pool/GPU settings and prepared miner data remain usable. A saved worker name is preserved; new configurations default to `ocm`.

## Your payout destination

The miner accepts an address from an existing wallet; it does not generate or store wallet keys. Rewards are sent by the selected pool according to its fee, payment schedule, and payout threshold. **View payouts** opens the pool's website; WoolyPooly lets you paste your address into its dashboard search. **Open my wallet** uses the registered `vertcoin:` handler when available. Sending coins and displaying a wallet balance belong to that wallet.

The interface deliberately has no sample balances or invented income estimates. Hashrate and accepted/rejected share counts come from the actual miner log. Share counters restart when the worker restarts, including after an AI pause. Changing the payout address does not transfer balances that remain at a pool under an old address. Check the receiving address before starting.

## Automatic AI / GPU sharing

- **Auto on:** watches active and queued inference in LM Studio, llmster, and Bionic through the installed `lms` CLI. An idle loaded model may remain loaded. Remote LM Link inference is excluded. Busy AI stops the entire owned mining worker, releasing its GPU allocations; continuous idle time starts a fresh worker.
- **Unknown status:** holds mining paused if a running supported host cannot report its state. The app does not launch AI applications to check their status.
- **Other AI tools:** configurable process names reserve the GPU while any matching process runs. Defaults include Ollama, llama-server, and ComfyUI. Add `python` if that is how your local AI application appears in Task Manager. This conservative fallback may hold mining paused for an idle server process.
- **Auto off:** manual mining ignores AI state. Another miner still prevents the app from starting a competing worker.

Checks occur roughly once per second, and status calls can take up to five seconds. Very short requests and initial model loading may happen between checks. Stop mining before loading a large model if available VRAM is tight. There is no universal detector for every AI application, GPU task, game, or browser. The app observes inference/process state; it does not read prompts or responses.

## Pools

The curated pool list was checked on 2026-11-17. Pool availability, fees, and terms can change. There is no automatic switch to a different pool; selection stays under your control. Custom pools use `stratum+tcp://host:port`, your receiving address as username, and the password/options entered in Settings. Account-name pools are outside this address-based setup.

| Pool | Endpoint | Password/options | Source |
|---|---|---|---|
| WoolyPooly PPLNS | `pool.woolypooly.com:3102` | `x` | [Pool instructions](https://woolypooly.com/en/coin/vtc) |
| Zpool | `verthash.mine.zpool.ca:6144` | `c=VTC,zap=VTC` | [Official OCM catalog](https://github.com/vertcoin-project/one-click-miner-vnext/blob/master/pools.json), [pool terms](https://zpool.ca/) |
| Suprnova anonymous VTC | `vtc.suprnova.cc:1776` | `x` | [Official OCM catalog](https://github.com/vertcoin-project/one-click-miner-vnext/blob/master/pools.json) |

A successful connection check means the server answered `mining.subscribe`. It does not prove that it will authorize a payout address, accept a share, or make a payment. The miner performs authorization and reports failures through The receipts. Zpool's non-guaranteed VTC payout availability follows its own terms; read them before use.

## Local files and privacy

Configuration, the downloaded worker, and generated data are stored in `%LOCALAPPDATA%\VertcoinGarden`. The original Vertcoin OCM wallet and configuration are not modified. The app stores a public receiving address and pool options, never a recovery phrase or wallet private key. VerthashMiner writes timestamped logs alongside its executable. Those logs may contain your payout address and pool connection details; review them before sharing. There is no telemetry, automatic startup, or background service.

The source contains all pool settings and miner launch arguments. It invokes the miner directly, without a command shell. Windows assigns each mining process to a job at creation time so it cannot be orphaned if the app exits. The UI is local WPF and does not load executable web content.

## Build and verify

On Windows, with a .NET SDK that supports targeting .NET 8. The full Source ZIP includes every media file. A Git clone contains the code, artwork, voice and effects; run `fetch-music.ps1` once to install the checksum-verified music/cover pack from this release before building:

```powershell
# Git clones only; unnecessary for the full Source ZIP:
.\fetch-music.ps1
.\build.ps1
.\build\tests\OcmTests.exe
# Optional read-only checks against the live pool endpoints:
.\build\tests\OcmTests.exe --pools
# Read local AI status without starting a miner:
.\build\tests\OcmTests.exe --ai
```

See **TEST-RESULTS.md** for the verification record and remaining limits. The source and Windows build are supplied together for inspection. The executable is unsigned. No antivirus exclusions or security-setting changes are performed by the app.

## Upstream resources

- [VerthashMiner source and usage](https://github.com/CryptoGraphics/VerthashMiner)
- [Pinned official VerthashMiner 0.7.2 Windows release](https://github.com/CryptoGraphics/VerthashMiner/releases/tag/0.7.2)
- [Vertcoin Core address prefixes](https://github.com/vertcoin-project/vertcoin-core/blob/master/src/chainparams.cpp)
- [LM Studio CLI inference-status fields](https://github.com/lmstudio-ai/lms/blob/main/src/subcommands/list.ts)

The application source is MIT licensed. VerthashMiner is a separate GPL-3.0 program, downloaded from upstream during setup; its license and source remain available from that project. See [media credits](docs/MEDIA-CREDITS.md) and [artwork notes](docs/ARTWORK.md) for the supplied and generated creative assets. This fan edition does not change the Vertcoin network's official branding.
