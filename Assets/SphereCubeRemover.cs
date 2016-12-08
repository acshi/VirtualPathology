using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SphereCubeRemover : MonoBehaviour {
	private GameObject sphere;
	private SphereCollider sphereCollider;
	public bool isVisible = true;

	// Use this for initialization
	void Start () {
		sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.transform.localScale = new Vector3(0.2f, 0.2f, 0.2f);
        sphereCollider = sphere.AddComponent<SphereCollider>() as SphereCollider;
		//sphereMC.convex = true;
	}
	
	// Update is called once per frame
	void Update () {
		if (isVisible) {
            //Debug.Log("controller position?? " + gameObject.transform.position);
			sphere.transform.position = gameObject.transform.position;
            sphereCollider.center = gameObject.transform.position;
		}
	}

	void setVisibility(bool isVisible) {
		this.isVisible = isVisible;
		sphere.SetActive(isVisible);
	}
}
