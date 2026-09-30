using System.Collections.Generic;
using EarthVR.Configuration;
using EarthVR.Core;
using EarthVR.Input;
using EarthVR.Navigation;
using UnityEngine;
using UnityEngine.Rendering;

namespace EarthVR.UI
{
    /// <summary>Marker used to keep globe-picker hits out of terrain grabbing.</summary>
    public sealed class MiniatureGlobeSurface : MonoBehaviour
    {
    }

    /// <summary>A map-textured destination globe carried by the left controller
    /// while the hand menu is open. The right-hand pointer selects a
    /// coordinate; a second press near the same point confirms travel.</summary>
    public sealed class MiniatureGlobePicker : MonoBehaviour
    {
        private const float ConfirmationSeconds = 4f;
        private const float ConfirmationAngleDegrees = 12f;
        private const int LongitudeSegments = 96;
        private const int LatitudeSegments = 48;
        public static readonly Vector3 LeftHandOffset = new(0f, 0.12f, 0.14f);

        private IEarthVRInput _input;
        private EarthVRSettings _settings;
        private EarthVRRig _rig;
        private NavigationController _navigation;
        private LoadingAwareArrivalController _arrival;
        private GameObject _root;
        private Transform _marker;
        private Transform _currentLocationMarker;
        private TextMesh _label;
        private TextMesh _currentLocationLabel;
        private bool _triggerWasHeld;
        private bool _dragging;
        private bool _dragMoved;
        private Vector3 _dragStartWorldNormal;
        private Vector3 _dragStartPointerDirection;
        private Vector3 _lastPointerWorldNormal;
        private Quaternion _dragStartRotation;
        private Quaternion _globeRotation = Quaternion.identity;
        private float _displayScale = 1f;
        private Vector3 _selectedNormal;
        private float _selectionExpires;
        private bool _visibilityAllowed = true;
        private bool _summoned;
        public bool IsVisible => _root != null && _root.activeSelf;

        /// <summary>A head-readable frame centered on the miniature Earth. UI
        /// parented here stays physically attached to the globe as the hand moves.</summary>
        public Transform InterfaceAnchor { get; private set; }

        /// <summary>Allows or suppresses the globe regardless of the hand menu,
        /// e.g. while the planetary overview owns the right-hand pointer.</summary>
        public void SetVisible(bool visible)
        {
            _visibilityAllowed = visible;
            UpdateVisibility();
            if (!visible)
            {
                _triggerWasHeld = _input != null && _input.RightTriggerHeld;
                ClearSelection();
            }
        }

        /// <summary>Shows or hides the globe together with the hand menu.</summary>
        public void SetSummoned(bool summoned)
        {
            _summoned = summoned;
            UpdateVisibility();
        }

        public void Initialize(
            IEarthVRInput input,
            EarthVRSettings settings,
            EarthVRRig rig,
            NavigationController navigation,
            LoadingAwareArrivalController arrival)
        {
            _input = input;
            _settings = settings;
            _rig = rig;
            _navigation = navigation;
            _arrival = arrival;
            CreateGlobe();
            _root.SetActive(false);
            UpdateVisibility();
        }

        private void OnEnable() => Application.onBeforeRender += StabilizeGlobePose;

        private void OnDisable() => Application.onBeforeRender -= StabilizeGlobePose;

        private void LateUpdate() => StabilizeGlobePose();

        private void Update()
        {
            if (_root == null || _input == null)
                return;

            UpdateVisibility();
            if (!_root.activeSelf)
            {
                _triggerWasHeld = _input.RightTriggerHeld;
                return;
            }

            // Follow the hand's position, but deliberately cancel its rotation.
            // Globe orientation belongs to the trackball interaction below.
            _root.transform.rotation = _globeRotation;
            RefreshInterfaceAnchorPose();
            UpdateCurrentLocationMarker();
            FaceLabelTowardViewer();
            var held = _input.RightTriggerHeld;
            var pressed = held && !_triggerWasHeld;
            var released = !held && _triggerWasHeld;
            _triggerWasHeld = held;
            var pointed = TryGetPointerNormal(out var pointerWorldNormal);
            if (pressed && pointed && !_arrival.IsArriving)
                BeginGlobeInteraction(pointerWorldNormal);
            if (_dragging && held &&
                Vector3.Angle(_dragStartPointerDirection, _rig.RightController.forward) >=
                Mathf.Max(0.5f, _settings.miniatureGlobeDragThresholdDegrees))
                _dragMoved = true;
            if (_dragging && held && pointed)
                UpdateGlobeDrag(pointerWorldNormal);
            if (_dragging && released)
                EndGlobeInteraction();

            var targetScale = pointed || _dragging
                ? Mathf.Clamp(_settings.miniatureGlobeHoverScale, 1f, 2f)
                : 1f;
            _displayScale = Mathf.MoveTowards(
                _displayScale,
                targetScale,
                Time.unscaledDeltaTime * 4f);
            _root.transform.localScale = Vector3.one * _displayScale;
            if (_selectionExpires > 0f && Time.unscaledTime > _selectionExpires)
                ClearSelection();
        }

