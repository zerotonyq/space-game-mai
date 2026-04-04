using TMPro;
using UnityEngine;

namespace App.Planets.GfxGen
{
    public class WorldLoadingCanvasController : MonoBehaviour
    {
        [SerializeField] private Canvas rootCanvas;
        [SerializeField] private TMP_Text progressText;

        public void SetVisible(bool isVisible)
        {
            if (rootCanvas != null)
                rootCanvas.gameObject.SetActive(isVisible);
        }

        public void SetProgress(bool isLoading, float progress01)
        {
            if (progressText == null)
                return;

            if (!isLoading)
            {
                progressText.text = string.Empty;
                return;
            }

            var percent = Mathf.RoundToInt(Mathf.Clamp01(progress01) * 100f);
            progressText.text = $"{percent}%";
        }
    }
}
