# Eikon

In-game, end-to-end-encrypted, Grindr-style match-making for gay men playing FFXIV. This is the client half: a [Dalamud](https://github.com/goatcorp/Dalamud) plugin (C#) that draws the UI, holds the crypto, and talks to the Eikon server over an authenticated WebSocket. Open it in-game with `/eikon`.

> **The server never sees a plaintext message.** Identity keys, the message ratchet, and photo encryption all live here in the plugin. `api.eikon.chat` only ever relays ciphertext; it can't read your chats, and neither can I. **No service keys, signing keys, or client secrets ship in the plugin**; the server holds its own secrets on its own host.

## Install (players)

You don't build anything. In Dalamud (`/xlsettings` → Experimental → custom plugin repositories) add:

```
https://eikon.chat/repo.json
```

Then open the plugin installer, find **Eikon**, install it, and type `/eikon`. Updates arrive on Dalamud's next refresh.

## Build from source

You need a Dalamud dev environment (XIVLauncher installed, so the SDK can find dev Dalamud under `%AppData%\XIVLauncher`) and the .NET SDK that `Dalamud.NET.Sdk` 15 pins, currently **.NET 10**.

```sh
git clone --recurse-submodules <this-repo>   # OtterGui lives in external/ as a submodule
git submodule update --init                   # if you cloned without --recurse-submodules
dotnet build -c Release
```

The output lands in `bin/Release/Eikon/`: the DLL, the `Eikon.json` manifest, every managed dependency, and the native libsodium runtimes, packaged the way Dalamud expects. Point a local custom repo at that folder (or the `latest.zip` inside it) to test your build in-game.

## Point it somewhere else (self-hosting)

The plugin defaults to `https://api.eikon.chat`. Nothing about the protocol is tied to my deployment. Run your own server and set `ServerBaseUrl` in the saved config:

```
%AppData%\XIVLauncher\pluginConfigs\Eikon.json  →  "ServerBaseUrl": "http://127.0.0.1:8080"
```

It persists and isn't reset, so you set it once.

## Layout

```
Plugin.cs        Entry point, builds the DI container and wires the command + windows
Config/          Persisted settings (DPAPI-sealed session at rest)
Crypto/          Key vault, DPAPI sealing, the moderation-escrow key
Net/             API client, auth, the E2E relay, and every per-feature service
Generated/       Wire types emitted from the contracts, never hand-edited
Screens/         One file per screen, routed by ScreenRouter
UI/              The widget kit, theme, and fonts
Windows/         The main window, the orb, and toast notifications
external/        OtterGui (submodule, its own license)
```

## License

[AGPL-3.0-or-later](LICENSE). If you run a modified copy as a network service, you owe your users the source. OtterGui under `external/` keeps its own license.
