using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Valve.VR;


public class triggerScript : MonoBehaviour {

	private SteamVR_TrackedObject trackedObject;

	private Vector3 positionDifference;
	private Vector3 increment = new Vector3 (.1f, .1f, .1f);

	// Use this for initialization
	void Awake () {
		trackedObject = GetComponent<SteamVR_TrackedObject> ();
	}
	
	// Update is called once per frame
	void FixedUpdate () {
		SteamVR_Controller.Device device = SteamVR_Controller.Input ((int)trackedObject.index);
		if (device.GetTouch (SteamVR_Controller.ButtonMask.Trigger)) {
			Debug.Log ("got press");
			GameObject.Find ("mesh0").transform.position = this.transform.position + positionDifference;
		} else {
			positionDifference = GameObject.Find ("mesh0").transform.position - this.transform.position;
		}

		if (device.GetPressDown(SteamVR_Controller.ButtonMask.Touchpad)) {
			if (device.GetAxis (EVRButtonId.k_EButton_SteamVR_Touchpad).y < 0) {
				Debug.Log ("bottom");
				GameObject.Find ("mesh0").transform.localScale -= increment;
			} else {
				Debug.Log ("top");
				GameObject.Find ("mesh0").transform.localScale += increment;
			}


		}
	}

	void OnCollisionEnter(Collision col)
	{
		// We just take the first collision point and ignore others
		Debug.LogError("Entered OnCollisionEnter");
		ContactPoint P = col.contacts[0];
		RaycastHit hit;
		Ray ray = new Ray(P.point + P.normal * 0.05f, -P.normal);
		if (P.otherCollider.Raycast(ray, out hit, 0.1f))
		{
			int triangle = hit.triangleIndex;
			Debug.LogError("got triangle: " + triangle);
			// do something...
		}
		else
			Debug.LogError("Have a collision but can't raycast the point");
	}

}
