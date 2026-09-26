using System.Collections.Generic;
using System.Text;
using System.Threading;
using EarthVR.Configuration;
using EarthVR.Core;
using EarthVR.Input;
using EarthVR.Navigation;
using EarthVR.Scaling;
using EarthVR.Terrain;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UI;

namespace EarthVR.UI
{
    /// <summary>
    /// A floating panel summoned in front of the viewer (not attached to a
    /// hand). Opening it re-anchors it a comfortable distance in front of
    /// wherever the head is currently looking; it then stays put in the world
    /// until closed and reopened, rather than chasing the head every frame.
    /// </summary>
    public sealed class EarthVRWristMenu : MonoBehaviour
    {
        private const float UiRefreshIntervalSeconds = 0.25f;
        private const float SummonDistanceMeters = 0.85f;
        private const float SummonDownOffsetMeters = 0.08f;
        private static readonly Color BackgroundColor = new(0.02f, 0.035f, 0.062f, 0.97f);
        private static readonly Color CardColor = new(0.04f, 0.085f, 0.128f, 0.94f);
        private static readonly Color ButtonColor = new(0.05f, 0.18f, 0.25f, 0.98f);
        private static readonly Color AccentColor = new(0.28f, 0.86f, 1f, 1f);
        private static readonly Color MutedTextColor = new(0.66f, 0.77f, 0.86f, 1f);
        private static Sprite _roundedSprite;
        private static Texture2D _roundedTexture;
        private IEarthVRInput _input;
        private EarthVRSettings _settings;
        private EarthVRRig _rig;
        private NavigationController _navigation;
        private WorldManipulationController _scaling;
        private CesiumEarthProvider _earth;
        private IGeocodingProvider _geocoder;
        private ComfortVignetteController _vignette;
        private ISystemKeyboardProvider _keyboardProvider;
        private Text _vignetteButtonLabel;
        private Canvas _canvas;
        private GameObject _mainPanel;
        private GameObject _searchPanel;
        private GameObject _keyboardPanel;
        private GameObject _resultsPanel;
        private Text _status;
        private Text _modeBadge;
        private Text _diagnostics;
        private Text _searchQueryText;
        private Text _searchStatus;
        private readonly List<GameObject> _resultButtons = new();
        private IReadOnlyList<GeographicPlace> _searchResults;
        private CancellationTokenSource _searchCancellation;
        private string _searchQuery = string.Empty;
        private Transform _selectionController;
        private float _smoothedDelta = 1f / 90f;
        private float _nextUiRefreshTime;
        private bool _showPerformance;
        private bool _rightTriggerWasHeld;
        private WorldSpaceButton _pointedButton;

