# v0.4.0 verification record

Tested on Windows x64 on 2026-11-18. Product: **Vertcoin Bitch Blast! Edition**.

## Completed

- Self-contained .NET 8 WPF release build succeeded. Build host uses .NET SDK 11.0.100-rc.3 to target .NET 8.
- **46 automated checks passed**, covering payout-address validation, endpoint validation, AI pause/resume and unknown-state behavior, worker lifecycle/crash cleanup, real socket subscription parsing, fixture-worker payout routing/logs, media path containment, unavailable-track handling, repeat/shuffle selection, independent mutes, ducking, volume bounds and preference persistence. [Full log](tests/verification-log.txt).
- **42 audio files fully decoded** through Windows Media Foundation: 22 MP3 songs, 15 spoken WAV reactions and five WAV effects. Durations, hashes and file sizes are recorded in [Audio-Verification.json](docs/Audio-Verification.json). This verifies media integrity, not a full subjective listening review.
- Desktop inspection confirmed the new name, transparent Emi patch, walnut/trailer artwork, pool controls, AI switch, installed 22-track catalog and matching artwork.
- Actual player inspection verified advancing playback time, pause holding position, resume, seeking, next-track switching, No Permission Slip playback, master mute/unmute, persisted mute settings and clean window shutdown. Voice/effect test controls were exercised. Automated checks independently cover the three channel volume rules.
- Music does not start automatically. No live mining or payouts were started during this update.

## Earlier hardware baseline

The earlier mining implementation was tested on 2026-11-17 with the pinned official VerthashMiner 0.7.2 and its canonical 1,283,457,024-byte data file. Brief offline samples were 339.94 kH/s on an RTX 3050 Ti Laptop GPU and 167.27 kH/s on an Intel Arc A770. These are earlier short samples, not fresh v0.4.0 benchmarks, sustained performance or evidence of profitability. Arc temperature monitoring was unavailable and the interface reports that limitation. WoolyPooly, Zpool and Suprnova answered real Stratum subscription checks during that baseline.

## Still requires field testing

- Real pool authorization, accepted live mining shares and a completed VTC payout to the user's wallet.
- Sustained thermal behavior, sleep/resume, outages and extended operation.
- Other devices/drivers, AMD hardware, and broader accessibility/display testing.
- Further end-to-end local AI workload tests. Automated worker transitions use a controlled inference probe and a real fixture worker.

## Reproduce

Run `build.ps1`, then `build/tests/OcmTests.exe`. Tests use temporary files and a fixture worker, not pool mining. `--pools` checks real pool subscriptions; `--ai` reads local inference status. The app's **Test GPU offline** performs hardware hashing after setup without a payout address. Its loopback endpoint and checksum-valid zero-hash diagnostic address are used only with VerthashMiner's offline benchmark mode; real mining always uses the user's saved address.
