# v0.5.0 verification record

Tested on Windows x64 on 2026-11-18. Product: **Vertcoin Bitch Blast! Edition**.

## Automated checks

- Self-contained .NET 8 WPF release build succeeded. Build host uses .NET SDK 11.0.100-rc.3 to target .NET 8.
- **73 automated checks passed.** Existing coverage includes payout-address and endpoint validation, AI pause/resume and unknown-state behavior, worker lifecycle/crash cleanup, real socket subscription parsing, fixture-worker payout routing/logs, media path containment, unavailable-track handling, repeat/shuffle selection, independent mutes, ducking, volume bounds and preference persistence. [Full log](tests/verification-log.txt).
- New Core coverage includes loopback-only RPC and cookie authentication, rejected HTTP redirects, isolated launch settings, port/pruning bounds, wallet-name and cookie-path containment, real RPC field parsing, decimal balances, receiving addresses, encrypted-wallet creation and minimum-passphrase validation.
- Ownership checks prevent the app from stopping or creating a wallet in an existing node that it did not launch. Receiving addresses are bound to the selected wallet, data folder, RPC port and network; changing any of those invalidates the previously displayed address. The integration has no send, unlock or private-key export operation.

## Real Vertcoin Core smoke test

The official **Vertcoin Core v23.2 Windows x64 ZIP** was downloaded and its SHA-256 matched the published release checksum: `fc710f8603a0c8366e08b78ee1dd569ac5fff3ea485317f9172ffefe140ab5e2`. Release-signature GPG verification was not performed. The actual official daemon passed **six smoke checks** in an isolated temporary **regtest** data folder, with **zero peers and zero funds**:

1. Starts with isolated settings and answers authenticated local RPC.
2. Creates a locked, encrypted descriptor wallet with a zero balance.
3. Produces a receiving address while the wallet stays locked, without spending.
4. Allows a second manager to read status but prevents it from stopping the unowned node.
5. Flushes and exits through authenticated RPC `stop`.
6. Reloads the same encrypted wallet, still locked, after a managed restart.

See the [Core smoke log](tests/core-smoke-log.txt). Core requires its own 1,283,457,024-byte Verthash data file even in regtest; that disk space is separate from the block-pruning target. These checks did not synchronize mainnet, fund a wallet, mine, change router settings or test public inbound reachability.

## Current desktop setup check

The actual **Prepare Core** interface downloaded, verified and installed the official pinned archive successfully. Preparation did not start a mainnet node. Starting synchronization remains a separate explicit action.

The rebuilt gallery was checked in the actual WPF app: a separate resizable window, F11 fullscreen, Escape restoration, HUD show/hide, automatic slideshow advancement, shared music play/pause and master mute/unmute. At the minimum window width, the control buttons wrap onto a second row without overlapping the HUD. Mining remained idle, so the screenshots show no invented hashrate or shares. The in-app router guide was expanded and checked for readable layout. No router/firewall changes were made.

## Audio and earlier desktop baseline

The unchanged media collection contains 22 MP3 songs, 15 spoken WAV reactions and five WAV effects. All **42 audio files fully decoded** through Windows Media Foundation during v0.4.0 verification. Durations, hashes and file sizes are recorded in [Audio-Verification.json](docs/Audio-Verification.json). This verifies media integrity, not a full subjective listening review.

The v0.4.0 desktop baseline verified the Emi patch, walnut/trailer artwork, 22-track catalog, matching artwork and existing mining controls. Actual player checks covered advancing playback time, pause/resume, seeking, next-track switching, No Permission Slip playback, master mute/unmute, persisted mute settings and clean shutdown; voice/effect test controls were exercised. The new gallery uses the same audio engine and preferences. Music starts only after a Play action.

## Earlier hardware baseline

The earlier mining implementation was tested on 2026-11-17 with the pinned official VerthashMiner 0.7.2 and its canonical 1,283,457,024-byte data file. Brief offline samples were 339.94 kH/s on an RTX 3050 Ti Laptop GPU and 167.27 kH/s on an Intel Arc A770. These are earlier short samples, not fresh v0.5.0 benchmarks, sustained performance or evidence of profitability. Arc temperature monitoring was unavailable and the interface reports that limitation. WoolyPooly, Zpool and Suprnova answered real Stratum subscription checks during that baseline; those checks did not prove authorization, share acceptance or payout.

## Still requires field testing

- Real pool authorization, accepted live mining shares and a completed VTC payout to the user's wallet.
- A complete mainnet node sync, a funded Core wallet, and inbound reachability through a real home router.
- Sustained thermal behavior, sleep/resume, outages and extended operation.
- Other devices/drivers, AMD hardware, and broader accessibility/display testing.
- Further end-to-end local AI workload tests. Automated worker transitions use a controlled inference probe and a real fixture worker.

## Reproduce

Run `build.ps1`, then `build/tests/OcmTests.exe`. Tests use temporary files and a fixture worker, not pool mining. `--pools` checks real pool subscriptions; `--ai` reads local inference status. The app's **Test GPU offline** performs hardware hashing after setup without a payout address. Its loopback endpoint and checksum-valid zero-hash diagnostic address are used only with VerthashMiner's offline benchmark mode; real mining always uses the user's saved address.

To run the separate Core smoke test after building, supply the official daemon's path:

```powershell
.\build\tests\OcmTests.exe --core-smoke 'C:\path\to\vertcoind.exe'
```

This starts a real isolated regtest node, creates an encrypted test wallet, checks receiving addresses and ownership, then gracefully stops and restarts the node to verify persistence. It uses no network peers or funds. Allow extra disk space and first-start time for the Verthash data file. The optional third argument is an existing canonical `verthash.dat` file to copy into the test directory. See the [Core setup and router guide](docs/CORE-NODE.md) for normal app use.
