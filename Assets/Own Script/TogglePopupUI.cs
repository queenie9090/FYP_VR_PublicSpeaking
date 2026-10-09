using UnityEngine;

public class ExitButtonPopUp : MonoBehaviour
{
    public GameObject popupIcon;

    public void ToggleUI()
    {
        if (popupIcon != null) {
            popupIcon.SetActive(!popupIcon.activeSelf);
        }
    }
}
