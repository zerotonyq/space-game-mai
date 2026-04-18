using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

namespace App.Planets.Persistence
{
    [Serializable]
    public class ProjectileActionButtons
    {
        [SerializeField] private Button shootButton;
        [SerializeField] private Button createButton;
        [SerializeField] private TextMeshProUGUI createdCountText;
        [SerializeField] private string createdCountFormat = "{0}";
        [NonSerialized] private UnityAction _shootAction;
        [NonSerialized] private UnityAction _createAction;

        public void Bind(Action onShoot, Action onCreate)
        {
            Unbind();

            _shootAction = () => onShoot?.Invoke();
            _createAction = () => onCreate?.Invoke();

            if (shootButton != null)
                shootButton.onClick.AddListener(_shootAction);

            if (createButton != null)
                createButton.onClick.AddListener(_createAction);
        }

        public void Unbind()
        {
            if (shootButton != null && _shootAction != null)
                shootButton.onClick.RemoveListener(_shootAction);

            if (createButton != null && _createAction != null)
                createButton.onClick.RemoveListener(_createAction);

            _shootAction = null;
            _createAction = null;
        }

        public void SetInteractable(bool interactable)
        {
            if (shootButton != null)
                shootButton.interactable = interactable;

            if (createButton != null)
                createButton.interactable = interactable;
        }

        public void SetCreatedCount(int count)
        {
            if (createdCountText != null)
                createdCountText.text = FormatCount(createdCountFormat, Mathf.Max(0, count));
        }

        private static string FormatCount(string format, int count)
        {
            if (string.IsNullOrWhiteSpace(format))
                return count.ToString(CultureInfo.InvariantCulture);

            try
            {
                if (format.Contains("{0"))
                    return string.Format(CultureInfo.InvariantCulture, format, count);

                return count.ToString(format, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                return count.ToString(CultureInfo.InvariantCulture);
            }
        }
    }
}
