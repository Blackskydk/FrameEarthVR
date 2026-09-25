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
    public sealed class EarthVRWristMenu : MonoBehaviour
    {
        private const float UiRefreshIntervalSeconds = 0.25f;
        private static readonly Color BackgroundColor = new(0.018f, 0.032f, 0.058f, 0.97f);
        private static readonly Color CardColor = new(0.035f, 0.075f, 0.115f, 0.94f);
        private static readonly Color ButtonColor = new(0.045f, 0.16f, 0.23f, 0.98f);
        private static readonly Color AccentColor = new(0.22f, 0.82f, 1f, 1f);
        private static readonly Color MutedTextColor = new(0.64f, 0.75f, 0.84f, 1f);
        private static Sprite _roundedSprite;
        private static Texture2D _roundedTexture;
        private IEarthVRInput _input;
        private EarthVRSettings _settings;
        private NavigationController _navigation;
        private WorldManipulationController _scaling;
        private CesiumEarthProvider _earth;
        private IGeocodingProvider _geocoder;
        private ComfortVignetteController _vignette;
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
            ComfortVignetteController vignette)
        {
            _input = input;
            _settings = settings;
            _navigation = navigation;
            _scaling = scaling;
            _earth = earth;
            _geocoder = geocoder;
            _vignette = vignette;
            _selectionController = rig.RightController;

            var canvasObject = new GameObject("EarthVR Wrist Menu", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(rig.LeftController, false);
            canvasObject.transform.localPosition = new Vector3(0.095f, 0.035f, 0.125f);
            canvasObject.transform.localRotation = Quaternion.Euler(58f, 0f, 0f);
            canvasObject.transform.localScale = Vector3.one * 0.00058f;
            _canvas = canvasObject.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.worldCamera = rig.Camera;
            var rect = canvasObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(600f, 820f);

            _mainPanel = CreatePanel(canvasObject.transform, BackgroundColor);
            var header = CreateCard(_mainPanel.transform, "Header", new Vector2(0f, 342f), new Vector2(548f, 104f), new Color(0.025f, 0.10f, 0.15f, 0.98f));
            CreateCard(header.transform, "Accent", new Vector2(-264f, 0f), new Vector2(7f, 72f), AccentColor);
            var title = CreateText(header.transform, new Vector2(-92f, 13f), new Vector2(330f, 42f), 31, TextAnchor.MiddleLeft);
            title.text = "EARTH  VR";
            title.fontStyle = FontStyle.Bold;
            var subtitle = CreateText(header.transform, new Vector2(-92f, -22f), new Vector2(330f, 28f), 15, TextAnchor.MiddleLeft);
            subtitle.text = "EXPLORE  ·  SCALE  ·  DISCOVER";
            subtitle.color = MutedTextColor;
            _modeBadge = CreateText(header.transform, new Vector2(184f, 2f), new Vector2(135f, 42f), 17, TextAnchor.MiddleCenter);
            _modeBadge.color = AccentColor;
            _modeBadge.fontStyle = FontStyle.Bold;

            CreateCard(_mainPanel.transform, "Controls Card", new Vector2(0f, 214f), new Vector2(548f, 128f), CardColor);
            _status = CreateText(_mainPanel.transform, new Vector2(0f, 214f), new Vector2(500f, 102f), 15, TextAnchor.MiddleLeft);
            _status.color = new Color(0.9f, 0.95f, 1f, 1f);

            CreateButton(_mainPanel.transform, "RECENTER VIEW", new Vector2(0f, 116f), () => _navigation.ResetUpright(), new Vector2(520f, 42f));
            CreateButton(_mainPanel.transform, "FLIGHT / GROUNDED MODE", new Vector2(0f, 68f), () => _navigation.State.Toggle(), new Vector2(520f, 42f));
            var vignetteButton = CreateButton(_mainPanel.transform, "COMFORT VIGNETTE: OFF", new Vector2(0f, 20f), ToggleComfortVignette, new Vector2(520f, 42f));
            _vignetteButtonLabel = vignetteButton.GetComponentInChildren<Text>();
            CreateButton(_mainPanel.transform, "PERFORMANCE OVERLAY", new Vector2(0f, -28f), () => _showPerformance = !_showPerformance, new Vector2(520f, 42f));
            CreateButton(_mainPanel.transform, "SEARCH FOR A LOCATION", new Vector2(0f, -76f), ShowSearch, new Vector2(520f, 42f));
            UpdateVignetteButtonLabel();

            CreateCard(_mainPanel.transform, "Diagnostics Card", new Vector2(0f, -245f), new Vector2(548f, 250f), new Color(0.025f, 0.055f, 0.085f, 0.96f));
            _diagnostics = CreateText(_mainPanel.transform, new Vector2(0f, -245f), new Vector2(500f, 216f), 16, TextAnchor.UpperLeft);
            _diagnostics.color = MutedTextColor;
            var hint = CreateText(_mainPanel.transform, new Vector2(0f, -382f), new Vector2(520f, 24f), 14, TextAnchor.MiddleCenter);
            hint.text = "LEFT VIEW BUTTON  ·  CLOSE MENU";
            hint.color = new Color(0.42f, 0.58f, 0.68f, 1f);

            CreateSearchPanel(canvasObject.transform);
            _canvas.gameObject.SetActive(false);
        }

        private void CreateSearchPanel(Transform parent)
        {
            _searchPanel = CreatePanel(parent, BackgroundColor);
            var searchTitle = CreateText(_searchPanel.transform, new Vector2(0f, 354f), new Vector2(520f, 48f), 30, TextAnchor.MiddleCenter);
            searchTitle.text = "GO TO LOCATION";
            searchTitle.fontStyle = FontStyle.Bold;
            searchTitle.color = AccentColor;
            CreateCard(_searchPanel.transform, "Search Field", new Vector2(0f, 294f), new Vector2(530f, 56f), CardColor);
            _searchQueryText = CreateText(_searchPanel.transform, new Vector2(0f, 294f), new Vector2(490f, 44f), 23, TextAnchor.MiddleLeft);
            _searchStatus = CreateText(_searchPanel.transform, new Vector2(0f, 240f), new Vector2(510f, 54f), 17, TextAnchor.UpperLeft);
            _searchStatus.color = MutedTextColor;

            _keyboardPanel = new GameObject("Search Keyboard", typeof(RectTransform));
            _keyboardPanel.transform.SetParent(_searchPanel.transform, false);
            CreateKeyRow("1234567890", 145f);
            CreateKeyRow("QWERTYUIOP", 95f);
            CreateKeyRow("ASDFGHJKL", 45f);
            CreateKeyRow("ZXCVBNM", -5f);
            CreateButton(_keyboardPanel.transform, "SPACE", new Vector2(-105f, -67f), () => AppendSearch(" "), new Vector2(175f, 46f));
            CreateButton(_keyboardPanel.transform, "BACK", new Vector2(56f, -67f), BackspaceSearch, new Vector2(125f, 46f));
            CreateButton(_keyboardPanel.transform, "SEARCH", new Vector2(190f, -67f), RunSearch, new Vector2(125f, 46f));

            _resultsPanel = new GameObject("Search Results", typeof(RectTransform));
            _resultsPanel.transform.SetParent(_searchPanel.transform, false);
            for (var i = 0; i < 4; i++)
            {
                var resultIndex = i;
                var resultButton = CreateButton(
                    _resultsPanel.transform,
                    "Result",
                    new Vector2(0f, 145f - i * 75f),
                    () => GoToSearchResult(resultIndex),
                    new Vector2(470f, 66f));
                resultButton.GetComponentInChildren<Text>().fontSize = 16;
                _resultButtons.Add(resultButton);
            }

            CreateText(_searchPanel.transform, new Vector2(0f, -250f), new Vector2(470f, 38f), 16, TextAnchor.MiddleCenter).text =
                "Search data © OpenStreetMap contributors";
            CreateButton(_searchPanel.transform, "Back to controls", new Vector2(0f, -320f), ShowMain, new Vector2(450f, 48f));
            _searchPanel.SetActive(false);
            UpdateSearchQueryText();
        }

        private void CreateKeyRow(string keys, float y)
        {
            const float spacing = 46f;
            var startX = -(keys.Length - 1) * spacing * 0.5f;
            for (var i = 0; i < keys.Length; i++)
            {
                var character = keys[i].ToString();
                CreateButton(
                    _keyboardPanel.transform,
                    character,
                    new Vector2(startX + i * spacing, y),
                    () => AppendSearch(character),
                    new Vector2(41f, 42f));
            }
        }

        private void ShowSearch()
        {
            _mainPanel.SetActive(false);
            _searchPanel.SetActive(true);
            ShowKeyboard();
        }

        private void ShowMain()
        {
            _searchCancellation?.Cancel();
            _searchPanel.SetActive(false);
            _mainPanel.SetActive(true);
        }

        private void ShowKeyboard()
        {
            _keyboardPanel.SetActive(true);
            _resultsPanel.SetActive(false);
            _searchStatus.text = "Enter a city, landmark, or address, then press SEARCH.";
        }

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
                _earth == null || _selectionController == null)
                return;
            var rightTriggerHeld = _input.RightTriggerHeld;
            var rightTriggerPressed = rightTriggerHeld && !_rightTriggerWasHeld;
            _rightTriggerWasHeld = rightTriggerHeld;
            if (_input.OpenMenuPressed)
                _canvas.gameObject.SetActive(!_canvas.gameObject.activeSelf);
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
                "THUMBSTICK  Aim + move       TRIGGER  Pull Earth\n" +
                "BOTH TRIGGERS  Pan / rotate / zoom together\n" +
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
            outline.effectColor = new Color(0.18f, 0.55f, 0.72f, 0.35f);
            outline.effectDistance = new Vector2(2f, -2f);
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
            colors.highlightedColor = new Color(0.08f, 0.30f, 0.40f, 1f);
            colors.pressedColor = new Color(0.16f, 0.62f, 0.76f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.06f, 0.08f, 0.10f, 0.6f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            var collider = gameObject.GetComponent<BoxCollider>();
            collider.size = new Vector3(rect.sizeDelta.x, rect.sizeDelta.y, 12f);
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

            const int size = 32;
            const float radius = 8f;
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