        private void UpdateVisibility()
        {
            if (_root == null)
                return;

            // The controller menu button owns visibility. Head gaze never opens,
            // closes, or retains the globe after the user dismisses the menu.
            var shouldShow = _visibilityAllowed && !_arrival.IsArriving && _summoned;
            if (_root.activeSelf == shouldShow)
                return;

            _root.SetActive(shouldShow);
            if (!shouldShow)
            {
                _dragging = false;
                _displayScale = 1f;
                _triggerWasHeld = _input != null && _input.RightTriggerHeld;
                ClearSelection();
            }
        }

        private bool TryGetPointerNormal(out Vector3 worldNormal)
        {
            worldNormal = default;
            if (!Physics.Raycast(
                    _rig.RightController.position,
                    _rig.RightController.forward,
                    out var hit,
                    5f,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Collide) ||
                hit.collider.GetComponent<MiniatureGlobeSurface>() == null)
                return false;
            worldNormal = (hit.point - _root.transform.position).normalized;
            return true;
        }

        private void BeginGlobeInteraction(Vector3 pointerWorldNormal)
        {
            _dragging = true;
            _dragMoved = false;
            _dragStartWorldNormal = pointerWorldNormal;
            _dragStartPointerDirection = _rig.RightController.forward;
            _lastPointerWorldNormal = pointerWorldNormal;
            _dragStartRotation = _globeRotation;
        }

        private void UpdateGlobeDrag(Vector3 pointerWorldNormal)
        {
            _lastPointerWorldNormal = pointerWorldNormal;
            var angle = Vector3.Angle(_dragStartWorldNormal, pointerWorldNormal);
            if (angle >= Mathf.Max(0.5f, _settings.miniatureGlobeDragThresholdDegrees))
                _dragMoved = true;
            if (!_dragMoved)
                return;
            _globeRotation = CalculateDragRotation(
                _dragStartRotation,
                _dragStartWorldNormal,
                pointerWorldNormal);
            _root.transform.rotation = _globeRotation;
        }

        public static Quaternion CalculateDragRotation(
            Quaternion startRotation,
            Vector3 startWorldNormal,
            Vector3 currentWorldNormal) =>
            Quaternion.FromToRotation(
                startWorldNormal.normalized,
                currentWorldNormal.normalized) * startRotation;

        private void EndGlobeInteraction()
        {
            _dragging = false;
            if (_dragMoved)
                return;
            var localNormal = Quaternion.Inverse(_globeRotation) * _lastPointerWorldNormal;
            SelectOrTravel(localNormal.normalized);
        }

        private void SelectOrTravel(Vector3 normal)
        {
            if (_selectionExpires > Time.unscaledTime &&
                Vector3.Angle(_selectedNormal, normal) <= ConfirmationAngleDegrees)
            {
                var coordinates = NormalToLongitudeLatitude(normal);
                var destination = _arrival.CreateSearchDestination(new GeographicPlace(
                    $"Globe {coordinates.y:F2}°, {coordinates.x:F2}°",
                    coordinates.x,
                    coordinates.y));
                ClearSelection();
                _arrival.TravelTo(destination);
                return;
            }

            _selectedNormal = normal;
            _selectionExpires = Time.unscaledTime + ConfirmationSeconds;
            var lonLat = NormalToLongitudeLatitude(normal);
            _marker.gameObject.SetActive(true);
            _marker.localPosition = normal * (_settings.miniatureGlobeRadiusMeters + 0.008f);
            _label.text = $"{lonLat.y:F1}°, {lonLat.x:F1}°\nPRESS AGAIN TO TRAVEL";
        }

        public static Vector2 NormalToLongitudeLatitude(Vector3 normal)
        {
            normal.Normalize();
            var latitude = Mathf.Asin(Mathf.Clamp(normal.y, -1f, 1f)) * Mathf.Rad2Deg;
            // The map texture is mirrored onto the procedural sphere below, so
            // visible east/west and selected longitude use the same convention.
            var longitude = -Mathf.Atan2(normal.x, normal.z) * Mathf.Rad2Deg;
            return new Vector2(longitude, latitude);
        }

