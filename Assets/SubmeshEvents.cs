using UnityEngine;

// Simple Behavior to forward events from a mesh to the main buildMesh behavior
public class SubmeshEvents : MonoBehaviour {
    public BuildMesh buildMesh;
    void Start() { }
    void OnMouseDown() {
        if (buildMesh != null) {
            buildMesh.OnMouseDown();
        }
    }
    void OnMouseDrag() {
        if (buildMesh != null) {
            buildMesh.OnMouseDrag();
        }
    }
    void OnMouseUp() {
        if (buildMesh != null) {
            buildMesh.OnMouseUp();
        }
    }
    void Update() { }

    void OnCollisionEnter(Collision col)
    {
        Debug.Log("entered OnCollisionEnter");
        // We just take the first collision point and ignore others
        ContactPoint P = col.contacts[0];
        RaycastHit hit;
        Ray ray = new Ray(P.point + P.normal * 0.05f, -P.normal);
        if (gameObject.GetComponent<MeshCollider>().Raycast(ray, out hit, 0.1f))
        {
            int triangle = hit.triangleIndex;
            Debug.Log("Got triangle: " + triangle);
            // do something...
        }
        else
            Debug.LogError("Have a collision but can't raycast the point");
    }
}