        public void Initialize(
            IEarthVRInput input,
            EarthVRSettings settings,
            EarthVRRig rig,
            NavigationController navigation,
            WorldManipulationController scaling,
            CesiumEarthProvider earth,
            IGeocodingProvider geocoder,
            ComfortVignetteController vignette,
            ISystemKeyboardProvider keyboardProvider = null)
        {
            _input = input;
            _settings = settings;
            _rig = rig;
            _navigation = navigation;
            _scaling = scaling;
            _earth = earth;
            _geocoder = geocoder;
            _vignette = vignette;
            _keyboardProvider = keyboardProvider ?? new NullSystemKeyboardProvider();
            _selectionController = rig.RightController;

            // Parented to Navigation Space (not a hand): it travels with the
            // player's overall navigation, but does not chase real head/hand
            // motion, and does not move at all just from physically stepping
            // around a room-scale play area.
            var canvasObject = new GameObject("EarthVR Floating Menu", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(rig.NavigationSpace, false);
            canvasObject.transform.localScale = Vector3.one * 0.00085f;
            _canvas = canvasObject.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.worldCamera = rig.Camera;
            var rect = canvasObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(640f, 960f);

            _mainPanel = CreatePanel(canvasObject.transform, BackgroundColor);
            var header = CreateCard(_mainPanel.transform, "Header", new Vector2(0f, 400f), new Vector2(600f, 110f), new Color(0.028f, 0.11f, 0.165f, 0.98f));
            CreateCard(header.transform, "Accent", new Vector2(-286f, 0f), new Vector2(8f, 82f), AccentColor);
            var title = CreateText(header.transform, new Vector2(-96f, 16f), new Vector2(360f, 48f), 34, TextAnchor.MiddleLeft);
            title.text = "EARTH  VR";
            title.fontStyle = FontStyle.Bold;
            var subtitle = CreateText(header.transform, new Vector2(-96f, -24f), new Vector2(360f, 30f), 16, TextAnchor.MiddleLeft);
            subtitle.text = "EXPLORE  ·  SCALE  ·  DISCOVER";
            subtitle.color = MutedTextColor;
            _modeBadge = CreateText(header.transform, new Vector2(203f, 2f), new Vector2(150f, 46f), 18, TextAnchor.MiddleCenter);
            _modeBadge.color = AccentColor;
            _modeBadge.fontStyle = FontStyle.Bold;

            CreateCard(_mainPanel.transform, "Controls Card", new Vector2(0f, 245f), new Vector2(600f, 150f), CardColor);
            _status = CreateText(_mainPanel.transform, new Vector2(0f, 245f), new Vector2(550f, 122f), 19, TextAnchor.MiddleLeft);
            _status.color = new Color(0.92f, 0.96f, 1f, 1f);

            var buttonSize = new Vector2(580f, 60f);
            CreateButton(_mainPanel.transform, "RECENTER VIEW", new Vector2(0f, 126f), () => _navigation.ResetUpright(), buttonSize);
            CreateButton(_mainPanel.transform, "FLIGHT / GROUNDED MODE", new Vector2(0f, 54f), () => _navigation.State.Toggle(), buttonSize);
            var vignetteButton = CreateButton(_mainPanel.transform, "COMFORT VIGNETTE: OFF", new Vector2(0f, -18f), ToggleComfortVignette, buttonSize);
            _vignetteButtonLabel = vignetteButton.GetComponentInChildren<Text>();
            CreateButton(_mainPanel.transform, "PERFORMANCE OVERLAY", new Vector2(0f, -90f), () => _showPerformance = !_showPerformance, buttonSize);
            CreateButton(_mainPanel.transform, "SEARCH FOR A LOCATION", new Vector2(0f, -162f), ShowSearch, buttonSize);
            UpdateVignetteButtonLabel();

            CreateCard(_mainPanel.transform, "Diagnostics Card", new Vector2(0f, -330f), new Vector2(600f, 220f), new Color(0.028f, 0.06f, 0.092f, 0.96f));
            _diagnostics = CreateText(_mainPanel.transform, new Vector2(0f, -330f), new Vector2(550f, 188f), 16, TextAnchor.UpperLeft);
            _diagnostics.color = MutedTextColor;
            var hint = CreateText(_mainPanel.transform, new Vector2(0f, -462f), new Vector2(600f, 24f), 14, TextAnchor.MiddleCenter);
            hint.text = "LEFT VIEW BUTTON  ·  CLOSE MENU";
            hint.color = new Color(0.44f, 0.6f, 0.7f, 1f);

            CreateSearchPanel(canvasObject.transform);
            _canvas.gameObject.SetActive(false);
        }

        private void CreateSearchPanel(Transform parent)
        {
            _searchPanel = CreatePanel(parent, BackgroundColor);
            var searchTitle = CreateText(_searchPanel.transform, new Vector2(0f, 390f), new Vector2(560f, 50f), 30, TextAnchor.MiddleCenter);
            searchTitle.text = "GO TO LOCATION";
            searchTitle.fontStyle = FontStyle.Bold;
            searchTitle.color = AccentColor;
            CreateCard(_searchPanel.transform, "Search Field", new Vector2(0f, 326f), new Vector2(570f, 58f), CardColor);
            _searchQueryText = CreateText(_searchPanel.transform, new Vector2(0f, 326f), new Vector2(530f, 46f), 24, TextAnchor.MiddleLeft);
            _searchStatus = CreateText(_searchPanel.transform, new Vector2(0f, 268f), new Vector2(550f, 56f), 17, TextAnchor.UpperLeft);
            _searchStatus.color = MutedTextColor;

            _keyboardPanel = new GameObject("Search Keyboard", typeof(RectTransform));
            _keyboardPanel.transform.SetParent(_searchPanel.transform, false);
            CreateKeyRow("1234567890", 170f);
            CreateKeyRow("QWERTYUIOP", 116f);
            CreateKeyRow("ASDFGHJKL", 62f);
            CreateKeyRow("ZXCVBNM", 8f);
            CreateButton(_keyboardPanel.transform, "SPACE", new Vector2(-120f, -55f), () => AppendSearch(" "), new Vector2(190f, 52f));
            CreateButton(_keyboardPanel.transform, "BACK", new Vector2(65f, -55f), BackspaceSearch, new Vector2(135f, 52f));
            CreateButton(_keyboardPanel.transform, "SEARCH", new Vector2(220f, -55f), RunSearch, new Vector2(135f, 52f));

            _resultsPanel = new GameObject("Search Results", typeof(RectTransform));
            _resultsPanel.transform.SetParent(_searchPanel.transform, false);
            for (var i = 0; i < 4; i++)
            {
                var resultIndex = i;
                var resultButton = CreateButton(
                    _resultsPanel.transform,
                    "Result",
                    new Vector2(0f, 165f - i * 82f),
                    () => GoToSearchResult(resultIndex),
                    new Vector2(500f, 72f));
                resultButton.GetComponentInChildren<Text>().fontSize = 17;
                _resultButtons.Add(resultButton);
            }

            CreateText(_searchPanel.transform, new Vector2(0f, -260f), new Vector2(500f, 38f), 15, TextAnchor.MiddleCenter).text =
                "Search data © OpenStreetMap contributors";
            CreateButton(_searchPanel.transform, "Back to controls", new Vector2(0f, -340f), ShowMain, new Vector2(480f, 54f));
            _searchPanel.SetActive(false);
            UpdateSearchQueryText();
        }

        private void CreateKeyRow(string keys, float y)
        {
            const float spacing = 50f;
            var startX = -(keys.Length - 1) * spacing * 0.5f;
            for (var i = 0; i < keys.Length; i++)
            {
                var character = keys[i].ToString();
                CreateButton(
                    _keyboardPanel.transform,
                    character,
                    new Vector2(startX + i * spacing, y),
                    () => AppendSearch(character),
                    new Vector2(45f, 46f));
            }
        }

        private void ShowSearch()
        {
            _mainPanel.SetActive(false);
            _searchPanel.SetActive(true);
            if (_keyboardProvider != null && _keyboardProvider.IsAvailable)
            {
                _keyboardPanel.SetActive(false);
                _resultsPanel.SetActive(false);
                _searchStatus.text = "Enter a city, landmark, or address.";
                _keyboardProvider.Show(_searchQuery, OnSystemKeyboardTextChanged, OnSystemKeyboardDone);
            }
            else
            {
                ShowKeyboard();
            }
        }

        private void ShowMain()
        {
            _searchCancellation?.Cancel();
            _keyboardProvider?.Hide();
            _searchPanel.SetActive(false);
            _mainPanel.SetActive(true);
        }

        private void ShowKeyboard()
        {
            _keyboardPanel.SetActive(true);
            _resultsPanel.SetActive(false);
            _searchStatus.text = "Enter a city, landmark, or address, then press SEARCH.";
        }

        private void OnSystemKeyboardTextChanged(string text)
        {
            _searchQuery = text ?? string.Empty;
            UpdateSearchQueryText();
        }

        private void OnSystemKeyboardDone() => RunSearch();

        private void AppendSearch(string value)
        {
            if (_searchQuery.Length >= 64)
                return;
            _searchQuery += value;
            UpdateSearchQueryText();
        }

        private void BackspaceSearch()
        {
            if (_searchQuery.Length > 0)
                _searchQuery = _searchQuery.Substring(0, _searchQuery.Length - 1);
            UpdateSearchQueryText();
        }

        private void UpdateSearchQueryText()
        {
            if (_searchQueryText != null)
                _searchQueryText.text = string.IsNullOrWhiteSpace(_searchQuery) ? "Search: _" : "Search: " + _searchQuery + "_";
        }

        private async void RunSearch()
        {
            var query = _searchQuery.Trim();
            if (query.Length < 2)
            {
                _searchStatus.text = "Enter at least two characters.";
                return;
            }

            _searchCancellation?.Cancel();
            _searchCancellation?.Dispose();
            var cancellation = new CancellationTokenSource();
            _searchCancellation = cancellation;
            _searchStatus.text = "Searching…";

            try
            {
                var results = await _geocoder.SearchAsync(query, cancellation.Token);
                if (this == null || cancellation.IsCancellationRequested || _searchCancellation != cancellation)
                    return;
                _searchResults = results;

                if (_searchResults.Count == 0)
                {
                    _searchStatus.text = "No locations found. Try a broader name.";
                    return;
                }

                _keyboardPanel.SetActive(false);
                _resultsPanel.SetActive(true);
                _searchStatus.text = "Point and trigger to choose a destination:";
                for (var i = 0; i < _resultButtons.Count; i++)
                {
                    var visible = i < _searchResults.Count;
                    _resultButtons[i].SetActive(visible);
                    if (visible)
                        _resultButtons[i].GetComponentInChildren<Text>().text = Shorten(_searchResults[i].Name, 74);
                }
            }
            catch (System.OperationCanceledException)
            {
            }
            catch (System.Exception exception)
            {
                if (this != null)
                    _searchStatus.text = exception.Message;
            }
        }

        private void GoToSearchResult(int index)
        {
            if (_searchResults == null || index < 0 || index >= _searchResults.Count)
                return;

            var place = _searchResults[index];
            _scaling.SetUserScale(1f);
            _navigation.GoToLocation(place.Longitude, place.Latitude);
            ShowMain();
        }

        private void ToggleComfortVignette()
        {
            if (_vignette == null)
                return;
            _vignette.Enabled = !_vignette.Enabled;
            UpdateVignetteButtonLabel();
        }

        private void UpdateVignetteButtonLabel()
        {
            if (_vignetteButtonLabel == null)
                return;
            _vignetteButtonLabel.text = _vignette != null && _vignette.Enabled
                ? "COMFORT VIGNETTE: ON"
                : "COMFORT VIGNETTE: OFF";
        }

        private static string Shorten(string value, int maximumLength) =>
            string.IsNullOrEmpty(value) || value.Length <= maximumLength
                ? value
                : value.Substring(0, maximumLength - 1) + "…";

        private void Update()
        {
            // A script recompile while Play mode is active can leave this runtime-built
            // panel alive while its non-serialized service references are reset.
            if (_canvas == null || _input == null || _navigation == null || _scaling == null ||
                _earth == null || _selectionController == null || _rig == null)
                return;
            var rightTriggerHeld = _input.RightTriggerHeld;
            var rightTriggerPressed = rightTriggerHeld && !_rightTriggerWasHeld;
            _rightTriggerWasHeld = rightTriggerHeld;
            if (_input.OpenMenuPressed)
            {
                var opening = !_canvas.gameObject.activeSelf;
                _canvas.gameObject.SetActive(opening);
                if (opening)
                    SummonInFrontOfViewer();
            }
            if (!_canvas.gameObject.activeSelf)
            {
                SetPointedButton(null);
                return;
            }

            SetPointedButton(FindNearestPointedButton());
            if (rightTriggerPressed)
                _pointedButton?.Invoke();

            _smoothedDelta = Mathf.Lerp(_smoothedDelta, Time.unscaledDeltaTime, 0.05f);
            if (Time.unscaledTime < _nextUiRefreshTime)
                return;
            _nextUiRefreshTime = Time.unscaledTime + UiRefreshIntervalSeconds;

            _modeBadge.text = _navigation.State.Mode == MovementMode.Flight
                ? "●  FLIGHT"
                : "●  GROUNDED";
            _status.text =
                $"HEIGHT  {ScaleMath.ApproximateEyeHeight(_settings.realEyeHeightMeters, _scaling.UserScale):N1} m       " +
                $"SPEED  {_navigation.CurrentGeographicSpeed:N1} m/s\n" +
                "THUMBSTICK  Aim + move       RIGHT TRIGGER  Grab ground\n" +
                "GRIP  Rotate world              SHOULDER  Flight boost\n" +
                "SUN / MOON  Point + trigger to change time";

            if (!_showPerformance)
            {
                _diagnostics.text = _earth.StatusMessage + "\nPerformance details: off";
                return;
            }

            var llh = _navigation.LongitudeLatitudeHeight;
            var builder = new StringBuilder();
            builder.AppendLine($"FPS: {1f / Mathf.Max(0.0001f, _smoothedDelta):N0}");
            builder.AppendLine($"Frame: {_smoothedDelta * 1000f:N1} ms");
            builder.AppendLine($"Altitude: {_navigation.AltitudeMeters:N0} m ellipsoid");
            builder.AppendLine($"Lon/Lat: {llh.x:F5}, {llh.y:F5}");
            builder.AppendLine($"Managed+native allocated: {Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024):N0} MB");
            builder.AppendLine($"Tiles for view: {_earth.Tileset.ComputeLoadProgress():N0}%");
            builder.Append(_earth.StatusMessage);
            _diagnostics.text = builder.ToString();
        }

        /// <summary>Re-anchors the panel a fixed distance in front of wherever
        /// the head is looking, upright (yaw only), facing back toward the
        /// viewer. Called only when the menu transitions from closed to open,
        /// so it summons in front of the viewer without chasing the head
        /// afterward.</summary>
        private void SummonInFrontOfViewer()
        {
            var headTransform = _rig.Camera.transform;
            var headPosition = headTransform.position;
            var facing = Vector3.ProjectOnPlane(headTransform.forward, Vector3.up);
            if (facing.sqrMagnitude < 0.0001f)
                facing = Vector3.ProjectOnPlane(-headTransform.up, Vector3.up);
            if (facing.sqrMagnitude < 0.0001f)
                facing = Vector3.forward;
            facing.Normalize();

            _canvas.transform.position = headPosition + facing * SummonDistanceMeters + Vector3.down * SummonDownOffsetMeters;
            _canvas.transform.rotation = Quaternion.LookRotation(-facing, Vector3.up);
        }

        private WorldSpaceButton FindNearestPointedButton()
        {
            var hits = Physics.RaycastAll(
                _selectionController.position,
                _selectionController.forward,
                10f,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Collide);
            WorldSpaceButton nearestButton = null;
            var nearestDistance = float.PositiveInfinity;
            foreach (var hit in hits)
            {
                var button = hit.collider.GetComponent<WorldSpaceButton>();
                if (button == null || !button.gameObject.activeInHierarchy || hit.distance >= nearestDistance)
                    continue;
                nearestButton = button;
                nearestDistance = hit.distance;
            }
            return nearestButton;
        }

        private void SetPointedButton(WorldSpaceButton button)
        {
            if (_pointedButton == button)
                return;
            _pointedButton?.SetPointed(false);
            _pointedButton = button;
            _pointedButton?.SetPointed(true);
        }

        private void OnDestroy()
        {
            _searchCancellation?.Cancel();
            _searchCancellation?.Dispose();
        }

        private static GameObject CreatePanel(Transform parent, Color color)
        {
            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(Outline));
            panel.transform.SetParent(parent, false);
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            StyleRoundedImage(panel.GetComponent<Image>(), color);
            var outline = panel.GetComponent<Outline>();
            outline.effectColor = new Color(0.22f, 0.62f, 0.78f, 0.4f);
            outline.effectDistance = new Vector2(2.5f, -2.5f);
            return panel;
        }

