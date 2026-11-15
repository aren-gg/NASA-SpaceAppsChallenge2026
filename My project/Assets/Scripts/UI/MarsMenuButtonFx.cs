using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MarsSurvival.UI
{
    /// <summary>
    /// Hover and press feedback for the home screen buttons: colours ease toward their
    /// hover values, the button grows slightly on hover and dips when pressed.
    /// Runs on unscaled time so it works while the game is paused.
    /// </summary>
    public sealed class MarsMenuButtonFx : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public Graphic background;
        public Color backgroundNormal = Color.white, backgroundHover = Color.white;
        public Graphic label;
        public Color labelNormal = Color.white, labelHover = Color.white;
        public Graphic fill;
        public Color fillNormal = Color.clear, fillHover = Color.clear;
        public float hoverScale = 1.035f;
        public float pressScale = 0.97f;

        private bool hovered, pressed;
        private float hover, press;

        public void OnPointerEnter(PointerEventData eventData) { hovered = true; }
        public void OnPointerExit(PointerEventData eventData) { hovered = false; pressed = false; }
        public void OnPointerDown(PointerEventData eventData) { pressed = true; }
        public void OnPointerUp(PointerEventData eventData) { pressed = false; }

        private void OnDisable()
        {
            hovered = pressed = false;
            hover = press = 0f;
            Apply(0f);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            hover = Mathf.MoveTowards(hover, hovered ? 1f : 0f, dt * 7f);
            press = Mathf.MoveTowards(press, pressed ? 1f : 0f, dt * 14f);
            Apply(hover);
            float scale = Mathf.Lerp(1f, hoverScale, hover) * Mathf.Lerp(1f, pressScale, press);
            transform.localScale = new Vector3(scale, scale, 1f);
        }

        public void Apply(float t)
        {
            t = t * t * (3f - 2f * t);
            if (background != null) background.color = Color.Lerp(backgroundNormal, backgroundHover, t);
            if (label != null) label.color = Color.Lerp(labelNormal, labelHover, t);
            if (fill != null) fill.color = Color.Lerp(fillNormal, fillHover, t);
        }
    }
}
