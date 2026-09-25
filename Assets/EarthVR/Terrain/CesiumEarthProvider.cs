using System.Collections;
using CesiumForUnity;
using EarthVR.Configuration;
using UnityEngine;

namespace EarthVR.Terrain
{
    public sealed class CesiumEarthProvider : MonoBehaviour
    {
        // Cesium ion's curated Google Photorealistic 3D Tiles asset.
        private const long GooglePhotorealistic3DTilesAssetId = 2275207;

        public CesiumGeoreference Georeference { get; private set; }
        public Cesium3DTileset Tileset { get; private set; }
        public bool HasAccessToken { get; private set; }
        public string StatusMessage { get; private set; } = "Loading Cesium ion configuration";

        private void OnEnable()
        {
            Cesium3DTileset.OnCesium3DTilesetLoadFailure += OnTilesetLoadFailure;
            CesiumRasterOverlay.OnCesiumRasterOverlayLoadFailure += OnRasterOverlayLoadFailure;
        }

        private void OnDisable()
        {
            Cesium3DTileset.OnCesium3DTilesetLoadFailure -= OnTilesetLoadFailure;
            CesiumRasterOverlay.OnCesiumRasterOverlayLoadFailure -= OnRasterOverlayLoadFailure;
        }

        public void Initialize(EarthVRSettings settings)
        {
            Georeference = gameObject.AddComponent<CesiumGeoreference>();
            Georeference.SetOriginLongitudeLatitudeHeight(
                settings.startLongitude,
                settings.startLatitude,
                0d);
            Georeference.scale = 1d;

            var tilesObject = new GameObject("Google Photorealistic 3D Tiles (via Cesium ion)");
            tilesObject.transform.SetParent(transform, false);
            Tileset = tilesObject.AddComponent<Cesium3DTileset>();
            ConfigureTileset(Tileset, settings);
            Tileset.tilesetSource = CesiumDataSource.FromCesiumIon;
            Tileset.ionAssetID = GooglePhotorealistic3DTilesAssetId;

            StartCoroutine(LoadCesiumWorld());
        }

        private static void ConfigureTileset(Cesium3DTileset tileset, EarthVRSettings settings)
        {
            tileset.suspendUpdate = true;
            tileset.showCreditsOnScreen = true;
            tileset.maximumScreenSpaceError = Application.isMobilePlatform
                ? settings.standaloneMaximumScreenSpaceError
                : settings.pcMaximumScreenSpaceError;
            tileset.maximumCachedBytes = (long)(Application.isMobilePlatform
                ? settings.standaloneCacheMegabytes
                : settings.pcCacheMegabytes) * 1024L * 1024L;
            tileset.maximumSimultaneousTileLoads = (uint)settings.maximumSimultaneousTileLoads;
            tileset.preloadAncestors = true;
            tileset.preloadSiblings = true;
            // Waiting for every child before refining can create large upload bursts
            // and visible frame stalls in VR. Allow refinement to stream progressively.
            tileset.forbidHoles = false;
            tileset.loadingDescendantLimit = 20;
            tileset.enableFrustumCulling = true;
            // Cesium's fog culling is an internal distance heuristic, independent
            // of Unity fog. Disable it so the requested LOD is retained toward
            // the horizon instead of silently dropping distant tiles.
            tileset.enableFogCulling = false;
            tileset.createPhysicsMeshes = settings.createPhysicsMeshes;
        }

        private IEnumerator LoadCesiumWorld()
        {
            string accessToken = null;
            yield return CesiumIonConfiguration.LoadAccessToken(value => accessToken = value);
            HasAccessToken = !string.IsNullOrEmpty(accessToken);
            if (!HasAccessToken)
            {
                StatusMessage = $"Token missing: {CesiumIonConfiguration.LocalFileName}";
                yield break;
            }

            Tileset.ionAccessToken = accessToken;
            StatusMessage = "Google Photorealistic 3D Tiles configured via Cesium ion";
            Tileset.suspendUpdate = false;
            Tileset.RecreateTileset();
            Debug.Log("EarthVR configured Google Photorealistic 3D Tiles through Cesium ion.");
        }

        private void OnTilesetLoadFailure(Cesium3DTilesetLoadFailureDetails details)
        {
            if (details.tileset != Tileset)
                return;

            StatusMessage = $"Google 3D Tiles error (HTTP {details.httpStatusCode})";
            Debug.LogError($"EarthVR could not load Google Photorealistic 3D Tiles through Cesium ion: {details.message}");
        }

        private void OnRasterOverlayLoadFailure(CesiumRasterOverlayLoadFailureDetails details)
        {
            if (details.overlay == null || Tileset == null || details.overlay.gameObject != Tileset.gameObject)
                return;

            StatusMessage = $"Google 3D Tiles overlay error (HTTP {details.httpStatusCode})";
            Debug.LogError($"EarthVR Google Photorealistic 3D Tiles overlay failed: {details.message}");
        }
    }
}
