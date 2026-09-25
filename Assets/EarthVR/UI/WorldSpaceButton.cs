using UnityEngine;
using UnityEngine.UI;

namespace EarthVR.UI
{
    public sealed class WorldSpaceButton : MonoBehaviour
    {
        private Button _button;
        private Image _image;
        private Color _normalColor;

        public void Configure(Button button)
        {
            _button = button;
            _image = button != null ? button.targetGraphic as Image : null;
            if (_image != null)
                _normalColor = _image.color;
        }

        public void SetPointed(bool pointed)
        {
            if (_image == null)
                return;
            _image.color = pointed
                ? Color.Lerp(_normalColor, new Color(0.22f, 0.82f, 1f, 1f), 0.58f)
                : _normalColor;
        }

        public void Invoke() => _button?.onClick.Invoke();
    }
}
