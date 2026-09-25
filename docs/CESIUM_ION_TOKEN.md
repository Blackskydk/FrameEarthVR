# Cesium ion access token

EarthVR streams Google Photorealistic 3D Tiles through Cesium ion.

1. Sign in at [Cesium ion](https://ion.cesium.com/).
2. In the Asset Depot, add **Google Photorealistic 3D Tiles** to your assets if it is not already present.
3. Open **Access Tokens** and create a token for this application. For local development, the account's default token also works.
4. Give the token the public `assets:read` scope. If asset restrictions are enabled, allow Google Photorealistic 3D Tiles asset ID `2275207`.
5. Copy `Assets/StreamingAssets/EarthVR/cesium-ion.example.json` to `Assets/StreamingAssets/EarthVR/cesium-ion.local.json`.
6. Replace the placeholder in the new local file with the token, save it, and enter Play mode again.

The local file and its Unity `.meta` file are ignored by Git. For editor-only convenience, `EARTHVR_CESIUM_ION_ACCESS_TOKEN` may instead be defined in the environment before Unity starts.

Access tokens shipped in a player can be extracted. Before distributing a build, create a separate application token with only `assets:read`, restrict it to asset `2275207`, and monitor its usage in Cesium ion.
