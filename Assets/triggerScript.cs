using UnityEngine;
using Valve.VR;


public class triggerScript : MonoBehaviour {

    public BuildMesh buildMesh;
    public GameObject procedural;

    private SteamVR_TrackedObject trackedObject;

    private Vector3 positionDifference;
    private float increment = .1f;

	private SteamVR_Controller.Device device = null;

    // Use this for initialization
    void Awake() {
        trackedObject = GetComponent<SteamVR_TrackedObject>();
    }



    // Update is called once per frame
    void Update() {
		if (device == null) {
			if (trackedObject.index != SteamVR_TrackedObject.EIndex.None) {
				device = SteamVR_Controller.Input ((int)trackedObject.index);
			} 

		} else {
			//Debug.Log ("pos:" + gameObject.transform.position);
			if (device.GetPressDown (SteamVR_Controller.ButtonMask.Trigger)) {
				buildMesh.triggerDown (gameObject.transform.position);
			} else if (device.GetPress (SteamVR_Controller.ButtonMask.Trigger)) {
				buildMesh.triggerHeld (gameObject.transform.position);
			} else if (device.GetPressUp (SteamVR_Controller.ButtonMask.Trigger)) {
				buildMesh.triggerUp ();
			}


				if (device.GetPressDown (SteamVR_Controller.ButtonMask.Touchpad)) {
					Vector2 coords = device.GetAxis (EVRButtonId.k_EButton_SteamVR_Touchpad);
					//if zoom
					if (coords.x > -.2f && coords.x < .2f) {
						if (coords.y < 0) {
							Debug.Log ("bottom");
							buildMesh.zoomIn (-increment);
						} else {
							Debug.Log ("top");
							buildMesh.zoomIn (increment);
						}
					} else {
						if (coords.x < 0) {
							Debug.Log ("left");
							procedural.transform.Rotate (0, 10, 0);
						} else {
							Debug.Log ("right");
							procedural.transform.Rotate (0, -10, 0);
						}
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