        public static Vector3 LongitudeLatitudeToNormal(double longitude, double latitude)
        {
            var longitudeRadians = (float)(longitude * Mathf.Deg2Rad);
            var latitudeRadians = (float)(latitude * Mathf.Deg2Rad);
            var cosLatitude = Mathf.Cos(latitudeRadians);
            return new Vector3(
                -Mathf.Sin(longitudeRadians) * cosLatitude,
                Mathf.Sin(latitudeRadians),
                Mathf.Cos(longitudeRadians) * cosLatitude).normalized;
        }

        private void ClearSelection()
        {
            _selectionExpires = 0f;
            if (_marker != null)
                _marker.gameObject.SetActive(false);
            if (_label != null)
                _label.text = "RIGHT POINTER + TRIGGER\nCLICK TO SELECT  •  DRAG TO ROTATE";
        }

        private void FaceLabelTowardViewer()
        {
            var towardViewer = _label.transform.position - _rig.Camera.transform.position;
            if (towardViewer.sqrMagnitude > 0.001f)
                _label.transform.rotation = Quaternion.LookRotation(towardViewer.normalized, Vector3.up);
            if (_currentLocationLabel != null)
            {
                towardViewer = _currentLocationLabel.transform.position - _rig.Camera.transform.position;
                if (towardViewer.sqrMagnitude > 0.001f)
                    _currentLocationLabel.transform.rotation = Quaternion.LookRotation(towardViewer.normalized, Vector3.up);
            }
        }

        public void RefreshInterfaceAnchorPose()
        {
            if (InterfaceAnchor == null || _rig == null)
                return;
            InterfaceAnchor.position = _root != null
                ? _root.transform.position
                : _rig.LeftController.TransformPoint(LeftHandOffset);
            var fromViewer = InterfaceAnchor.position - _rig.Camera.transform.position;
            if (fromViewer.sqrMagnitude > 0.000001f)
                InterfaceAnchor.rotation = Quaternion.LookRotation(fromViewer.normalized, Vector3.up);
        }

        private void UpdateCurrentLocationMarker()
        {
            if (_currentLocationMarker == null || _navigation == null)
                return;
            var llh = _navigation.LongitudeLatitudeHeight;
            var normal = LongitudeLatitudeToNormal(llh.x, llh.y);
            _currentLocationMarker.localPosition = normal * (_settings.miniatureGlobeRadiusMeters + 0.009f);
            if (_currentLocationLabel != null)
                _currentLocationLabel.text = $"YOU ARE HERE  •  {_navigation.CurrentPlaceName}";
        }

        private void StabilizeGlobePose()
        {
            if (_root == null)
                return;
            // The carry offset belongs to the view, not the controller's roll.
            var forward = Vector3.ProjectOnPlane(_rig.Camera.transform.forward, Vector3.up).normalized;
            _root.transform.position = _rig.LeftController.position + Vector3.up * LeftHandOffset.y +
                forward * LeftHandOffset.z;
            _root.transform.rotation = _globeRotation;
            RefreshInterfaceAnchorPose();
            UpdateCurrentLocationMarker();
            if (_label != null && _rig != null)
            {
                _label.transform.position = _root.transform.position +
                    Vector3.up * ((_settings.miniatureGlobeRadiusMeters + 0.075f) * _displayScale);
                if (_currentLocationLabel != null)
                    _currentLocationLabel.transform.position = _root.transform.position -
                        Vector3.up * ((_settings.miniatureGlobeRadiusMeters + 0.06f) * _displayScale);
                FaceLabelTowardViewer();
            }
        }

