using System.Collections.Generic;
using CesiumForUnity;
using EarthVR.Configuration;
using EarthVR.Core;
using EarthVR.Scaling;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace EarthVR.UI
{
    /// <summary>
    /// A bundled, offline set of orientation labels for planetary view. No
    /// geocoder, places service, or other metered API is contacted.
    /// </summary>
    public sealed class GlobeOverviewLabels : MonoBehaviour
    {
        private const double LabelAltitudeMeters = 65000d;
        private const double SurfaceAltitudeMeters = 0d;

        private readonly List<LabelVisual> _visuals = new List<LabelVisual>();
        private readonly List<Vector2> _occupiedViewportPositions = new List<Vector2>();
        private EarthVRSettings _settings;
        private EarthVRRig _rig;
        private CesiumGeoreference _georeference;
        private WorldManipulationController _scaling;
        private bool _visibilityRequested;
        private Material _cityMarkerMaterial;
        private Material _placeMarkerMaterial;

        private sealed class LabelVisual
        {
            public OfflinePlaceCatalog.CatalogPlace data;
            public Transform marker;
            public TextMesh text;
            public TextMesh shadow;
        }

        public void Initialize(
            EarthVRSettings settings,
            EarthVRRig rig,
            CesiumGeoreference georeference,
            WorldManipulationController scaling)
        {
            _settings = settings;
            _rig = rig;
            _georeference = georeference;
            _scaling = scaling;
            CreateVisuals();
            SetVisible(false);
        }

        public void SetVisible(bool visible)
        {
            _visibilityRequested = visible;
            if (!visible)
                HideAll();
        }

        private void LateUpdate()
        {
            if (!_visibilityRequested || _settings == null || _rig == null ||
                _georeference == null || _scaling == null)
                return;

            var minimumScale = Mathf.Max(1000f, _settings.globeOverviewLabelsMinimumScale);
            var scaleFade = Mathf.InverseLerp(minimumScale * 0.65f, minimumScale * 1.15f, _scaling.UserScale);
            if (scaleFade <= 0f)
            {
                HideAll();
                return;
            }

            var camera = _rig.Camera;
            var center = EcefToWorld(double3.zero);
            var cameraFromCenter = camera.transform.position - center;
            var cameraDistance = cameraFromCenter.magnitude;
            var globeRadius = Vector3.Distance(
                center,
                EcefToWorld(new double3(6378137d, 0d, 0d)));
            if (cameraDistance <= globeRadius)
            {
                HideAll();
                return;
            }

            var cameraDirection = cameraFromCenter / cameraDistance;
            var horizonThreshold = globeRadius / cameraDistance - 0.025f;
            var separation = Mathf.Clamp(_settings.globeOverviewLabelSeparation, 0.01f, 0.2f);
            var separationSquared = separation * separation;
            var maximum = Mathf.Clamp(_settings.globeOverviewMaximumLabels, 4, 64);
            var shown = 0;
            _occupiedViewportPositions.Clear();

            for (var i = 0; i < _visuals.Count; i++)
            {
                var visual = _visuals[i];
                var surface = EcefToWorld(ToEcef(visual.data, SurfaceAltitudeMeters));
                var outward = (surface - center).normalized;
                var facing = Vector3.Dot(outward, cameraDirection);
                var anchor = EcefToWorld(ToEcef(visual.data, LabelAltitudeMeters));
                var viewport = camera.WorldToViewportPoint(anchor);
                var viewportPosition = new Vector2(viewport.x, viewport.y);

                var eligible = facing >= horizonThreshold && viewport.z > 0f &&
                               viewport.x > 0.035f && viewport.x < 0.965f &&
                               viewport.y > 0.04f && viewport.y < 0.96f && shown < maximum &&
                               !Overlaps(viewportPosition, separationSquared);
                if (!eligible)
                {
                    SetActive(visual, false);
                    continue;
                }

                _occupiedViewportPositions.Add(viewportPosition);
                shown++;
                var horizonFade = Mathf.InverseLerp(horizonThreshold, horizonThreshold + 0.12f, facing);
                var alpha = Mathf.Lerp(0.62f, 1f, scaleFade) * Mathf.Lerp(0.58f, 1f, horizonFade);
                var textPosition = anchor + outward * 0.028f;
                visual.marker.position = anchor;
                visual.marker.localScale = Vector3.one * Mathf.Lerp(0.016f, 0.028f, alpha);
                visual.text.transform.position = textPosition;
                var towardViewer = textPosition - camera.transform.position;
                if (towardViewer.sqrMagnitude > 0.000001f)
                {
                    visual.text.transform.rotation = Quaternion.LookRotation(towardViewer.normalized, camera.transform.up);
                    visual.shadow.transform.rotation = visual.text.transform.rotation;
                    visual.shadow.transform.position = textPosition +
                                                       camera.transform.right * 0.0018f -
                                                       camera.transform.up * 0.0018f +
                                                       towardViewer.normalized * 0.001f;
                }
                var baseColor = visual.data.Category != "City"
                    ? new Color(1f, 0.79f, 0.32f, alpha)
                    : new Color(0.88f, 0.97f, 1f, alpha);
                visual.text.color = baseColor;
                visual.shadow.color = new Color(0f, 0.012f, 0.025f, alpha * 0.92f);
                SetActive(visual, true);
            }
        }

        private bool Overlaps(Vector2 candidate, float separationSquared)
        {
            for (var i = 0; i < _occupiedViewportPositions.Count; i++)
            {
                if ((_occupiedViewportPositions[i] - candidate).sqrMagnitude < separationSquared)
                    return true;
            }
            return false;
        }

        private void CreateVisuals()
        {
            _cityMarkerMaterial = CreateMarkerMaterial(
                "Planetary City Marker Material",
                new Color(0.44f, 0.9f, 1f, 0.95f));
            _placeMarkerMaterial = CreateMarkerMaterial(
                "Planetary Place Marker Material",
                new Color(1f, 0.64f, 0.12f, 0.95f));

            for (var i = 0; i < OfflinePlaceCatalog.All.Count; i++)
            {
                var data = OfflinePlaceCatalog.All[i];
                if (!data.ShowOnGlobe)
                    continue;
                var markerObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                markerObject.name = $"Globe label marker - {data.Name}";
                markerObject.transform.SetParent(transform, false);
                Destroy(markerObject.GetComponent<Collider>());
                var markerRenderer = markerObject.GetComponent<MeshRenderer>();
                markerRenderer.shadowCastingMode = ShadowCastingMode.Off;
                markerRenderer.receiveShadows = false;
                markerRenderer.sharedMaterial = data.Category != "City" ? _placeMarkerMaterial : _cityMarkerMaterial;

                var textObject = new GameObject($"Globe label - {data.Name}", typeof(TextMesh));
                textObject.transform.SetParent(transform, false);
                var text = textObject.GetComponent<TextMesh>();
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.fontSize = data.Category != "City" ? 50 : 56;
                text.characterSize = data.Category != "City" ? 0.0031f : 0.0034f;
                text.anchor = TextAnchor.LowerCenter;
                text.alignment = TextAlignment.Center;
                text.fontStyle = FontStyle.Bold;
                text.text = data.Name;
                var textRenderer = text.GetComponent<MeshRenderer>();
                textRenderer.shadowCastingMode = ShadowCastingMode.Off;
                textRenderer.receiveShadows = false;
                textRenderer.sortingOrder = 20;

                var shadowObject = new GameObject($"Globe label shadow - {data.Name}", typeof(TextMesh));
                shadowObject.transform.SetParent(transform, false);
                var shadow = shadowObject.GetComponent<TextMesh>();
                shadow.font = text.font;
                shadow.fontSize = text.fontSize;
                shadow.characterSize = text.characterSize;
                shadow.anchor = text.anchor;
                shadow.alignment = text.alignment;
                shadow.fontStyle = FontStyle.Bold;
                shadow.text = text.text;
                var shadowRenderer = shadow.GetComponent<MeshRenderer>();
                shadowRenderer.shadowCastingMode = ShadowCastingMode.Off;
                shadowRenderer.receiveShadows = false;
                shadowRenderer.sortingOrder = 19;

                _visuals.Add(new LabelVisual
                {
                    data = data,
                    marker = markerObject.transform,
                    text = text,
                    shadow = shadow
                });
            }
        }

        private static Material CreateMarkerMaterial(string name, Color color)
        {
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                return null;
            return new Material(shader)
            {
                name = name,
                color = color,
                hideFlags = HideFlags.DontSave
            };
        }

        private static double3 ToEcef(OfflinePlaceCatalog.CatalogPlace label, double altitude) =>
            CesiumWgs84Ellipsoid.LongitudeLatitudeHeightToEarthCenteredEarthFixed(
                new double3(label.Longitude, label.Latitude, altitude));

        private Vector3 EcefToWorld(double3 ecef)
        {
            var local = _georeference.TransformEarthCenteredEarthFixedPositionToUnity(ecef);
            return _georeference.transform.TransformPoint((Vector3)(float3)local);
        }

        private static void SetActive(LabelVisual visual, bool active)
        {
            if (visual.marker.gameObject.activeSelf != active)
                visual.marker.gameObject.SetActive(active);
            if (visual.text.gameObject.activeSelf != active)
                visual.text.gameObject.SetActive(active);
            if (visual.shadow.gameObject.activeSelf != active)
                visual.shadow.gameObject.SetActive(active);
        }

        private void HideAll()
        {
            for (var i = 0; i < _visuals.Count; i++)
                SetActive(_visuals[i], false);
        }

        private void OnDisable() => HideAll();

        private void OnDestroy()
        {
            if (_cityMarkerMaterial != null)
                Destroy(_cityMarkerMaterial);
            if (_placeMarkerMaterial != null)
                Destroy(_placeMarkerMaterial);
        }
    }
}
