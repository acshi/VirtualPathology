using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SphereCollider : MonoBehaviour {
	private GameObject sphere;
	private MeshCollider sphereMC;
	public bool isVisible;

	// Use this for initialization
	void Start () {
		sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
		sphereMC = sphere.AddComponent<>(MeshCollider);
		sphereMC.convex = true;
	}
	
	// Update is called once per frame
	void Update () {
		if (isVisible) {
			sphere.transform.position = gameObject.transform.position;
		}
	}

	void setVisibility(bool isVisible) {
		this.isVisible = isVisible;
		sphere.SetActiveRecursively(isVisible);
		sphereMC.convex = isVisible;
	}

	void OnCollisionEnter(Collision col)
	{
		if (!isActive) {
			Debug.Log("still in onCollisionEnter");
			return;
		}
		Debug.Log("entered OnCollisionEnter");
		// We just take the first collision point and ignore others
		ContactPoint P = col.contacts[0];
		RaycastHit hit;
		Ray ray = new Ray(P.point + P.normal * 0.05f, -P.normal);
		if (P.otherCollider.RayCast(ray, out hit, 0.1f))
		{
			int triangle = hit.triangleIndex;
			Debug.Log("Got triangle: " + triangle);
			// do something...
		}
		else
			Debug.LogError("Have a collision but can't raycast the point");
	}
}
