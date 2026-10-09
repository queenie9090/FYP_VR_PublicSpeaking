using UnityEngine;

public class groundBoundary : MonoBehaviour
{
    public Vector3 centerPoint = Vector3.zero;
    public float radius = 2f;

    private void LateUpdate()
    {
        Vector3 flatPosition = transform.position;
        flatPosition.y = 0f;

        Vector3 flatCenter = centerPoint;
        flatCenter.y = 0f;

        Vector3 offset = flatPosition - flatCenter;

        if(offset.magnitude > radius)
            offset = offset.normalized * radius;
        transform.position = new Vector3(flatCenter.x + offset.x, transform.position.y, flatCenter.z + offset.z);

    }
}
