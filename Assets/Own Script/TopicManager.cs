using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class TopicManager : MonoBehaviour
{
    [Header("UI Elements")]
    public Dropdown topicDropdown;
    public Button confirmButton;
    public Button randomizeButton;
    public Text topicTitleText;
    public Text topicContentText;
    [SerializeField] private GameObject exitButtonCanvas;

    [System.Serializable]
    public class Topic
    {
        public string title;
        [TextArea(3, 10)]
        public string content;
    }

    [Header("Topics")]
    public List<Topic> availableTopics = new List<Topic>();

    private int selectedTopicIndex = 0;

    void Start()
    {
        SetupDropdownOptions();

        topicDropdown.onValueChanged.AddListener(OnDropdownChanged);
        randomizeButton.onClick.AddListener(RandomizeTopic);
        confirmButton.onClick.AddListener(OnConfirmTopic);

        UpdateTopicUI(0);
    }

    void SetupDropdownOptions()
    {
        topicDropdown.ClearOptions();
        List<string> options = new List<string>();
        foreach (var topic in availableTopics)
        {
            options.Add(topic.title);
        }
        topicDropdown.AddOptions(options);
    }

    void OnDropdownChanged(int index)
    {
        selectedTopicIndex = index;
        UpdateTopicUI(index);
    }

    void UpdateTopicUI(int index)
    {
        if (index >= 0 && index < availableTopics.Count)
        {
            topicTitleText.text = availableTopics[index].title;
            topicContentText.text = availableTopics[index].content;
        }
    }

    void RandomizeTopic()
    {
        int randomIndex = Random.Range(0, availableTopics.Count);
        topicDropdown.value = randomIndex;
        topicDropdown.RefreshShownValue();
        UpdateTopicUI(randomIndex);
    }

    void OnConfirmTopic()
    {
        exitButtonCanvas.SetActive(false); // Hide exit button to restrict leaving
        Debug.Log("Topic confirmed: " + availableTopics[selectedTopicIndex].title);
    }

    public string GetSelectedTopicTitle()
    { 
        return availableTopics[selectedTopicIndex].title;
    }
}
