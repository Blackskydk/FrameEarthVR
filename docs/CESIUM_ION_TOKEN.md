# Cesium ion access token

EarthVR streams Google Photorealistic 3D Tiles through Cesium ion.

1. Sign in at [Cesium ion](https://ion.cesium.com/).
2. In the Asset Depot, add **Google Photorealistic 3D Tiles** to your assets if it is not already present.
3. Open **Access Tokens** and create a token for this application. For local development, the account's default token also works.
4. Give the token the public `assets:read` scope. If asset restrictions are enabled, allow Google Photorealistic 3D Tiles asset ID `2275207`.
5. Copy `Assets/StreamingAssets/EarthVR/cesium-ion.example.json` to `Assets/StreamingAssets/EarthVR/cesium-ion.local.json`.
6. Replace the placeholder in the new local file with the token, save it, and enter Play mode again.

The local file and its Unity `.meta` file are ignored by Git. For editor-only convenience, `EARTHVR_CESIUM_ION_ACCESS_TOKEN` may instead be defined in the environment before Unity starts.

Public releases do not include a publisher token. End users enter their own token in the first-run setup panel; release runtime ignores developer environment variables and bundled configuration. Use **Build Release APK** to exclude your local credential files while preserving them for development. See [standalone installation](STANDALONE_INSTALL.md).

Google Photorealistic 3D Tiles root requests have their own Cesium ion plan quota. Repeated HTTP 429 responses from `tile.googleapis.com` while this project uses `FromCesiumIon` indicate that upstream root access is rate- or quota-limited, not that the unused local Google key is invalid. Check the ion Usage dashboard; during a short-term limit, EarthVR pauses requests and retries with bounded exponential backoff.
