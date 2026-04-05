using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace App.Planets.Persistence
{
    public class GameplayAimJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [SerializeField] private RectTransform joystickArea;
        [SerializeField] private RectTransform handle;
        [SerializeField] [Min(1f)] private float handleRangePixels = 60f;

        private Vector2 _direction;
        private bool _isInteractable = true;

        public Vector2 Direction => _direction;
        public event Action<Vector2> DirectionChanged;
        public event Action Released;

        public void SetInteractable(bool isInteractable)
        {
            _isInteractable = isInteractable;
            if (!_isInteractable)
                ResetHandle();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!_isInteractable)
                return;

            UpdateFromPointer(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_isInteractable)
                return;

            UpdateFromPointer(eventData);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!_isInteractable)
                return;

            ResetHandle(notifyRelease: true);
        }

        private void UpdateFromPointer(PointerEventData eventData)
        {
            var area = joystickArea ? joystickArea : transform as RectTransform;
            if (area == null)
                return;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    area,
                    eventData.position,
                    eventData.pressEventCamera,
                    out var localPoint))
                return;

            var clamped = Vector2.ClampMagnitude(localPoint, handleRangePixels);
            _direction = clamped.sqrMagnitude > 0.0001f ? clamped.normalized : Vector2.zero;
            DirectionChanged?.Invoke(_direction);

            if (handle)
                handle.anchoredPosition = clamped;
        }

        private void ResetHandle(bool notifyRelease = false)
        {
            if (notifyRelease)
                Released?.Invoke();

            _direction = Vector2.zero;
            DirectionChanged?.Invoke(_direction);

            if (handle)
                handle.anchoredPosition = Vector2.zero;
        }
    }
}
