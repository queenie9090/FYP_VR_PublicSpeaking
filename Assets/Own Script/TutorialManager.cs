using UnityEngine;

public class TutorialManager : MonoBehaviour
{
    public GameObject[] panels;
    public GameObject startMenuPanel;

    private int currentIndex = 0;

    private void Start()
    {
        ShowPanel(0);
    }

    public void ShowPanel(int index)
    {
        for (int i = 0; i < panels.Length; i++)
            panels[i].SetActive(i == index);

        currentIndex = index;
    }

    public void NextPanel()
    {
        if (currentIndex < panels.Length - 1)
        {
            ShowPanel(currentIndex + 1);
        }
        else
        {
            CloseTutorial();
        }
    }

    public void PreviousPanel()
    {
        if (currentIndex > 0)
        {
            ShowPanel(currentIndex - 1);
        }
    }

    public void CloseTutorial()
    {
        gameObject.SetActive(false);
        if (startMenuPanel != null)
            startMenuPanel.SetActive(true);
    }
}
