using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlayerProgressUI : MonoBehaviour
{
    public Slider progressSlider;
    public TMP_Text stepText;
    public TMP_Text percentageText;
    [Header("Audio Settings")]
    public AudioSource audioSource;
    public AudioClip soundCue;
    private int currentStep = 1;
    private int totalSteps = 5;

    private string[] steps = { 
        "Step 1: Approach the table",
        "Step 2: Start your speech",
        "Step 3: Select topic",
        "Step 4: Talks to students",
        "Step 5: Receieve my feedback"
    };

    private string[] percentageTexts = {
        "0%",
        "33%",
        "66%",
        "80%",
        "100%"
    };

    private void Start()
    {
        progressSlider.interactable = false;
        UpdateUI();
    }

    public void NextStep()
    {
        if (currentStep < totalSteps)
        {
            currentStep++;
            UpdateUI();
            audioSource.PlayOneShot(soundCue);
        }
    }

    private void UpdateUI()
    {
        progressSlider.value = currentStep-1;
        stepText.text = steps[currentStep -1];
        percentageText.text = percentageTexts[currentStep -1];
    }

    public void ToggleProgressBar()
    {
        if (this.gameObject != null)
        {
            gameObject.SetActive(!gameObject.activeSelf);
        }
    }
    
}
