# v0.5.0 — Core node, wallet and gallery HUD

- Added optional official Vertcoin Core v23.2 preparation with a pinned archive checksum, explicit node start/connect controls, sync and peer status, pruning settings and local cookie-authenticated RPC.
- Added encrypted Core wallet creation, balance display, receiving addresses and an explicit action to use a Core address for mining. Wallet creation requires a confirmed passphrase of at least 12 characters; the passphrase is not saved in app settings.
- Added a graceful handoff from a managed daemon to the official Core wallet GUI for backups, recovery and sending coins. Existing connected Core instances remain independently controlled.
- Added a router guide and local network hints. Incoming peers are opt-in; the app does not change router or firewall rules. RPC stays on loopback.
- Added a separate 23-picture resizable gallery with full-screen/F11/Esc controls, optional slideshow, and a green-and-cyan HUD showing real miner, AI, pool, node and music status. Gallery audio controls share the existing jukebox.
- Retained all 22 recordings, 15 spoken reactions, five sound effects, independent mutes and the existing mining controls.
- All **73 automated checks** and **six real Core v23.2 regtest smoke checks** passed, including encrypted-wallet reload and graceful process shutdown. Regtest used no network peers or funds.

This remains an unsigned independent community preview. Live pool share acceptance, completed payouts, a full mainnet node sync and public inbound reachability are not verified by these tests.

## v0.4.0 — Vertcoin Bitch Blast! Edition

- Rebuilt the interface around Jona's walnut/trailer reference, plum-and-green palette, Emi patch, and new product name.
- Added a local 22-song jukebox with cover artwork, lyrics, seeking, shuffle, repeat, and persistent audio preferences.
- Added 15 spoken interaction reactions and five original effects. Music, effects and voice have independent volume/mute controls and a master mute. Music ducks during voice commentary.
- Retained address-based payouts, selectable pools, offline GPU test, worker ownership/cleanup and automatic sharing for supported local AI.
- Added 12 audio/catalog checks; all 46 automated checks pass. All 42 bundled audio files fully decode.

This is an independent community preview. Live share acceptance and payouts remain unverified.