        private void CreateGlobe()
        {
            var radius = _settings.miniatureGlobeRadiusMeters;
            _root = new GameObject("Left-hand Destination Globe");
            _root.transform.SetParent(_rig.LeftController, false);
            _root.transform.localPosition = LeftHandOffset;
            _root.transform.localRotation = Quaternion.identity;

            InterfaceAnchor = new GameObject("Left-hand Globe Interface Anchor").transform;
            InterfaceAnchor.SetParent(_rig.LeftController, false);
            InterfaceAnchor.localPosition = LeftHandOffset;

            var sphere = new GameObject(
                "Blue Marble Destination Globe",
                typeof(MeshFilter),
                typeof(MeshRenderer),
                typeof(SphereCollider),
                typeof(MiniatureGlobeSurface));
            sphere.transform.SetParent(_root.transform, false);
            sphere.GetComponent<MeshFilter>().sharedMesh = CreateLongitudeLatitudeSphere(radius);
            sphere.GetComponent<SphereCollider>().radius = radius;

            var earthTexture = Resources.Load<Texture2D>("EarthVRMiniGlobe");
            var globeMaterial = new Material(FindTexturedUnlitShader())
            {
                name = "EarthVR NASA Blue Marble Globe Material",
                color = Color.white,
                hideFlags = HideFlags.DontSave
            };
            globeMaterial.mainTexture = earthTexture;
            if (globeMaterial.HasProperty("_BaseMap"))
                globeMaterial.SetTexture("_BaseMap", earthTexture);
            if (globeMaterial.HasProperty("_BaseColor"))
                globeMaterial.SetColor("_BaseColor", Color.white);
            var globeRenderer = sphere.GetComponent<MeshRenderer>();
            globeRenderer.sharedMaterial = globeMaterial;
            globeRenderer.shadowCastingMode = ShadowCastingMode.Off;
            globeRenderer.receiveShadows = false;
            if (earthTexture == null)
                Debug.LogError("EarthVR miniature globe map is missing from UI/Resources/EarthVRMiniGlobe.");

            var lineMaterial = new Material(FindLineShader())
            {
                name = "EarthVR Globe Graticule Material",
                color = new Color(0.45f, 0.9f, 1f, 0.24f),
                hideFlags = HideFlags.DontSave
            };
            CreateGraticule(radius * 1.006f, lineMaterial);

            var markerObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            markerObject.name = "Selected Destination";
            markerObject.transform.SetParent(_root.transform, false);
            markerObject.transform.localScale = Vector3.one * 0.016f;
            Destroy(markerObject.GetComponent<Collider>());
            var markerMaterial = new Material(FindLineShader())
            {
                name = "EarthVR Globe Selection Material",
                color = new Color(1f, 0.72f, 0.12f, 1f),
                hideFlags = HideFlags.DontSave
            };
            markerObject.GetComponent<MeshRenderer>().sharedMaterial = markerMaterial;
            _marker = markerObject.transform;

            var currentMarkerObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            currentMarkerObject.name = "Current Location - You Are Here";
            currentMarkerObject.transform.SetParent(_root.transform, false);
            currentMarkerObject.transform.localScale = Vector3.one * 0.020f;
            Destroy(currentMarkerObject.GetComponent<Collider>());
            var currentMarkerMaterial = new Material(FindLineShader())
            {
                name = "EarthVR Current Location Material",
                color = new Color(0.2f, 1f, 0.62f, 1f),
                hideFlags = HideFlags.DontSave
            };
            currentMarkerObject.GetComponent<MeshRenderer>().sharedMaterial = currentMarkerMaterial;
            _currentLocationMarker = currentMarkerObject.transform;

            var labelObject = new GameObject("Globe Instructions", typeof(TextMesh));
            labelObject.transform.SetParent(_root.transform, false);
            labelObject.transform.localPosition = new Vector3(0f, radius + 0.075f, 0f);
            _label = labelObject.GetComponent<TextMesh>();
            _label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _label.fontSize = 48;
            _label.characterSize = 0.0025f;
            _label.anchor = TextAnchor.MiddleCenter;
            _label.alignment = TextAlignment.Center;
            _label.color = new Color(0.9f, 0.97f, 1f, 1f);

            var currentLabelObject = new GameObject("Current Location Label", typeof(TextMesh));
            currentLabelObject.transform.SetParent(_root.transform, false);
            currentLabelObject.transform.localPosition = new Vector3(0f, -radius - 0.06f, 0f);
            _currentLocationLabel = currentLabelObject.GetComponent<TextMesh>();
            _currentLocationLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _currentLocationLabel.fontSize = 42;
            _currentLocationLabel.characterSize = 0.0023f;
            _currentLocationLabel.anchor = TextAnchor.MiddleCenter;
            _currentLocationLabel.alignment = TextAlignment.Center;
            _currentLocationLabel.color = new Color(0.2f, 1f, 0.62f, 1f);
            UpdateCurrentLocationMarker();
            ClearSelection();
        }

