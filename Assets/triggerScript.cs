using UnityEngine;
using Valve.VR;


public class triggerScript : MonoBehaviour {

    public BuildMesh buildMesh;
    public GameObject procedural;

    private SteamVR_TrackedObject trackedObject;
    private Vector3 deltaPosition;
    private Vector3 oldPosition;
    public int slideSensitivity = 10000;
    public int translationSensitivity = 5;
    private float sumScrollDelta;

    public bool isDominantController = false;
    public GameObject otherController;
    private triggerScript otherScript;
    private Vector3 otherPosition;
    private Vector3 distanceBetweenControllers;
    //activate or deactive canvas
    public GameObject canvas;
    public GameObject mainCamera;
    GameObject sphere;
    public float menuDistance = .8f;

    private SteamVR_Controller.Device device = null;

    public states controllerState;

    private LaserPointer laser;
    public enum states {
        rotate,
        translate,
        slice,
        shoot,
        none
    }

    // Use this for initialization
    void Awake() {
        trackedObject = GetComponent<SteamVR_TrackedObject>();
        laser = GetComponent<LaserPointer>();
        oldPosition = gameObject.transform.position;
        otherScript = otherController.GetComponent<triggerScript> ();
        canvas.GetComponent<Canvas> ().enabled = false;
        //sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        //sphere.transform.position = Vector3.zero;
    }

    // Update is called once per frame
    void Update() {
        if (device == null) {
            if (trackedObject.index != SteamVR_TrackedObject.EIndex.None) {
                device = SteamVR_Controller.Input((int)trackedObject.index);
            }
        } else {
            //Debug.Log ("pos:" + gameObject.transform.position);
            if (controllerState == states.rotate ) {
                if (otherScript.controllerState != states.rotate || isDominantController) {
                    if (device.GetPressDown (SteamVR_Controller.ButtonMask.Trigger)) {
                        buildMesh.shouldSnap = false;
                        //buildMesh.triggerDown (gameObject.transform.position);
                    } else if (device.GetPress (SteamVR_Controller.ButtonMask.Trigger)) {
                        //buildMesh.triggerHeld (gameObject.transform.position);
						buildMesh.triggerLookRotation(gameObject.transform.position);
                    } else if (device.GetPressUp (SteamVR_Controller.ButtonMask.Trigger)) {
                        //buildMesh.triggerUp ();
                    }
                }

                laser.active = false;
            } else if (controllerState == states.translate) {
                if (device.GetPress (SteamVR_Controller.ButtonMask.Trigger)) {
                    buildMesh.shouldReset = false;
                    procedural.transform.position += deltaPosition;
                }
                laser.active = false;
            } else if (controllerState == states.shoot) {
                laser.active = true;
            } else if (controllerState == states.slice) {
                if (device.GetPress(SteamVR_Controller.ButtonMask.Trigger)) {

                    sumScrollDelta += (gameObject.transform.position.z - oldPosition.z) * slideSensitivity;
                    if (Mathf.Abs(sumScrollDelta) >= 1) {
                        int ticks = (int)sumScrollDelta;
                        sumScrollDelta -= ticks;
                        buildMesh.orthogonalScroll(ticks);
                    }

                }
                laser.active = false;
            }
        }

        if (device.GetPressDown(SteamVR_Controller.ButtonMask.Touchpad)) {
            Vector2 coords = device.GetAxis(EVRButtonId.k_EButton_SteamVR_Touchpad);
            //if zoom
            if (Mathf.Abs(coords.x) < Mathf.Abs(coords.y)) {
                if (coords.y < 0) {
                    Debug.Log("bottom");
                    //buildMesh.zoomIn (-increment);
                    controllerState = states.translate;
                } else {
                    Debug.Log("top");
                    //buildMesh.zoomIn (increment);
                    controllerState = states.slice;
                }
            } else {
                if (coords.x < 0) {
                    Debug.Log("left");
                    //procedural.transform.Rotate (0, 10, 0);
                    controllerState = states.shoot;
                } else {
                    Debug.Log("right");
                    //procedural.transform.Rotate (0, -10, 0);
                    controllerState = states.rotate;
                }
            }
        }

        if (device.GetPressDown(SteamVR_Controller.ButtonMask.Grip)) {
            buildMesh.shouldSnap = true;
            buildMesh.shouldReset = true;
        }

        //toggle menu pointer on menu button
        //NOTE: menu pointer actually controlled via VRTK Controller Events script, but we track state here as well to disable other actions
        if (device.GetPressDown (SteamVR_Controller.ButtonMask.ApplicationMenu)) {
            controllerState = states.none;
            canvas.GetComponent<Canvas> ().enabled = true;
            canvas.GetComponent<Canvas>().transform.position = Camera.main.transform.position + Camera.main.transform.forward;// * menuDistance;
            //canvas.transform.LookAt(gameObject.transform);
            canvas.transform.rotation = Quaternion.LookRotation(canvas.transform.position - Camera.main.transform.position);
            //canvas.transform.localPosition = Vector3.zero;
            //canvas.transform.localRotation = Quaternion.identity;
            
            //canvas.transform.rotation = mainCamera.transform.rotation;
            Debug.Log ("camera global pos: " + mainCamera.transform.position + ", local pos: " + mainCamera.transform.localPosition);
            Debug.Log ("camera global pos: " + Camera.main.transform.position + ", local pos: " + Camera.main.transform.localPosition);
            //Debug.Log ("camera global rot: " + mainCamera.transform.rotation + ", local rot: " + mainCamera.transform.localRotation);
            //Debug.Log ("controller global pos: " + gameObject.transform.position + ", local pos: " + gameObject.transform.localPosition);
            Debug.Log ("canvas global pos: " + canvas.transform.position + ", local pos: " + canvas.transform.localPosition);
            Debug.Log ("canvas global rot: " + canvas.transform.rotation + ", local rot: " + canvas.transform.localRotation);
            //Debug.Log ("forward: " + mainCamera.transform.forward);
        }
        if (device.GetPressUp (SteamVR_Controller.ButtonMask.ApplicationMenu)) {
            canvas.GetComponent<Canvas> ().enabled = false;
        }
        deltaPosition = translationSensitivity * (gameObject.transform.position - oldPosition);
        oldPosition = gameObject.transform.position;
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
