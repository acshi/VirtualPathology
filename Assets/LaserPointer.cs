using UnityEngine;

public class LaserPointer : MonoBehaviour {
    public bool active = false;
    public Color color;
    public float thickness = 0.002f;
    //GameObject holder;
    GameObject pointer;
    bool isActive = false;
    public bool addRigidBody = false;
    public event PointerEventHandler PointerIn;
    public event PointerEventHandler PointerOut;
    public BuildMesh bm;

    Transform previousContact = null;

    private SteamVR_Controller.Device device;
    private SteamVR_TrackedObject trackedObject;

    // Use this for initialization
    void Start() {
        //holder = new GameObject("Laser Pointer Holder");
        //holder.transform.parent = this.transform;
        //holder.transform.localPosition = Vector3.zero;

        pointer = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pointer.name = "Laser Pointer";
        pointer.transform.parent = this.transform;
        pointer.transform.localScale = new Vector3(thickness, thickness, 100f);
        pointer.transform.localPosition = new Vector3(0f, 0f, 50f);
        BoxCollider collider = pointer.GetComponent<BoxCollider>();
        if (addRigidBody) {
            if (collider) {
                collider.isTrigger = true;
            }
            Rigidbody rigidBody = pointer.AddComponent<Rigidbody>();
            rigidBody.isKinematic = true;
        } else {
            if (collider) {
                Object.Destroy(collider);
            }
        }
        Material newMaterial = new Material(Shader.Find("Unlit/Color"));
        newMaterial.SetColor("_Color", color);
        pointer.GetComponent<MeshRenderer>().material = newMaterial;

        trackedObject = GetComponent<SteamVR_TrackedObject>();
        device = SteamVR_Controller.Input((int)trackedObject.index);
    }

    public virtual void OnPointerIn(PointerEventArgs e) {
        if (PointerIn != null) {
            PointerIn(this, e);
        }
    }

    public virtual void OnPointerOut(PointerEventArgs e) {
        if (PointerOut != null) {
            PointerOut(this, e);
        }
    }


    // Update is called once per frame
    void Update() {
        if (active) {
            pointer.SetActive(true);

            if (!isActive) {
                isActive = true;
                this.transform.GetChild(0).gameObject.SetActive(true);
            }

            float dist = 100f;
            //
            SteamVR_TrackedController controller = GetComponent<SteamVR_TrackedController>();

            Ray raycast = new Ray(transform.position, transform.forward);
            RaycastHit hit;
            bool bHit = Physics.Raycast(raycast, out hit);

            if (device.GetTouch(SteamVR_Controller.ButtonMask.Trigger)) {
                Debug.Log("got press 2");
                bm.removeCubeFromRay(hit);
            }

            if (device.GetTouch(SteamVR_Controller.ButtonMask.Trigger))
                if (previousContact && previousContact != hit.transform) {
                    PointerEventArgs args = new PointerEventArgs();
                    if (controller != null) {
                        args.controllerIndex = controller.controllerIndex;
                    }
                    args.distance = 0f;
                    args.flags = 0;
                    args.target = previousContact;
                    OnPointerOut(args);
                    previousContact = null;
                }
            if (bHit && previousContact != hit.transform) {
                PointerEventArgs argsIn = new PointerEventArgs();
                if (controller != null) {
                    argsIn.controllerIndex = controller.controllerIndex;
                }
                argsIn.distance = hit.distance;
                argsIn.flags = 0;
                argsIn.target = hit.transform;
                OnPointerIn(argsIn);
                previousContact = hit.transform;
            }
            if (!bHit) {
                previousContact = null;
            }
            if (bHit && hit.distance < 100f) {
                dist = hit.distance;
            }

            if (controller != null && controller.triggerPressed) {
                pointer.transform.localScale = new Vector3(thickness * 5f, thickness * 5f, dist);
            } else {
                pointer.transform.localScale = new Vector3(thickness, thickness, dist);
            }
            pointer.transform.localPosition = new Vector3(0f, 0f, dist / 2f);
        } else {
            pointer.SetActive(false);
        }
    }
}
