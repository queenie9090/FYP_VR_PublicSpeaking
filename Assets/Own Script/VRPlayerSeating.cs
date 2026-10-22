using UnityEngine;

public class VRPlayerSeating : MonoBehaviour
{
    [SerializeField] private Transform playerXRRig;
    [SerializeField] private Transform chairTargetTransform;

    public void SnapPlayerToChair()
    {
        if (playerXRRig == null || chairTargetTransform == null) return;

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
    }
}