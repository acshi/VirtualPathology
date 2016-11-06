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
}
