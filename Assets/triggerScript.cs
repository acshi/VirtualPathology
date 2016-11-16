using UnityEngine;
using Valve.VR;


public class triggerScript : MonoBehaviour {

    public GameObject procedural;

    private SteamVR_TrackedObject trackedObject;

    private Vector3 positionDifference;
    private Vector3 increment = new Vector3(.1f, .1f, .1f);

    private SteamVR_Controller.Device device;

    // Use this for initialization
    void Awake() {
        trackedObject = GetComponent<SteamVR_TrackedObject>();
        device = SteamVR_Controller.Input((int)trackedObject.index);
		Debug.Log ("awake and set device");
    }

    // Update is called once per frame
    void FixedUpdate() {
        if (device.GetTouch(SteamVR_Controller.ButtonMask.Trigger)) {
            Debug.Log("got press");
            procedural.transform.position = transform.position + positionDifference;
        } else {
            positionDifference = procedural.transform.position - transform.position;
        }


        if (device.GetPressDown(SteamVR_Controller.ButtonMask.Touchpad)) {
            Vector2 coords = device.GetAxis(EVRButtonId.k_EButton_SteamVR_Touchpad);
            //if zoom
            if (coords.x > -.2f && coords.x < .2f) {
                if (coords.y < 0) {
                    Debug.Log("bottom");
                    procedural.transform.localScale -= increment;
                } else {
                    Debug.Log("top");
                    procedural.transform.localScale += increment;
                }
            } else {
                if (coords.x < 0) {
                    Debug.Log("left");
                    procedural.transform.Rotate(0, 10, 0);
                } else {
                    Debug.Log("right");
                    procedural.transform.Rotate(0, -10, 0);
                }
            }
        }
    }

    void OnCollisionEnter(Collision col) {
        // We just take the first collision point and ignore others
        Debug.LogError("Entered OnCollisionEnter");
        ContactPoint P = col.contacts[0];
        RaycastHit hit;
        Ray ray = new Ray(P.point + P.normal * 0.05f, -P.normal);
        if (P.otherCollider.Raycast(ray, out hit, 0.1f)) {
            int triangle = hit.triangleIndex;
            Debug.LogError("got triangle: " + triangle);
            // do something...
        } else {
            Debug.LogError("Have a collision but can't raycast the point");
        }
    }
}
