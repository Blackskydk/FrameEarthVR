using UnityEngine;
using UnityEngine.UI;

namespace EarthVR.UI
{
    public sealed class WorldSpaceButton : MonoBehaviour
    {
        private Button _button;
        private Image _image;
        private Color _normalColor;
        private bool _pointed;
        private Text _label;
        private Color _labelColor;
        private Outline _outline;

        public void Configure(Button button)
        {
            _button = button;
            _image = button != null ? button.targetGraphic as Image : null;
            if (_image != null)
                _normalColor = _image.color;
        }

        public void SetPointed(bool pointed)
        {
            _pointed = pointed;
            if (_image == null)
                return;
            RefreshAppearance();
        }

        public void SetNormalColor(Color color)
        {
            _normalColor = color;
            if (_image != null)
                RefreshAppearance();
        }

        private void RefreshAppearance()
        {
            if (_label == null)
            {
                _label = GetComponentInChildren<Text>();
                if (_label != null)
                    _labelColor = _label.color;
            }
            if (_outline == null)
            {
                _outline = gameObject.AddComponent<Outline>();
                _outline.effectColor = Color.white;
                _outline.effectDistance = new Vector2(4f, -4f);
            }
            _outline.enabled = _pointed;
            _image.color = _pointed ? new Color(1f, 0.85f, 0.12f, 1f) : _normalColor;
            if (_label != null)
                _label.color = _pointed ? new Color(0.015f, 0.025f, 0.04f, 1f) : _labelColor;
        }

        public void Invoke() => _button?.onClick.Invoke();
    }
}
