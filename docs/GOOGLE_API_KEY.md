# Google Map Tiles API key

1. Create or choose a project in [Google Cloud Console](https://console.cloud.google.com/).
2. Attach a billing account. Photorealistic 3D Tiles usage is billable and subject to Google Maps Platform terms.
3. Open **APIs & Services > Library** and enable **Map Tiles API**.
4. Open **APIs & Services > Credentials**, create an API key, and restrict it to the Map Tiles API.
5. Copy `Assets/StreamingAssets/EarthVR/google-maps.example.json` to `Assets/StreamingAssets/EarthVR/google-maps.local.json`.
6. Replace the placeholder in the new `.local.json` file. That exact local file and its `.meta` are ignored by Git.

For editor-only convenience, `EARTHVR_GOOGLE_MAPS_API_KEY` may instead be defined in the process environment before Unity starts. The runtime constructs `https://tile.googleapis.com/v1/3dtiles/root.json?key=...`; no key is embedded in source.

Keys shipped in a desktop or standalone client can always be extracted. API restrictions cannot reliably bind a Unity desktop binary by HTTP referrer, so use the narrowest restrictions Google offers for this credential, set conservative per-day quotas, configure billing budget alerts, monitor usage, rotate the key if exposed, and do not distribute personal builds containing it.

Google requires attribution supplied with the tiles. `Cesium3DTileset.showCreditsOnScreen` is enabled, allowing Cesium's supported credit system to render the current Google/data-provider credits. Do not replace it with static attribution text. Confirm legibility in the headset during every release test.

References: [Google Map Tiles setup](https://developers.google.com/maps/documentation/tile/cloud-setup), [API security best practices](https://developers.google.com/maps/api-security-best-practices), and [Photorealistic 3D Tiles policies](https://developers.google.com/maps/documentation/tile/policies).

