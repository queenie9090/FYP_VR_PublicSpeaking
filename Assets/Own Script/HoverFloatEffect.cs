using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class HoverFloatEffect : MonoBehaviour
{
    public Vector3 floatOffset = new Vector3(0, 0.05f, 0);
    public float floatSpeed = 4f;

    private Vector3 originalPosition;
    private XRBaseInteractable interactable;
    private bool isHovered = false;

    void Start()
    {
        originalPosition = transform.localPosition;
        interactable = GetComponent<XRBaseInteractable>();

        interactable.hoverEntered.AddListener(_ => isHovered = true);
        interactable.hoverExited.AddListener(_ => isHovered = false);
    }

    void Update()
    {
        Vector3 targetPosition = isHovered ? originalPosition + floatOffset : originalPosition;
        transform.localPosition = Vector3.Lerp(transform.localPosition, targetPosition, Time.deltaTime * floatSpeed);
    }
}