        private static GameObject CreateCard(
            Transform parent,
            string name,
            Vector2 position,
            Vector2 size,
            Color color)
        {
            var card = new GameObject(name, typeof(RectTransform), typeof(Image));
            card.transform.SetParent(parent, false);
            var rect = card.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            StyleRoundedImage(card.GetComponent<Image>(), color);
            return card;
        }

        private static Text CreateText(Transform parent, Vector2 position, Vector2 size, int fontSize, TextAnchor alignment)
        {
            var gameObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            gameObject.transform.SetParent(parent, false);
            var rect = gameObject.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var text = gameObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static GameObject CreateButton(Transform parent, string label, Vector2 position, UnityEngine.Events.UnityAction callback, Vector2? size = null)
        {
            var gameObject = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(BoxCollider), typeof(WorldSpaceButton));
            gameObject.transform.SetParent(parent, false);
            var rect = gameObject.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.sizeDelta = size ?? new Vector2(450f, 48f);
            var image = gameObject.GetComponent<Image>();
            StyleRoundedImage(image, ButtonColor);
            var button = gameObject.GetComponent<Button>();
            button.onClick.AddListener(callback);
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = ButtonColor;
            colors.highlightedColor = new Color(0.10f, 0.34f, 0.45f, 1f);
            colors.pressedColor = new Color(0.20f, 0.68f, 0.82f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.06f, 0.08f, 0.10f, 0.6f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            var collider = gameObject.GetComponent<BoxCollider>();
            // A small invisible margin beyond the visible button graphic —
            // pointer aim at VR distances is imprecise, so the hit target is
            // slightly bigger than what's drawn. Kept modest because several
            // buttons (keyboard keys especially) sit close together; anything
            // larger starts overlapping neighbouring hit zones.
            collider.size = new Vector3(rect.sizeDelta.x * 1.04f, rect.sizeDelta.y * 1.08f, 20f);
            gameObject.GetComponent<WorldSpaceButton>().Configure(button);
            var text = CreateText(gameObject.transform, Vector2.zero, rect.sizeDelta - new Vector2(30f, 0f), 20, TextAnchor.MiddleCenter);
            text.text = label;
            text.fontStyle = FontStyle.Bold;
            return gameObject;
        }

        private static void StyleRoundedImage(Image image, Color color)
        {
            image.sprite = GetRoundedSprite();
            image.type = Image.Type.Sliced;
            image.color = color;
        }

        private static Sprite GetRoundedSprite()
        {
            if (_roundedSprite != null)
                return _roundedSprite;

            const int size = 64;
            const float radius = 15f;
            _roundedTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "EarthVR Rounded UI Texture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var nearestX = Mathf.Clamp(x, radius, size - 1f - radius);
                    var nearestY = Mathf.Clamp(y, radius, size - 1f - radius);
                    var distance = Vector2.Distance(new Vector2(x, y), new Vector2(nearestX, nearestY));
                    var alpha = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(radius + 0.5f - distance));
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }
            _roundedTexture.SetPixels32(pixels);
            _roundedTexture.Apply(false, true);
            _roundedSprite = Sprite.Create(
                _roundedTexture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(radius, radius, radius, radius));
            _roundedSprite.name = "EarthVR Rounded UI Sprite";
            _roundedSprite.hideFlags = HideFlags.HideAndDontSave;
            return _roundedSprite;
        }
    }
}
