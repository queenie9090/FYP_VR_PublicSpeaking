using System.Collections;
using UnityEngine;

public class VRPlayerSeating : MonoBehaviour
{
    [SerializeField] private Transform playerXRRig;
    [SerializeField] private Transform chairTargetTransform;
    [SerializeField] private CharacterController characterController;

    public void SnapPlayerToChair()
    {
        if (playerXRRig == null || chairTargetTransform == null)
        {
            Debug.LogError("[VRPlayerSeating Error] playerXRRig or chairTargetTransform is NULL!");
            return;
        }

        Debug.Log($"[VRPlayerSeating] chair target WORLD pos: {chairTargetTransform.position} | local: {chairTargetTransform.localPosition} | parent: {(chairTargetTransform.parent ? chairTargetTransform.parent.name : "none")} | rig: {playerXRRig.position}");

        StartCoroutine(SnapPlayerRoutine());
    }

    private IEnumerator SnapPlayerRoutine()
    {
        if (characterController == null)
            characterController = playerXRRig.GetComponentInChildren<CharacterController>();

        // 1. Disable CharacterController physics
        if (characterController != null)
            characterController.enabled = false;

        yield return null; // Wait 1 frame for physics to decouple

        Camera vrCamera = Camera.main ?? playerXRRig.GetComponentInChildren<Camera>();

        if (vrCamera != null)
        {
            Vector3 cameraOffset = vrCamera.transform.position - playerXRRig.position;
            cameraOffset.y = 0;

            playerXRRig.position = chairTargetTransform.position - cameraOffset;

            float rotationAngleY = chairTargetTransform.rotation.eulerAngles.y - vrCamera.transform.rotation.eulerAngles.y;
            playerXRRig.RotateAround(vrCamera.transform.position, Vector3.up, rotationAngleY);
        }
        else
        {
            playerXRRig.position = chairTargetTransform.position;
            playerXRRig.rotation = chairTargetTransform.rotation;
        }

        yield return null; // Wait 1 frame for transform position to settle

        // 2. Re-enable CharacterController
        if (characterController != null)
            characterController.enabled = true;

        Debug.Log($"<color=green>[VRPlayerSeating Success]</color> Player snapped to chair target position: {playerXRRig.position}");
    }
}