        public static Mesh CreateLongitudeLatitudeSphere(float radius)
        {
            var vertices = new Vector3[(LatitudeSegments + 1) * (LongitudeSegments + 1)];
            var normals = new Vector3[vertices.Length];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[LatitudeSegments * LongitudeSegments * 6];
            var vertex = 0;
            for (var latitudeIndex = 0; latitudeIndex <= LatitudeSegments; latitudeIndex++)
            {
                var v = latitudeIndex / (float)LatitudeSegments;
                var latitude = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f, v);
                var cosLatitude = Mathf.Cos(latitude);
                for (var longitudeIndex = 0; longitudeIndex <= LongitudeSegments; longitudeIndex++)
                {
                    var u = longitudeIndex / (float)LongitudeSegments;
                    var longitude = Mathf.Lerp(-Mathf.PI, Mathf.PI, u);
                    var normal = new Vector3(
                        Mathf.Sin(longitude) * cosLatitude,
                        Mathf.Sin(latitude),
                        Mathf.Cos(longitude) * cosLatitude);
                    vertices[vertex] = normal * radius;
                    normals[vertex] = normal;
                    // Blue Marble's visual east/west orientation appeared
                    // mirrored on the original procedural mesh in-headset.
                    // Flip U and apply the same sign in hit conversion.
                    uv[vertex] = new Vector2(1f - u, v);
                    vertex++;
                }
            }

            var triangle = 0;
            for (var latitudeIndex = 0; latitudeIndex < LatitudeSegments; latitudeIndex++)
            {
                for (var longitudeIndex = 0; longitudeIndex < LongitudeSegments; longitudeIndex++)
                {
                    var a = latitudeIndex * (LongitudeSegments + 1) + longitudeIndex;
                    var b = a + 1;
                    var c = a + LongitudeSegments + 1;
                    var d = c + 1;
                    // Outward-facing winding. The UV flip above corrects the
                    // map orientation; reversing geometry instead exposes the
                    // inside of the globe in Unity's default back-face culling.
                    triangles[triangle++] = a;
                    triangles[triangle++] = b;
                    triangles[triangle++] = c;
                    triangles[triangle++] = b;
                    triangles[triangle++] = d;
                    triangles[triangle++] = c;
                }
            }

            var mesh = new Mesh { name = "EarthVR Longitude-Latitude Globe" };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        private void CreateGraticule(float radius, Material material)
        {
            for (var latitude = -60; latitude <= 60; latitude += 30)
            {
                var points = new List<Vector3>();
                var lat = latitude * Mathf.Deg2Rad;
                for (var longitude = -180; longitude <= 180; longitude += 10)
                {
                    var lon = longitude * Mathf.Deg2Rad;
                    points.Add(new Vector3(
                        Mathf.Sin(lon) * Mathf.Cos(lat),
                        Mathf.Sin(lat),
                        Mathf.Cos(lon) * Mathf.Cos(lat)) * radius);
                }
                CreateLine($"Latitude {latitude}", points, material);
            }

            for (var longitude = -150; longitude <= 180; longitude += 30)
            {
                var points = new List<Vector3>();
                var lon = longitude * Mathf.Deg2Rad;
                for (var latitude = -90; latitude <= 90; latitude += 5)
                {
                    var lat = latitude * Mathf.Deg2Rad;
                    points.Add(new Vector3(
                        Mathf.Sin(lon) * Mathf.Cos(lat),
                        Mathf.Sin(lat),
                        Mathf.Cos(lon) * Mathf.Cos(lat)) * radius);
                }
                CreateLine($"Longitude {longitude}", points, material);
            }
        }

        private void CreateLine(string name, IReadOnlyList<Vector3> points, Material material)
        {
            var lineObject = new GameObject(name, typeof(LineRenderer));
            lineObject.transform.SetParent(_root.transform, false);
            var line = lineObject.GetComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = false;
            line.positionCount = points.Count;
            line.widthMultiplier = 0.00055f;
            line.material = material;
            line.startColor = Color.white;
            line.endColor = Color.white;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            for (var i = 0; i < points.Count; i++)
                line.SetPosition(i, points[i]);
        }

        private static Shader FindTexturedUnlitShader() =>
            Resources.Load<Shader>("EarthVRHandUnlit") ?? Shader.Find("Universal Render Pipeline/Unlit") ??
            Shader.Find("Unlit/Texture") ??
            Shader.Find("Standard");

        private static Shader FindLineShader() =>
            Resources.Load<Shader>("EarthVRHandUnlit") ?? Shader.Find("Sprites/Default") ??
            Shader.Find("Universal Render Pipeline/Unlit") ??
            Shader.Find("Unlit/Color");
    }
}
