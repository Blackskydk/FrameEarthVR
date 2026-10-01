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
    /// The left-hand menu. The left View/pause button shows or hides it together
    /// with the destination globe. The panel rides beside the globe, turns to
    /// face the viewer, and closes itself whenever travel begins.
    /// </summary>
    public sealed class EarthVRWristMenu : MonoBehaviour
    {
        private const float UiRefreshIntervalSeconds = 0.25f;
        // Roughly the angular size the old floating panel had at 0.85 m, for a
        // hand held about 0.45 m from the eyes.
        private const float HandMenuScale = 0.0004f;
        private const float MenuPanelOffsetPixels = 1050f;
        private static readonly Color BackgroundColor = new(0.02f, 0.035f, 0.062f, 0.97f);
        private static readonly Color CardColor = new(0.04f, 0.085f, 0.128f, 0.94f);
        private static readonly Color ButtonColor = new(0.05f, 0.18f, 0.25f, 0.98f);
        private static readonly Color AccentColor = new(0.28f, 0.86f, 1f, 1f);
        private static readonly Color MutedTextColor = new(0.66f, 0.77f, 0.86f, 1f);
        private static readonly Color FlightColor = new(0.055f, 0.37f, 0.48f, 1f);
        private static readonly Color GroundedColor = new(0.38f, 0.25f, 0.055f, 1f);
        private static readonly Color EnabledColor = new(0.08f, 0.42f, 0.32f, 1f);
        private static readonly Color DisabledColor = new(0.055f, 0.10f, 0.14f, 1f);
        private static Sprite _roundedSprite;
        private static Texture2D _roundedTexture;
        private static Sprite _circleSprite;
        private static Texture2D _circleTexture;
        private IEarthVRInput _input;
        private EarthVRSettings _settings;
        private EarthVRRig _rig;
        private NavigationController _navigation;
        private GeographicOriginRebaser _originRebaser;
        private WorldManipulationController _scaling;
        private CesiumEarthProvider _earth;
        private IGeocodingProvider _geocoder;
        private IBookmarkProvider _places;
        private LoadingAwareArrivalController _arrival;
        private MiniatureGlobePicker _globePicker;
        private GlobeOverviewController _overview;
        private CarModeController _carMode;
        private ComfortVignetteController _vignette;
        private ISystemKeyboardProvider _keyboardProvider;
        private Canvas _canvas;
        private bool _isOpen;
        private WorldSpaceButton _modeButton;
        private Text _modeButtonLabel;
        private WorldSpaceButton _vignetteButton;
        private Text _vignetteButtonLabel;
        private WorldSpaceButton _favoriteButton;
        private WorldSpaceButton _menuButton;
        private Text _menuButtonLabel;
        private Text _favoriteButtonLabel;
        private WorldSpaceButton _carModeButton;
        private Text _carModeButtonLabel;
        private MovementMode _displayedMode;
        private bool _displayedVignette;
        private GameObject _mainPanel;
        private GameObject _searchPanel;
        private GameObject _placesPanel;
        private GameObject _keyboardPanel;
        private GameObject _resultsPanel;
        private GameObject _suggestionsPanel;
        private Text _status;
        private Text _modeBadge;
        private Text _diagnostics;
        private Text _searchQueryText;
        private Text _searchStatus;
        private readonly List<GameObject> _resultButtons = new();
        private readonly List<GameObject> _suggestionButtons = new();
        private readonly List<GameObject> _placeButtons = new();
        private readonly List<GameObject> _removePlaceButtons = new();
        private Text _placesModeButtonLabel;
        private GameObject _placePageButton;
        private Text _placePageButtonLabel;
        private Text _placesStatus;
        private bool _showingRecents;
        private int _placePage;
        private IReadOnlyList<GeographicPlace> _searchResults;
        private IReadOnlyList<OfflinePlaceCatalog.PlaceSuggestion> _searchSuggestions;
        private CancellationTokenSource _searchCancellation;
        private string _searchQuery = string.Empty;
        private Transform _selectionController;
        private float _smoothedDelta = 1f / 90f;
        private float _nextUiRefreshTime;
        private bool _showPerformance;
        private bool _rightTriggerWasHeld;
        private WorldSpaceButton _pointedButton;
        private CredentialSetupPanel _credentialSetup;
        private ReleaseUpdateChecker _updates;
        private WorldSpaceButton _updateButton;
        private Text _updateButtonLabel;
        private Text _updateBadge;
        public void SetCredentialSetup(CredentialSetupPanel setup) => _credentialSetup = setup;
        public void SetUpdateChecker(ReleaseUpdateChecker updates)
        {
            _updates = updates;
            _updates.Changed += RefreshUpdateStatus;
            RefreshUpdateStatus();
        }

        private void RefreshUpdateStatus()
        {
            if (_updates == null || _updateButtonLabel == null) return;
            _updateButtonLabel.text = _updates.IsUpdating ? "UPDATE IN PROGRESS…" : _updates.IsChecking ? "CHECKING FOR UPDATES…" : _updates.AvailableRelease != null
                ? "DOWNLOAD & APPLY UPDATE" : "CHECK FOR UPDATES";
            _updateButton.SetNormalColor(_updates.AvailableRelease != null ? GroundedColor : ButtonColor);
            _updateBadge.text = _updates.Status;
            _updateBadge.color = _updates.AvailableRelease != null ? new Color(1f, 0.85f, 0.12f) : MutedTextColor;
        }

        public void Initialize(
            IEarthVRInput input,
            EarthVRSettings settings,
            EarthVRRig rig,
            NavigationController navigation,
            GeographicOriginRebaser originRebaser,
            WorldManipulationController scaling,
            CesiumEarthProvider earth,
            IGeocodingProvider geocoder,
            IBookmarkProvider places,
            LoadingAwareArrivalController arrival,
            MiniatureGlobePicker globePicker,
            GlobeOverviewController overview,
            CarModeController carMode,
            ComfortVignetteController vignette,
            ISystemKeyboardProvider keyboardProvider = null)
        {
            _input = input;
            _settings = settings;
            _rig = rig;
            _navigation = navigation;
            _originRebaser = originRebaser;
            _scaling = scaling;
            _earth = earth;
            _geocoder = geocoder;
            _places = places;
            _arrival = arrival;
            _globePicker = globePicker;
            _overview = overview;
            _carMode = carMode;
            _vignette = vignette;
            _keyboardProvider = keyboardProvider ?? new NullSystemKeyboardProvider();
            _selectionController = rig.RightController;

            var canvasObject = new GameObject("EarthVR Globe-anchored Menu", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(globePicker.InterfaceAnchor ?? rig.LeftController, false);
            canvasObject.transform.localPosition = Vector3.zero;
            canvasObject.transform.localRotation = Quaternion.identity;
            canvasObject.transform.localScale = Vector3.one * HandMenuScale;
            _canvas = canvasObject.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.worldCamera = rig.Camera;
            var rect = canvasObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(2200f, 960f);

            _mainPanel = CreatePanel(canvasObject.transform, BackgroundColor);
            ConfigureMenuPanel(_mainPanel);
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

            var buttonSize = new Vector2(580f, 46f);
            CreateButton(_mainPanel.transform, "RECENTER VIEW", new Vector2(0f, 144f), () => _navigation.ResetUpright(), buttonSize);
            CreateButton(_mainPanel.transform, "PERFORMANCE OVERLAY", new Vector2(0f, 90f), () => _showPerformance = !_showPerformance, buttonSize);
            CreateButton(_mainPanel.transform, "SEARCH FOR A LOCATION", new Vector2(0f, 36f), ShowSearch, buttonSize);
            CreateButton(_mainPanel.transform, "FAVORITES & RECENT PLACES", new Vector2(0f, -18f), ShowPlaces, buttonSize);
            _carModeButton = CreateStateButton(
                _mainPanel.transform,
                "CAR MODE",
                new Vector2(0f, -72f),
                () => _carMode?.Toggle(),
                buttonSize,
                out _carModeButtonLabel);

            CreateGlobeControlRing(canvasObject.transform);
            CreateButton(_mainPanel.transform, "YOUR CESIUM ACCOUNT", new Vector2(0f, -126f),
                () => { SetOpen(false); _credentialSetup?.Open(); }, buttonSize);
            _updateButton = CreateStateButton(_mainPanel.transform, "CHECK FOR UPDATES", new Vector2(0f, -180f),
                () => _updates?.OpenAvailableRelease(), buttonSize, out _updateButtonLabel);
            _updateBadge = CreateText(_mainPanel.transform, new Vector2(0f, -213f), new Vector2(600f, 20f), 14, TextAnchor.MiddleCenter);

            CreateCard(_mainPanel.transform, "Diagnostics Card", new Vector2(0f, -335f), new Vector2(600f, 210f), new Color(0.028f, 0.06f, 0.092f, 0.96f));
            _diagnostics = CreateText(_mainPanel.transform, new Vector2(0f, -330f), new Vector2(550f, 188f), 16, TextAnchor.UpperLeft);
            _diagnostics.color = MutedTextColor;
            var hint = CreateText(_mainPanel.transform, new Vector2(0f, -462f), new Vector2(600f, 24f), 14, TextAnchor.MiddleCenter);
            hint.text = "LEFT VIEW BUTTON  ·  CLOSE MENU";
            hint.color = new Color(0.44f, 0.6f, 0.7f, 1f);

            CreateSearchPanel(canvasObject.transform);
            CreatePlacesPanel(canvasObject.transform);
            _places.Changed += RefreshPlaces;
            SetOpen(false);
            _canvas.gameObject.SetActive(false);
        }

        /// <summary>Opens or closes the menu and the destination globe together.
        /// The menu always reopens on its main page.</summary>
        private void SetOpen(bool open)
        {
            _isOpen = open;
            _globePicker?.SetSummoned(open);
            _menuButton?.SetNormalColor(open ? EnabledColor : DisabledColor);
            if (_menuButtonLabel != null)
                _menuButtonLabel.text = open ? "CLOSE\nMENU" : "OPEN\nMENU";
            if (open)
            {
                ShowMain();
                RefreshStateButtons(true);
                PlaceBesideGlobe();
                _nextUiRefreshTime = 0f;
                return;
            }

            _searchCancellation?.Cancel();
            _keyboardProvider?.Hide();
            _mainPanel.SetActive(false);
            _searchPanel.SetActive(false);
            _placesPanel.SetActive(false);
            SetPointedButton(null);
        }

        private void ToggleMode()
        {
            // A Flight/Grounded perspective transition suspends navigation and
            // owns scale until it settles; ignore presses until then.
            if (_navigation.NavigationEnabled)
                _navigation.State.Toggle();
        }

        private void ToggleVignette()
        {
            if (_vignette == null)
                return;
            _vignette.Enabled = !_vignette.Enabled;
            RefreshStateButtons(true);
        }

        private void RefreshStateButtons(bool force)
        {
            var mode = _navigation.State.Mode;
            if (force || mode != _displayedMode)
            {
                _displayedMode = mode;
                if (_modeButtonLabel != null)
                    _modeButtonLabel.text = mode switch
                    {
                        MovementMode.Flight => "FLIGHT",
                        MovementMode.Car => "CAR",
                        _ => "GROUNDED"
                    };
                _modeButton?.SetNormalColor(mode switch
                {
                    MovementMode.Flight => FlightColor,
                    MovementMode.Car => EnabledColor,
                    _ => GroundedColor
                });
                if (_carModeButtonLabel != null)
                    _carModeButtonLabel.text = mode == MovementMode.Car
                        ? "EXIT CAR MODE"
                        : "ENTER CAR MODE";
                _carModeButton?.SetNormalColor(mode == MovementMode.Car ? EnabledColor : DisabledColor);
            }

            var vignetteEnabled = _vignette != null && _vignette.Enabled;
            if (force || vignetteEnabled != _displayedVignette)
            {
                _displayedVignette = vignetteEnabled;
                if (_vignetteButtonLabel != null)
                    _vignetteButtonLabel.text = vignetteEnabled ? "VIGNETTE\nON" : "VIGNETTE\nOFF";
                _vignetteButton?.SetNormalColor(vignetteEnabled ? EnabledColor : DisabledColor);
            }

            RefreshFavoriteButton();
        }

        private void CreateGlobeControlRing(Transform parent)
        {
            var diameter = new Vector2(158f, 158f);
            _modeButton = CreateRoundStateButton(
                parent,
                "Flight or grounded",
                new Vector2(550f, 270f),
                ToggleMode,
                diameter,
                out _modeButtonLabel);
            _vignetteButton = CreateRoundStateButton(
                parent,
                "Comfort vignette",
                new Vector2(550f, 90f),
                ToggleVignette,
                diameter,
                out _vignetteButtonLabel);
            _favoriteButton = CreateRoundStateButton(
                parent,
                "Favorite current place",
                new Vector2(550f, -90f),
                ToggleFavoriteCurrentView,
                diameter,
                out _favoriteButtonLabel);
            _menuButton = CreateRoundStateButton(parent, "OPEN\nMENU",
                new Vector2(550f, -270f), () => SetOpen(!_isOpen), diameter,
                out _menuButtonLabel);
        }

        private void ToggleFavoriteCurrentView()
        {
            var current = _arrival.CaptureCurrentPlace();
            var favorite = FindMatchingFavorite(current);
            if (favorite != null)
                _places.RemoveBookmark(favorite.id);
            else
                _places.SaveBookmark(current);
            RefreshFavoriteButton();
        }

        private SavedPlace FindMatchingFavorite(SavedPlace place)
            => FindMatchingFavorite(place.name, place.longitude, place.latitude);

        private SavedPlace FindMatchingFavorite(string name, double longitude, double latitude)
        {
            if (_places == null)
                return null;
            for (var i = 0; i < _places.Bookmarks.Count; i++)
            {
                if (PlaceLibraryRules.IsSameDestination(
                        _places.Bookmarks[i],
                        name,
                        longitude,
                        latitude))
                    return _places.Bookmarks[i];
            }
            return null;
        }

        private void RefreshFavoriteButton()
        {
            if (_favoriteButton == null || _arrival == null)
                return;
            var llh = _navigation.LongitudeLatitudeHeight;
            var isFavorite = FindMatchingFavorite(
                _navigation.CurrentPlaceName,
                llh.x,
                llh.y) != null;
            _favoriteButtonLabel.text = isFavorite ? "★\nFAVORITE" : "☆\nFAVORITE";
            _favoriteButton.SetNormalColor(isFavorite ? EnabledColor : DisabledColor);
        }

        private void CreatePlacesPanel(Transform parent)
        {
            _placesPanel = CreatePanel(parent, BackgroundColor);
            ConfigureMenuPanel(_placesPanel);
            var title = CreateText(_placesPanel.transform, new Vector2(0f, 402f), new Vector2(560f, 54f), 30, TextAnchor.MiddleCenter);
            title.text = "FAVORITES";
            title.fontStyle = FontStyle.Bold;
            title.color = AccentColor;

            CreateButton(_placesPanel.transform, "FAVORITE THIS VIEW", new Vector2(0f, 332f), BookmarkCurrentView, new Vector2(540f, 58f));
            var modeButton = CreateButton(_placesPanel.transform, "SHOW RECENT PLACES", new Vector2(0f, 264f), TogglePlacesMode, new Vector2(540f, 52f));
            _placesModeButtonLabel = modeButton.GetComponentInChildren<Text>();
            _placesStatus = CreateText(_placesPanel.transform, new Vector2(0f, 214f), new Vector2(540f, 40f), 16, TextAnchor.MiddleCenter);
            _placesStatus.color = MutedTextColor;

            for (var i = 0; i < 5; i++)
            {
                var placeIndex = i;
                var y = 148f - i * 78f;
                var placeButton = CreateButton(
                    _placesPanel.transform,
                    "Place",
                    new Vector2(-35f, y),
                    () => TravelToPlace(placeIndex),
                    new Vector2(470f, 64f));
                placeButton.GetComponentInChildren<Text>().fontSize = 17;
                _placeButtons.Add(placeButton);

                var removeButton = CreateButton(
                    _placesPanel.transform,
                    "×",
                    new Vector2(245f, y),
                    () => RemoveBookmark(placeIndex),
                    new Vector2(64f, 64f));
                removeButton.GetComponentInChildren<Text>().fontSize = 28;
                _removePlaceButtons.Add(removeButton);
            }

            _placePageButton = CreateButton(_placesPanel.transform, "NEXT PAGE", new Vector2(0f, -236f), CyclePlacePage, new Vector2(400f, 48f));
            _placePageButtonLabel = _placePageButton.GetComponentInChildren<Text>();
            CreateButton(_placesPanel.transform, "Back to controls", new Vector2(0f, -316f), ShowMain, new Vector2(480f, 54f));
            var hint = CreateText(_placesPanel.transform, new Vector2(0f, -405f), new Vector2(570f, 80f), 15, TextAnchor.MiddleCenter);
            hint.text = "The map globe sits above your left hand.\nPoint with the right hand + trigger twice to travel.";
            hint.color = MutedTextColor;
            _placesPanel.SetActive(false);
        }

        private void CreateSearchPanel(Transform parent)
        {
            _searchPanel = CreatePanel(parent, BackgroundColor);
            ConfigureMenuPanel(_searchPanel);
            var searchTitle = CreateText(_searchPanel.transform, new Vector2(0f, 390f), new Vector2(560f, 50f), 30, TextAnchor.MiddleCenter);
            searchTitle.text = "GO TO LOCATION";
            searchTitle.fontStyle = FontStyle.Bold;
            searchTitle.color = AccentColor;
            CreateCard(_searchPanel.transform, "Search Field", new Vector2(0f, 326f), new Vector2(570f, 58f), CardColor);
            _searchQueryText = CreateText(_searchPanel.transform, new Vector2(0f, 326f), new Vector2(530f, 46f), 24, TextAnchor.MiddleLeft);
            _searchStatus = CreateText(_searchPanel.transform, new Vector2(0f, 268f), new Vector2(550f, 56f), 17, TextAnchor.UpperLeft);
            _searchStatus.color = MutedTextColor;

            _suggestionsPanel = new GameObject("Offline Suggestions", typeof(RectTransform));
            _suggestionsPanel.transform.SetParent(_searchPanel.transform, false);
            for (var i = 0; i < 3; i++)
            {
                var suggestionIndex = i;
                var suggestionButton = CreateButton(
                    _suggestionsPanel.transform,
                    "Suggestion",
                    new Vector2(-185f + i * 185f, 215f),
                    () => GoToSearchSuggestion(suggestionIndex),
                    new Vector2(174f, 42f));
                suggestionButton.GetComponentInChildren<Text>().fontSize = 13;
                _suggestionButtons.Add(suggestionButton);
            }

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
            _placesPanel.SetActive(false);
            _searchPanel.SetActive(true);
            if (_keyboardProvider != null && _keyboardProvider.IsAvailable)
            {
                _keyboardPanel.SetActive(false);
                _resultsPanel.SetActive(false);
                _searchStatus.text = "Enter a city, landmark, or address.";
                RefreshSearchSuggestions();
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
            _placesPanel.SetActive(false);
            _mainPanel.SetActive(true);
        }

        private void ShowPlaces()
        {
            _searchCancellation?.Cancel();
            _keyboardProvider?.Hide();
            _mainPanel.SetActive(false);
            _searchPanel.SetActive(false);
            _placesPanel.SetActive(true);
            RefreshPlaces();
        }

        private void TogglePlacesMode()
        {
            _showingRecents = !_showingRecents;
            _placePage = 0;
            RefreshPlaces();
        }

        private void CyclePlacePage()
        {
            var collection = _showingRecents ? _places.RecentPlaces : _places.Bookmarks;
            var pageCount = Mathf.Max(1, Mathf.CeilToInt(collection.Count / (float)_placeButtons.Count));
            _placePage = (_placePage + 1) % pageCount;
            RefreshPlaces();
        }

        private void BookmarkCurrentView()
        {
            var place = _arrival.CaptureCurrentPlace();
            _places.SaveBookmark(place);
            _placesStatus.text = $"Favorited {Shorten(place.name, 48)}";
        }

        private void TravelToPlace(int index)
        {
            var collection = _showingRecents ? _places.RecentPlaces : _places.Bookmarks;
            index += _placePage * _placeButtons.Count;
            if (index < 0 || index >= collection.Count)
                return;
            var destination = collection[index].Copy();
            SetOpen(false);
            _arrival.TravelTo(destination);
        }

        private void RemoveBookmark(int index)
        {
            index += _placePage * _placeButtons.Count;
            if (_showingRecents || index < 0 || index >= _places.Bookmarks.Count)
                return;
            _places.RemoveBookmark(_places.Bookmarks[index].id);
        }

        private void RefreshPlaces()
        {
            RefreshFavoriteButton();
            if (_placesPanel == null)
                return;
            var collection = _showingRecents ? _places.RecentPlaces : _places.Bookmarks;
            var pageCount = Mathf.Max(1, Mathf.CeilToInt(collection.Count / (float)_placeButtons.Count));
            _placePage = Mathf.Clamp(_placePage, 0, pageCount - 1);
            var firstIndex = _placePage * _placeButtons.Count;
            _placesModeButtonLabel.text = _showingRecents ? "SHOW FAVORITES" : "SHOW RECENT PLACES";
            _placesStatus.text = collection.Count == 0
                ? (_showingRecents ? "No recently visited places yet." : "No favorites yet.")
                : (_showingRecents ? "Most recently visited first" : "Favorite viewpoints");
            _placePageButton.SetActive(pageCount > 1);
            _placePageButtonLabel.text = $"NEXT PAGE  ·  {_placePage + 1}/{pageCount}";
            for (var i = 0; i < _placeButtons.Count; i++)
            {
                var collectionIndex = firstIndex + i;
                var visible = collectionIndex < collection.Count;
                _placeButtons[i].SetActive(visible);
                _removePlaceButtons[i].SetActive(visible && !_showingRecents);
                if (!visible)
                    continue;
                var place = collection[collectionIndex];
                _placeButtons[i].GetComponentInChildren<Text>().text =
                    $"{Shorten(place.name, 42)}\n{place.latitude:F3}°, {place.longitude:F3}°  ·  {place.userScale:N0}×";
            }
        }

        private void ShowKeyboard()
        {
            _keyboardPanel.SetActive(true);
            _resultsPanel.SetActive(false);
            _searchStatus.text = "Enter a city, landmark, or address, then press SEARCH.";
            RefreshSearchSuggestions();
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
            RefreshSearchSuggestions();
        }

        private void RefreshSearchSuggestions()
        {
            if (_suggestionsPanel == null)
                return;
            _searchSuggestions = OfflinePlaceCatalog.FindSuggestions(
                _searchQuery,
                _places?.Bookmarks,
                _places?.RecentPlaces,
                _suggestionButtons.Count);
            var showPanel = _searchPanel != null && _searchPanel.activeInHierarchy &&
                            (_resultsPanel == null || !_resultsPanel.activeSelf) &&
                            _searchSuggestions.Count > 0;
            _suggestionsPanel.SetActive(showPanel);
            for (var i = 0; i < _suggestionButtons.Count; i++)
            {
                var visible = showPanel && i < _searchSuggestions.Count;
                _suggestionButtons[i].SetActive(visible);
                if (!visible)
                    continue;
                var suggestion = _searchSuggestions[i];
                _suggestionButtons[i].GetComponentInChildren<Text>().text =
                    $"{Shorten(suggestion.Place.Name, 24)}\n{suggestion.Category.ToUpperInvariant()}";
            }
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
            _suggestionsPanel.SetActive(false);

            try
            {
                var results = await _geocoder.SearchAsync(query, cancellation.Token);
                if (this == null || cancellation.IsCancellationRequested || _searchCancellation != cancellation)
                    return;
                _searchResults = results;

                if (_searchResults.Count == 0)
                {
                    _searchStatus.text = _searchSuggestions != null && _searchSuggestions.Count > 0
                        ? "No additional online locations found."
                        : "No locations found. Try a broader name.";
                    RefreshSearchSuggestions();
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
            SetOpen(false);
            _arrival.TravelTo(_arrival.CreateSearchDestination(place));
        }

        private void GoToSearchSuggestion(int index)
        {
            if (_searchSuggestions == null || index < 0 || index >= _searchSuggestions.Count)
                return;
            var place = _searchSuggestions[index].Place;
            SetOpen(false);
            _arrival.TravelTo(_arrival.CreateSearchDestination(place));
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
                _earth == null || _originRebaser == null || _selectionController == null || _rig == null ||
                _arrival == null)
                return;
            var rightTriggerHeld = _input.RightTriggerHeld;
            var rightTriggerPressed = rightTriggerHeld && !_rightTriggerWasHeld;
            _rightTriggerWasHeld = rightTriggerHeld;
            if (_credentialSetup != null && _credentialSetup.IsOpen)
            {
                if (_isOpen) SetOpen(false);
                _canvas.gameObject.SetActive(false);
                SetPointedButton(null);
                _globePicker.SetVisible(false);
                return;
            }
            if (_overview == null || !_overview.IsActive)
                _globePicker.SetVisible(true);

            // Travel owns the view and the right-hand pointer: any journey closes
            // the menu, and the menu cannot be reopened until the journey ends.
            if (_arrival.IsArriving || (_overview != null && _overview.IsActive))
            {
                if (_isOpen)
                    SetOpen(false);
                _canvas.gameObject.SetActive(false);
                SetPointedButton(null);
                return;
            }
            if (_input.OpenMenuPressed)
            {
                SetOpen(!_isOpen);
                rightTriggerPressed = false;
            }
            PlaceBesideGlobe();
            var showControls = _isOpen && _globePicker.IsVisible;
            _canvas.gameObject.SetActive(showControls);
            if (!showControls)
            {
                SetPointedButton(null);
                return;
            }

            PlaceBesideGlobe();
            RefreshStateButtons(false);
            // The panel rides the tracked hand and was just re-placed. Transform
            // auto-sync is off in this project, so sync before ray-testing the
            // buttons where they are drawn this frame.
            Physics.SyncTransforms();
            SetPointedButton(FindNearestPointedButton());
            if (rightTriggerPressed)
                _pointedButton?.Invoke();
            // The pressed button may have started travel, which closes the menu.
            if (!_isOpen)
                return;

            _smoothedDelta = Mathf.Lerp(_smoothedDelta, Time.unscaledDeltaTime, 0.05f);
            if (_showPerformance)
                RuntimeQuality.SampleFrameTiming();
            if (Time.unscaledTime < _nextUiRefreshTime)
                return;
            _nextUiRefreshTime = Time.unscaledTime + UiRefreshIntervalSeconds;

            _modeBadge.text = _navigation.State.Mode switch
            {
                MovementMode.Flight => "●  FLIGHT",
                MovementMode.Car => "●  CAR",
                _ => "●  GROUNDED"
            };
            if (_navigation.State.Mode == MovementMode.Car)
            {
                _status.text =
                    $"CAR SPEED  {Mathf.Abs(_navigation.CarSpeedMetersPerSecond) * 3.6f:N0} km/h\n" +
                    "RIGHT STICK  ↑ throttle  ↓ reverse  ← → steer\n" +
                    "SHOULDER  Turbo                 MENU  Exit car mode";
            }
            else
            {
                _status.text =
                    $"HEIGHT  {ScaleMath.ApproximateEyeHeight(_settings.realEyeHeightMeters, _scaling.UserScale):N1} m       " +
                    $"SPEED  {_navigation.CurrentGeographicSpeed:N1} m/s\n" +
                    "THUMBSTICK  Aim + move       RIGHT TRIGGER  Grab ground\n" +
                    "GRIP  Rotate world              SHOULDER  Speed boost\n" +
                    "SUN / MOON  Point + trigger to change time\n" +
                    "LEFT D-PAD  ↑ Overview       → Flight / grounded";
            }

            if (!_showPerformance)
            {
                var arrivalStatus = string.IsNullOrEmpty(_arrival.StatusMessage)
                    ? string.Empty
                    : "\n" + _arrival.StatusMessage;
                _diagnostics.text = _earth.StatusMessage + arrivalStatus + "\nPerformance details: off";
                return;
            }

            var llh = _navigation.LongitudeLatitudeHeight;
            var builder = new StringBuilder();
            builder.AppendLine($"FPS: {1f / Mathf.Max(0.0001f, _smoothedDelta):N0}");
            builder.AppendLine($"Frame: {_smoothedDelta * 1000f:N1} ms");
            builder.AppendLine($"Altitude: {_navigation.AltitudeMeters:N0} m ellipsoid");
            builder.AppendLine($"Lon/Lat: {llh.x:F5}, {llh.y:F5}");
            builder.AppendLine($"Origin: {_originRebaser.DistanceFromOriginUnityMeters:N0} m  ·  rebases {_originRebaser.RebaseCount}");
            builder.AppendLine($"Managed+native allocated: {Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024):N0} MB");
            builder.AppendLine($"Tiles for view: {_earth.Tileset.ComputeLoadProgress():N0}%");
            builder.AppendLine(RuntimeQuality.DescribeFrameTiming());
            builder.AppendLine(RuntimeQuality.Describe());
            builder.Append(_earth.StatusMessage);
            _diagnostics.text = builder.ToString();
        }

        /// <summary>Keeps the complete interface in the globe's head-readable
        /// frame. The panel and its circular controls now move as one object with
        /// the miniature Earth instead of being independently positioned.</summary>
        private void PlaceBesideGlobe()
        {
            if (_canvas == null || _rig == null)
                return;
            _globePicker?.RefreshInterfaceAnchorPose();
            _canvas.transform.localPosition = Vector3.zero;
            _canvas.transform.localRotation = Quaternion.identity;
            // Each panel is offset from the globe. Face it from its own center,
            // rather than sharing the globe's oblique line of sight.
            FacePanel(_mainPanel);
            FacePanel(_searchPanel);
            FacePanel(_placesPanel);
        }

        private void FacePanel(GameObject panel)
        {
            if (panel == null)
                return;
            var fromViewer = panel.transform.position - _rig.Camera.transform.position;
            if (fromViewer.sqrMagnitude > 0.000001f)
                panel.transform.rotation = Quaternion.LookRotation(fromViewer.normalized, Vector3.up);
        }

        private void OnEnable() => Application.onBeforeRender += PlaceBesideGlobe;
        private void OnDisable() => Application.onBeforeRender -= PlaceBesideGlobe;

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
                if (button == null || !button.transform.IsChildOf(_canvas.transform) ||
                    !button.gameObject.activeInHierarchy || hit.distance >= nearestDistance)
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
            if (_updates != null) _updates.Changed -= RefreshUpdateStatus;
            _searchCancellation?.Cancel();
            _searchCancellation?.Dispose();
            if (_places != null)
                _places.Changed -= RefreshPlaces;
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

        private static void ConfigureMenuPanel(GameObject panel)
        {
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(MenuPanelOffsetPixels, 0f);
            rect.sizeDelta = new Vector2(640f, 960f);
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
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.10f, 0.34f, 0.45f, 1f);
            colors.pressedColor = new Color(0.20f, 0.68f, 0.82f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.06f, 0.08f, 0.10f, 0.6f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.transition = Selectable.Transition.None;
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

        /// <summary>A button whose color reports a state. Its tint is neutral so
        /// the color set through <see cref="WorldSpaceButton.SetNormalColor"/>
        /// shows as-is instead of being multiplied by the regular button tint.</summary>
        private static WorldSpaceButton CreateStateButton(
            Transform parent,
            string name,
            Vector2 position,
            UnityEngine.Events.UnityAction callback,
            Vector2 size,
            out Text label)
        {
            var buttonObject = CreateButton(parent, name, position, callback, size);
            var button = buttonObject.GetComponent<Button>();
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Color.white;
            colors.pressedColor = Color.white;
            colors.selectedColor = Color.white;
            button.colors = colors;
            label = buttonObject.GetComponentInChildren<Text>();
            return buttonObject.GetComponent<WorldSpaceButton>();
        }

        private static WorldSpaceButton CreateRoundStateButton(
            Transform parent,
            string name,
            Vector2 position,
            UnityEngine.Events.UnityAction callback,
            Vector2 size,
            out Text label)
        {
            var buttonObject = CreateButton(parent, name, position, callback, size);
            var image = buttonObject.GetComponent<Image>();
            image.sprite = GetCircleSprite();
            image.type = Image.Type.Simple;
            var button = buttonObject.GetComponent<Button>();
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Color.white;
            colors.pressedColor = Color.white;
            colors.selectedColor = Color.white;
            button.colors = colors;
            label = buttonObject.GetComponentInChildren<Text>();
            label.fontSize = 17;
            return buttonObject.GetComponent<WorldSpaceButton>();
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

        private static Sprite GetCircleSprite()
        {
            if (_circleSprite != null)
                return _circleSprite;

            const int size = 64;
            var center = new Vector2((size - 1f) * 0.5f, (size - 1f) * 0.5f);
            var radius = size * 0.5f - 1f;
            _circleTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "EarthVR Circular UI Texture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var distance = Vector2.Distance(new Vector2(x, y), center);
                    var alpha = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(radius + 0.75f - distance));
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }
            _circleTexture.SetPixels32(pixels);
            _circleTexture.Apply(false, true);
            _circleSprite = Sprite.Create(
                _circleTexture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                100f);
            _circleSprite.name = "EarthVR Circular UI Sprite";
            _circleSprite.hideFlags = HideFlags.HideAndDontSave;
            return _circleSprite;
        }
    }
}
