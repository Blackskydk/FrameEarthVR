using EarthVR.Configuration;
using EarthVR.Core;
using EarthVR.Navigation;
using UnityEngine;
using UnityEngine.UI;

namespace EarthVR.UI
{
    /// <summary>
    /// An optional peripheral-vision darkening shown during fast continuous
    /// locomotion, sized by how fast the camera is physically moving through
    /// real space (not raw geographic speed, which is meaningless once scale is
    /// applied). Off by default; the wrist menu can toggle <see cref="Enabled"/>.
    /// </summary>
    public sealed class ComfortVignetteController : MonoBehaviour
    {
        private EarthVRSettings _settings;
        private NavigationController _navigation;
        private Image _vignetteImage;
        private float _currentAlpha;

        public bool Enabled { get; set; }

        public void Initialize(EarthVRSettings settings, EarthVRRig rig, NavigationController navigation)
        {
            _settings = settings;
            _navigation = navigation;
            Enabled = settings.comfortVignetteEnabled;

            // Screen Space - Camera canvases do not reliably render under XR's
            // stereo single-pass rendering (the same reason the wrist menu is a
            // World Space canvas). Use a World Space quad parented to the camera
            // instead, sized generously so it comfortably covers any headset's
            // field of view regardless of exact FOV/aspect.
            var canvasObject = new GameObject("Comfort Vignette Canvas", typeof(Canvas));
            canvasObject.transform.SetParent(rig.Camera.transform, false);
            canvasObject.transform.localPosition = new Vector3(0f, 0f, 0.4f);
            canvasObject.transform.localRotation = Quaternion.identity;
            canvasObject.transform.localScale = Vector3.one * 2.2f;
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = rig.Camera;

            var imageObject = new GameObject("Vignette", typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(canvasObject.transform, false);
            var rect = imageObject.GetComponent<RectTransform>();
            rect.sizeDelta = Vector2.one;

            _vignetteImage = imageObject.GetComponent<Image>();
            _vignetteImage.sprite = CreateVignetteSprite();
            _vignetteImage.type = Image.Type.Simple;
            _vignetteImage.raycastTarget = false;
            _vignetteImage.color = new Color(0.02f, 0.02f, 0.03f, 0f);
        }

        private void Update()
        {
            if (_vignetteImage == null || _navigation == null || _settings == null)
                return;

            var targetAlpha = 0f;
            if (Enabled)
            {
                var startSpeed = _settings.comfortVignetteStartSpeedMetersPerSecond;
                var fullSpeed = Mathf.Max(startSpeed + 0.01f, _settings.comfortVignetteFullSpeedMetersPerSecond);
                var normalized = Mathf.InverseLerp(startSpeed, fullSpeed, _navigation.PhysicalSpeedMetersPerSecond);
                targetAlpha = Mathf.Clamp01(normalized) * _settings.comfortVignetteMaximumAlpha;
            }

            var responseSeconds = Mathf.Max(0.01f, _settings.comfortVignetteResponseSeconds);
            _currentAlpha = Mathf.MoveTowards(_currentAlpha, targetAlpha, Time.deltaTime / responseSeconds);
            var color = _vignetteImage.color;
            color.a = _currentAlpha;
            _vignetteImage.color = color;
        }

        private static Sprite CreateVignetteSprite()
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "EarthVR Comfort Vignette",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            var pixels = new Color32[size * size];
            var center = new Vector2(size * 0.5f, size * 0.5f);
            var maximumRadius = size * 0.5f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var distance01 = Vector2.Distance(new Vector2(x, y), center) / maximumRadius;
                    var alpha01 = Mathf.Clamp01(Mathf.InverseLerp(0.45f, 1f, distance01));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha01 * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
            sprite.name = "EarthVR Comfort Vignette Sprite";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}
