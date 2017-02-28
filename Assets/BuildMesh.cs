using UnityEngine;
using System.IO;
using System;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.IO.IsolatedStorage;

public class BuildMesh : MonoBehaviour {
    string datasetDirectory = "human_kidney_png";

    float yAspectRatio = 2.8f;
    public int subcubeSize = 30; // each cube has x, y, and z of this dimension.
    const float mouseRotationSensitivity = 0.3f;
    const float triggerRotationSensitivity = 300f;
    const float baseCameraZ = -2;

    Texture2D[] layers; // original image files -- the y axis
    float scaleFactor;
    int layerWidth; // Z
    int layerHeight; // X
    int layerNumber; // Y
    int[] layerPixels;

    float[] meshSize;

    // Used as a reference only to set up the materials of the meshes
    MeshRenderer baseRenderer;
    LineRenderer slicelineRenderer;

    // For detailed view mode
    GameObject[] detailsObjects;
    Mesh[] detailsMeshes;
    MeshRenderer[] detailsRenderers;
    Material[][] detailsMaterials;
    MeshCollider[] detailsColliders;
    Vector3[][] detailsVertices;
    Vector2[][] detailsUvs;

    List<Texture2D> cachedTextures = new List<Texture2D>();
    List<Plane> cachedTexturePlanes = new List<Plane>();

    // To minimize changes in updateMaterials
    int materialsMainAxis = -1; // xyz: 0, 1, 2
    int materialsAxisSign = -1; // +-: 0, 1

    bool isRotating = false;
    enum sliceMode { NONE, VERT, HORIZ };
    sliceMode currentSliceMode;
    Vector3 dragStartPosition;
    public Vector3 dominantLastPosition;
    public Vector3 nonDominantLastPosition;

    // How much to add to the local scale of the object
    // We use scale to effect zoom in a camera-independant manner
    public float zoomIncrements = 0;

    public bool shouldSnap = false;
    public bool shouldReset = false;
    Quaternion snappingRotation = new Quaternion();

    int[,] rmLayersXyz = new int[3, 2]; // [xyz index, minMax index]
    int[,] rmPixelsXyz = new int[3, 2]; // same as above, but with higher resolution for the details view
    //Vector3 positiveAxisZoom; // increase to view inner layers on the + side
    //Vector3 negativeAxisZoom; // for - side.
    float sumDeltaScrollTicks = 0;

    // UI Elements
    public Toggle transferFunctionToggle;
    public Text transparencyText;
    public Slider transparencyScalarSlider;
    public Text contrastText;
    public Slider contrastSlider;

    // Shader properties
    bool transferFunctionEnabled = true;
    float transparencyScalar = 0.0f;
    float contrast = 1.0f;

    public Vector3 lockPosition = Vector3.zero;
    public GameObject mainCamera;

    public bool dualControllerModeEnabled = true;
	public bool loadOnStart = false;

    void updateDetailsMeshVertices(bool updateToMesh = true) {
        if (detailsVertices == null) {
            detailsVertices = new Vector3[3][];
            detailsUvs = new Vector2[3][];
        }

        for (int axis = 0; axis < 3; axis++) {
            Vector3[] vertices = detailsVertices[axis];
            Vector2[] uvs = detailsUvs[axis];
            int meshTextureCount = layerPixels[axis];
            if (vertices == null || vertices.Length != meshTextureCount * 4) {
                vertices = new Vector3[meshTextureCount * 4]; // four vertices for a plane.
                uvs = new Vector2[meshTextureCount * 4];
                detailsVertices[axis] = vertices;
                detailsUvs[axis] = uvs;
            }

            int planeI = 0;
            // We subtract 0.5f / layerPixels[axis] so that crossing layers do not obscure each other
            // This amount is equal to half the distance between two consecutive layers -- effectively an epsilon
            float[] xyzMins = { meshSize[0] * (0.5f - 0.5f / layerPixels[0] - (float)rmPixelsXyz[0, 1] / layerPixels[0]),
                              -yAspectRatio * meshSize[1] * (0.5f - 0.5f / layerPixels[1] - (float)rmPixelsXyz[1, 0] / layerPixels[1]),
                              -meshSize[2] * (0.5f - 0.5f / layerPixels[2] - (float)rmPixelsXyz[2, 0] / layerPixels[2]) };
            float[] xyzMaxes = { -meshSize[0] * (0.5f - 0.5f / layerPixels[0] - (float)rmPixelsXyz[0, 0] / layerPixels[0]),
                                   yAspectRatio * meshSize[1] * (0.5f - 0.5f / layerPixels[1] - (float)rmPixelsXyz[1, 1] / layerPixels[1]),
                                   meshSize[2] * (0.5f - 0.5f / layerPixels[2] - (float)rmPixelsXyz[2, 1] / layerPixels[2]) };
            switch (axis) {
                case 0:
                    for (int i = 0; i < layerPixels[0]; i+=4) {
                        int vertI = planeI * 4;

                        if (i < rmPixelsXyz[0, 1] || i > layerPixels[0] - 1 - rmPixelsXyz[0, 0]) {
                            vertices[vertI] = Vector3.zero;
                            vertices[vertI + 1] = Vector3.zero;
                            vertices[vertI + 2] = Vector3.zero;
                            vertices[vertI + 3] = Vector3.zero;
                        } else {
                            float xVal = -((float)i / (layerPixels[0] - 1) - 0.5f) * meshSize[0];
                            vertices[vertI] = new Vector3(xVal, xyzMins[1], xyzMins[2]);
                            vertices[vertI + 1] = new Vector3(xVal, xyzMaxes[1], xyzMins[2]);
                            vertices[vertI + 2] = new Vector3(xVal, xyzMins[1], xyzMaxes[2]);
                            vertices[vertI + 3] = new Vector3(xVal, xyzMaxes[1], xyzMaxes[2]);
                        }

                        float zMin = (float)rmPixelsXyz[2, 0] / layerPixels[2];
                        float zMax = 1.0f - (float)rmPixelsXyz[2, 1] / layerPixels[2];
                        float yMin = (float)rmPixelsXyz[1, 0] / layerPixels[1];
                        float yMax = 1.0f - (float)rmPixelsXyz[1, 1] / layerPixels[1];
                        uvs[vertI] = new Vector2(zMin, yMin);
                        uvs[vertI + 1] = new Vector2(zMin, yMax);
                        uvs[vertI + 2] = new Vector2(zMax, yMin);
                        uvs[vertI + 3] = new Vector2(zMax, yMax);

                        planeI++;
                    }
                    break;
                case 1:
                    for (int i = 0; i < layerPixels[1]; i+= 4) {
                        int vertI = planeI * 4;

                        if (i < rmPixelsXyz[1, 0] || i > layerPixels[1] - 1 - rmPixelsXyz[1, 1]) {
                            vertices[vertI] = Vector3.zero;
                            vertices[vertI + 1] = Vector3.zero;
                            vertices[vertI + 2] = Vector3.zero;
                            vertices[vertI + 3] = Vector3.zero;
                        } else {
                            float yVal = ((float)i / (layerPixels[1] - 1) - 0.5f) * meshSize[1] * yAspectRatio;
                            vertices[vertI] = new Vector3(xyzMins[0], yVal, xyzMins[2]);
                            vertices[vertI + 1] = new Vector3(xyzMaxes[0], yVal, xyzMins[2]);
                            vertices[vertI + 2] = new Vector3(xyzMins[0], yVal, xyzMaxes[2]);
                            vertices[vertI + 3] = new Vector3(xyzMaxes[0], yVal, xyzMaxes[2]);
                        }

                        float zMin = (float)rmPixelsXyz[2, 0] / layerPixels[2];
                        float zMax = 1.0f - (float)rmPixelsXyz[2, 1] / layerPixels[2];
                        float xMin = (float)rmPixelsXyz[0, 0] / layerPixels[0];
                        float xMax = 1.0f - (float)rmPixelsXyz[0, 1] / layerPixels[0];
                        uvs[vertI] = new Vector2(zMin, xMax);
                        uvs[vertI + 1] = new Vector2(zMin, xMin);
                        uvs[vertI + 2] = new Vector2(zMax, xMax);
                        uvs[vertI + 3] = new Vector2(zMax, xMin);

                        planeI++;
                    }
                    break;
                case 2:
                    for (int i = 0; i < layerPixels[2]; i+=4) {
                        int vertI = planeI * 4;

                        if (i < rmPixelsXyz[2, 0] || i > layerPixels[2] - 1 - rmPixelsXyz[2, 1]) {
                            vertices[vertI] = Vector3.zero;
                            vertices[vertI + 1] = Vector3.zero;
                            vertices[vertI + 2] = Vector3.zero;
                            vertices[vertI + 3] = Vector3.zero;
                        } else {
                            float zVal = ((float)i / (layerPixels[2] - 1) - 0.5f) * meshSize[2];
                            vertices[vertI] = new Vector3(xyzMins[0], xyzMins[1], zVal);
                            vertices[vertI + 1] = new Vector3(xyzMaxes[0], xyzMins[1], zVal);
                            vertices[vertI + 2] = new Vector3(xyzMins[0], xyzMaxes[1], zVal);
                            vertices[vertI + 3] = new Vector3(xyzMaxes[0], xyzMaxes[1], zVal);
                        }
                        
                        float xMin = (float)rmPixelsXyz[0, 0] / layerPixels[0];
                        float xMax = 1.0f - (float)rmPixelsXyz[0, 1] / layerPixels[0];
                        float yMin = (float)rmPixelsXyz[1, 0] / layerPixels[1];
                        float yMax = 1.0f - (float)rmPixelsXyz[1, 1] / layerPixels[1];
                        uvs[vertI] = new Vector2(xMax, yMin);
                        uvs[vertI + 1] = new Vector2(xMin, yMin);
                        uvs[vertI + 2] = new Vector2(xMax, yMax);
                        uvs[vertI + 3] = new Vector2(xMin, yMax);

                        planeI++;
                    }
                    break;
            }

            if (updateToMesh) {
                detailsMeshes[axis].vertices = vertices;
                detailsMeshes[axis].uv = uvs;
                detailsColliders[axis].sharedMesh = null;
                detailsColliders[axis].sharedMesh = detailsMeshes[axis];
            }
        }
    }

    void makeDetailedViewMeshes() {
		Debug.Log ("calling make detail");
        if (detailsObjects == null || detailsMaterials == null) {
            detailsObjects = new GameObject[3];
            detailsMeshes = new Mesh[3];
            detailsRenderers = new MeshRenderer[3];
            detailsMaterials = new Material[3][];
            detailsColliders = new MeshCollider[3];
            detailsVertices = new Vector3[3][];
            detailsUvs = new Vector2[3][];
        }

        updateDetailsMeshVertices(false);

        for (int axis = 0; axis < 3; axis++) {
            Mesh mesh;
            MeshCollider collider;

            if (detailsObjects[axis] != null) {
                mesh = detailsMeshes[axis];
                collider = detailsColliders[axis];
            } else {
                GameObject obj = GameObject.Find("detailsMesh" + axis);
                if (obj) {
                    detailsObjects[axis] = obj;
                    mesh = obj.GetComponent<MeshFilter>().mesh;
                    detailsMeshes[axis] = mesh;

                    detailsRenderers[axis] = obj.GetComponent<MeshRenderer>();
                    collider = obj.GetComponent<MeshCollider>();
                    detailsColliders[axis] = collider;
                } else {
                    obj = new GameObject("detailsMesh" + axis);
                    // Use localRotation instead of rotation (which can also be set, but will implicitly subtract the parent's rotation)
                    obj.transform.parent = gameObject.transform;
                    obj.transform.localRotation = Quaternion.identity;
                    obj.transform.localPosition = Vector3.zero;

                    detailsObjects[axis] = obj;
                    SubmeshEvents submeshEvents = obj.AddComponent<SubmeshEvents>();
                    submeshEvents.buildMesh = this;

                    MeshFilter filter = obj.AddComponent<MeshFilter>();
                    mesh = filter.mesh;
                    detailsMeshes[axis] = mesh;

                    detailsRenderers[axis] = obj.AddComponent<MeshRenderer>();
                    detailsRenderers[axis].enabled = false;

                    Rigidbody body = obj.AddComponent<Rigidbody>();
                    body.useGravity = false;
                    body.isKinematic = true;

                    collider = obj.AddComponent<MeshCollider>();
                    detailsColliders[axis] = collider;
                }
            }
            
            mesh.subMeshCount = layerPixels[axis];

            // remove old triangles before changing vertices
            for (int i = 0; i < mesh.subMeshCount; i++) {
                mesh.SetTriangles(new int[0], i);
            }
            
            mesh.vertices = detailsVertices[axis];
            mesh.uv = detailsUvs[axis];
            
            for (int submeshI = 0; submeshI < layerPixels[axis]; submeshI++) {
                int[] triangles = new int[12];
                int vertI = submeshI * 4;
                triangles[0] = vertI;
                triangles[1] = vertI + 1;
                triangles[2] = vertI + 2;

                triangles[3] = vertI + 1;
                triangles[4] = vertI + 3;
                triangles[5] = vertI + 2;

                triangles[6] = vertI + 2;
                triangles[7] = vertI + 1;
                triangles[8] = vertI;

                triangles[9] = vertI + 2;
                triangles[10] = vertI + 3;
                triangles[11] = vertI + 1;

                mesh.SetTriangles(triangles, submeshI);
            }

            collider.sharedMesh = null;
            collider.sharedMesh = mesh;
        }
    }

    public void LoadDataset(string newDatasetDirectory) {
        datasetDirectory = newDatasetDirectory;
        loadLayerFiles();
        scaleFactor = 2f / layerWidth;

        // Try to load aspect ratio. Use default if it does not exist/can't be read.
        try {
            string aspectRatioPath = Path.Combine(Application.streamingAssetsPath, datasetDirectory);
            aspectRatioPath = Path.Combine(aspectRatioPath, "aspect_ratio");
            float.TryParse(File.ReadAllLines(aspectRatioPath)[0], out yAspectRatio);
        } catch (IOException) {
        } catch (IsolatedStorageException) { }

        Recreate(true);
    }

    void loadLayerFiles() {
		Debug.Log ("loading from: " + datasetDirectory);
        // The files in the designated folder have the source y-layers
        // Absolute dataset paths will ignore the streaming assets path

        string path = Path.Combine(Application.streamingAssetsPath, datasetDirectory);

        if (!Directory.Exists(path)) {
            Debug.Log("Directory does not exist");
            return;
        }

        // Try to release old textures from memory, if any
        foreach (Texture tex in cachedTextures) {
            Destroy(tex);
        }
        cachedTextures.Clear();
        cachedTexturePlanes.Clear();

        string[] files = Directory.GetFiles(path, "*.jpg");
        if (files.Length == 0) {
            files = Directory.GetFiles(path, "*.png");
        }
        layers = new Texture2D[files.Length];

        for (int i = 0; i < files.Length; i++) {
            Texture2D tex = new Texture2D(1, 1);
            tex.LoadImage(File.ReadAllBytes(files[i]));
            layers[i] = tex;

            cachedTextures.Add(tex);
            cachedTexturePlanes.Add(new Plane(new Vector3(0, 1, 0), 1f - (float)i / (files.Length - 1)));
        }

        if (layers.Length == 0) {
            layerWidth = 0;
            layerHeight = 0;
            layerNumber = 0;
        } else {
            layerWidth = layers[0].width;
            layerHeight = layers[0].height;
            layerNumber = layers.Length;
        }

        layerPixels = new int[3] { layerHeight, layerNumber, layerWidth };
    }

    Texture2D textureForPlane(Plane plane) {
        // Look for the texture in the cache
        float sqrEpsilon = 1e-8f;
        int index = cachedTexturePlanes.FindIndex(p => (plane.normal - p.normal).sqrMagnitude < sqrEpsilon && Math.Pow(plane.distance - p.distance, 2) < sqrEpsilon);
        if (index != -1) {
            return cachedTextures[index];
        }

        Texture2D tex = null;
        if (plane.normal.x == 1f) {
            tex = new Texture2D(layerWidth, layerNumber, TextureFormat.RGBA32, false);
            int xLevel = (int)((1 - plane.distance) * (layerHeight - 1) + 0.5f);
            for (int i = 0; i < layerNumber; i++) {
                Color[] line = layers[i].GetPixels(0, xLevel, layerWidth, 1);
                tex.SetPixels(0, layerNumber - 1 - i, layerWidth, 1, line);
            }
        } else if (plane.normal.y == 1f) {
            tex = layers[(int)((1 - plane.distance) * (layerNumber - 1) + 0.5f)];
        } else if (plane.normal.z == 1f) {
            tex = new Texture2D(layerHeight, layerNumber, TextureFormat.RGBA32, false);
            int zLevel = (int)(plane.distance * (layerWidth - 1) + 0.5f);
            for (int i = 0; i < layerNumber; i++) {
                Color[] line = layers[i].GetPixels(zLevel, 0, 1, layerHeight);
                tex.SetPixels(0, layerNumber - 1 - i, layerHeight, 1, line);
            }
        } else {
            // Error!
            return null;
        }
        tex.Apply();

        cachedTextures.Add(tex);
        cachedTexturePlanes.Add(plane);

        return tex;
    }

    void setupTextures(bool forceReset = false) {
        // do nothing if not yet loaded
        if (detailsObjects == null || detailsObjects.Length == 0) {
            return;
        }

        Material baseMaterial = baseRenderer.material;

        //cachedTexturePlanes.Clear();
        //cachedTextures.Clear();
        
        // Set the textures. We should only ever do this once, because it is so slow!
        // This mesh shouldn't ever really change so we only do this if absolutely necessary.
        if (forceReset || detailsMaterials[0] == null || detailsMaterials[0][0].mainTexture == null) {
            for (int axis = 0; axis < 3; axis++) {
                detailsMaterials[axis] = new Material[detailsMeshes[axis].subMeshCount];
                
                for (int i = 0; i < detailsMaterials[axis].Length; i++) {
                    detailsMaterials[axis][i] = new Material(baseMaterial);
                    detailsMaterials[axis][i].shader = baseMaterial.shader;
                    detailsMaterials[axis][i].hideFlags = HideFlags.HideAndDontSave | HideFlags.HideInInspector;
                }

                Vector3 planeDirection = new Vector3(axis == 0 ? 1 : 0, axis == 1 ? 1 : 0, axis == 2 ? 1 : 0);

                int count = layerPixels[axis];
                for (int submeshI = 0; submeshI < count; submeshI++) {
                    detailsMaterials[axis][submeshI].mainTexture = textureForPlane(new Plane(planeDirection, (float)submeshI / (count - 1)));
                }
                detailsRenderers[axis].materials = detailsMaterials[axis];
            }
        }
    }

    void updateRenderOrder(bool force = false) {
        // do nothing if not yet loaded
        if (detailsObjects == null || detailsObjects.Length == 0) {
            return;
        }

        // Vector from camera to object
        Vector3 cameraDirection = gameObject.transform.InverseTransformDirection(mainCamera.transform.forward);
        float[] cameraDirXyz = new float[3] { cameraDirection.x, cameraDirection.y, cameraDirection.z };

        int mainAxis;
        int mainAxisSign;
        getMainAxisAndSign(out mainAxis, out mainAxisSign);

        if (!force && materialsMainAxis == mainAxis && materialsAxisSign == mainAxisSign) {
            return;
        }
        materialsMainAxis = mainAxis;
        materialsAxisSign = mainAxisSign;

        for (int axis = 0; axis < 3; axis++) {
            bool isMain = axis == mainAxis;
            detailsRenderers[axis].enabled = isMain;
            if (isMain) {
                int count = layerPixels[axis];
                for (int submeshI = 0; submeshI < count; submeshI++) {
                    int drawIndex = (int)(2000 * -cameraDirXyz[axis] * submeshI / count * (axis == 0 ? -1 : 1)) + 2000;
                    if (detailsMaterials[axis].Length <= submeshI) {
                        drawIndex++;
                    }
                    detailsMaterials[axis][submeshI].renderQueue = 3000 + drawIndex;
                }
            }
        }
    }

    // Use this for initialization
    void Start() {
        baseRenderer = GetComponent<MeshRenderer>();
        gameObject.transform.position = lockPosition;
		if (loadOnStart) {
			LoadDataset ("D:/VRData/human_kidney_png");
		};
    }

    float constrain(float val, float min, float max) {
        return Math.Min(max, Math.Max(min, val));
    }

    void Recreate(bool forceResetTextures = false) {
        // nothing loaded yet
        if (layerNumber == 0) {
            return;
        }

        meshSize = new float[3]{ scaleFactor * layerWidth, scaleFactor * layerNumber, scaleFactor * layerHeight };
        makeDetailedViewMeshes();

        setupTextures(forceResetTextures);

        // reset back to nothing removed, because the whole mesh has changed
        rmLayersXyz = new int[3, 2];

        updateRenderOrder(true);

        updateShaderProperties();
    }

    bool mouseOverUI() {
        List<RaycastResult> raycastResults = new List<RaycastResult>();
        PointerEventData eventData = new PointerEventData(EventSystem.current);
        eventData.position = Input.mousePosition;
        EventSystem.current.RaycastAll(eventData, raycastResults);
        return raycastResults.Count > 0;
    }

    public void OnMouseDown() {
        if (mouseOverUI()) {
            return;
        }
        triggerDown(Input.mousePosition);
    }

    public int getMaxIndex(Vector3 v) {
        if (Mathf.Abs(v.x) > Mathf.Abs(v.y)) {
            if (Mathf.Abs(v.x) > Mathf.Abs(v.z)) {
                return v.x > 0 ? 1 : -1;
            }
            return v.z > 0 ? 3 : -3;
        } else if (Mathf.Abs(v.y) > Mathf.Abs(v.z)) {
            return v.y > 0 ? 2 : -2;
        }
        return v.z > 0 ? 3 : -3;
    }

    public void OnMouseDrag() {
        triggerHeld(Input.mousePosition, true);
    }

    public void OnMouseUp() {
        triggerUp();
    }
    // returns main axis of vector (x=1,y=2,z=3). if the direction of main axis is negative, return the result as negative
    void getMainAxis(Vector3 vec, out float mainAxis, out float mainVal) {
        mainAxis = 1;
        mainVal = vec.x;
        if (Mathf.Abs(vec.y) > Mathf.Abs(mainVal)) {
            mainAxis = 2;
            mainVal = vec.y;
        }
        if (Mathf.Abs(vec.z) > Mathf.Abs(mainVal)) {
            mainAxis = 3;
            mainVal = vec.z;
        }
        mainAxis = mainVal > 0 ? mainAxis : -mainAxis;
    }

    public void dualControllerHandler(Vector3 dominantPosition, Vector3 nonDominantPosition, Quaternion controllerRotation) {
        Debug.Log("entered dualcontrollerhandler");
        Vector3 offsetDominant = dominantPosition - dominantLastPosition;
        Vector3 offsetNonDominant = nonDominantPosition - nonDominantLastPosition;
        float mainAxisDominant, mainAxisNonDominant, mainValDominant, mainValNonDominant;
        getMainAxis(offsetDominant, out mainAxisDominant, out mainValDominant);
        getMainAxis(offsetNonDominant, out mainAxisNonDominant, out mainValNonDominant);
        if (mainAxisDominant == mainAxisNonDominant && Mathf.Abs(mainValDominant / mainValNonDominant) - 1 < 0.6) {
            // if same axis and direction, and approximately same magnitude, we translate
            // need to play around with the constant here
            Vector3 thisTranslation = (offsetDominant + offsetNonDominant) * 2;
            gameObject.transform.position += thisTranslation;
            //Debug.Log("translated: " + thisTranslation);
        } else {
            // otherwise, we rotate
            Quaternion interRotation = Quaternion.FromToRotation(dominantLastPosition - nonDominantLastPosition, dominantPosition - nonDominantPosition);
            gameObject.transform.rotation = interRotation * gameObject.transform.rotation;
            //Debug.Log("rotated: " + interRotation);
        }
        dominantLastPosition = dominantPosition;
        nonDominantLastPosition = nonDominantPosition;
    }

    public void triggerHeldRotation(Vector3 currentPosition) {
        if (isRotating) {
            //gameObject.transform.rotation = Quaternion.LookRotation(gameObject.transform.position - currentPosition);
            Quaternion rotation = Quaternion.FromToRotation(dragStartPosition - gameObject.transform.position, currentPosition - gameObject.transform.position);
            gameObject.transform.rotation = rotation * rotation * rotation * rotation * rotation * rotation * gameObject.transform.rotation;
            dragStartPosition = currentPosition;
        }
    }

    public void triggerDown(Vector3 initialPosition) {
        // If the allTriangles array has been mangled by a code reload, recreate all the missing arrays
        if (detailsObjects == null || detailsObjects.Length == 0) {
            Recreate();
        }

        isRotating = true;
        dragStartPosition = initialPosition;
    }

    public void triggerHeld(Vector3 currentPosition, bool useMouseSensitivity = false) {
        if (isRotating) {
            Vector3 change = (currentPosition - dragStartPosition) * (useMouseSensitivity ? mouseRotationSensitivity : triggerRotationSensitivity);
            gameObject.transform.Rotate(change.y, -change.x, 0, Space.World);
            updateRenderOrder();

            dragStartPosition = currentPosition;
        }
    }

    public void triggerUp() {
        isRotating = false;

        // Find rotation to snap to
        Vector3 rot = gameObject.transform.rotation.eulerAngles;
        rot.x = (float)Math.Round(rot.x / 90f) * 90;
        rot.y = (float)Math.Round(rot.y / 90f) * 90;
        rot.z = (float)Math.Round(rot.z / 90f) * 90;
        snappingRotation = Quaternion.Euler(rot);
    }

    public void setTransferFunctionEnabled(bool enabled) {
		Debug.Log ("setting transfer function");
        transferFunctionEnabled = enabled;
        updateShaderProperties();
    }

    public void setDualMode(bool enabled) {
        dualControllerModeEnabled = enabled;
    }

    public void setTransparencyScalar(float transparency) {
        transparencyScalar = transparency;
        transparencyText.text = "Transparency: " + transparency.ToString("F1");
        updateShaderProperties();
    }

    public void setContrast(float value) {
        contrast = value;
        contrastText.text = "Contrast: " + contrast.ToString("F1");
        updateShaderProperties();
    }

    public void resetAll() {
        gameObject.transform.rotation = new Quaternion();
        gameObject.transform.position = Vector3.zero;
        snappingRotation = new Quaternion();

        transferFunctionToggle.isOn = true;
        transparencyScalarSlider.value = 0.5f;
        contrastSlider.value = 1.0f;

        rmPixelsXyz = new int[3, 2];
        rmLayersXyz = new int[3, 2];
        Recreate();

        updateRenderOrder();
    }

    public void updateShaderProperties() {
        // do nothing if not yet loaded
        if (detailsObjects == null || detailsObjects.Length == 0) {
            return;
        }

        for (int axis = 0; axis < 3; axis++) {
            float axisTransparency = transparencyScalar * (axis != 1 ? yAspectRatio : 1);
            for (int matI = 0; matI < detailsMaterials[axis].Length; matI++) {
                detailsMaterials[axis][matI].SetInt("_UseTransferFunction", transferFunctionEnabled ? 1 : 0);
                detailsMaterials[axis][matI].SetFloat("_TransparencyScalar", axisTransparency);
                detailsMaterials[axis][matI].SetFloat("_Contrast", contrast * 3);
            }
        }
    }

    public void zoomIn(float increment) {
        zoomIncrements += increment;
    }

    bool floatEquals(float a, float b) {
        float epsilon = 1e-5f;
        return Math.Abs(a - b) < epsilon;
    }

    void getMainAxisAndSign(out int axis, out int sign) {
        Vector3 direction = gameObject.transform.InverseTransformDirection(mainCamera.transform.forward);

        float absX = Math.Abs(direction.x);
        float absY = Math.Abs(direction.y);
        float absZ = Math.Abs(direction.z);

        if (absX > absY && absX > absZ) {
            axis = 0;
            sign = direction.x < 0 ? 1 : 0;
        } else if (absY > absZ) {
            axis = 1;
            sign = direction.y < 0 ? 1 : 0;
        } else {
            axis = 2;
            sign = direction.z < 0 ? 1 : 0;
        }
    }

    void newGetMainAxisAndSign(out int axis, out int sign) {
        Vector3 cameraDirection = gameObject.transform.InverseTransformDirection(mainCamera.transform.forward);
        Vector3 cameraPosition = gameObject.transform.InverseTransformDirection(mainCamera.transform.position);
        float[] pos = new float[3] { cameraPosition.x, cameraPosition.y, cameraPosition.z };
        bool[] possibleAxes = new bool[3];
        for (int i = 0; i < 3; i++) {
            float dim = layerPixels[i] * scaleFactor / 2;
            possibleAxes[i] = pos[i] < -dim || pos[i] > dim;
        }

        float absX = Math.Abs(cameraPosition.x);
        float absY = Math.Abs(cameraPosition.y);
        float absZ = Math.Abs(cameraPosition.z);

        if (absX > absY && absX > absZ && possibleAxes[0]) {
            axis = 0;
            sign = cameraDirection.x < 0 ? 1 : 0;
        } else if (absY > absZ && possibleAxes[1]) {
            axis = 1;
            sign = cameraDirection.y < 0 ? 1 : 0;
        } else {
            axis = 2;
            sign = cameraDirection.z < 0 ? 1 : 0;
        }
    }

    public void orthogonalScroll(int pixels) {
        Debug.Log("scrolling");
        // vector from camera to object
        int axis;
        int sign;
        getMainAxisAndSign(out axis, out sign);

        // Keep the end number of pixels non-zero so the mesh never completely disappears
        int newRmPixels = (int)constrain(rmPixelsXyz[axis, sign] + pixels, 0, layerPixels[axis] - 1 - rmPixelsXyz[axis, (sign + 1) % 2]);
        if (newRmPixels == rmPixelsXyz[axis, sign]) {
            return;
        }
        rmPixelsXyz[axis, sign] = newRmPixels;
        updateDetailsMeshVertices();
    }

    // Update is called once per frame
    void Update() {
        // do nothing if not yet loaded
        if (detailsObjects == null || detailsObjects.Length == 0) {
            return;
        }

        updateDetailsMeshVertices();
        updateRenderOrder(true);

        if (Input.GetKeyDown("left shift")) {
            switch (currentSliceMode) {
                case sliceMode.NONE:
                    currentSliceMode = sliceMode.VERT;

                    slicelineRenderer = gameObject.AddComponent<LineRenderer>();
                    slicelineRenderer.material = new Material(Shader.Find("Standard"));

                    slicelineRenderer.material.color = Color.red;

                    slicelineRenderer.startWidth = 0.01f;
                    slicelineRenderer.endWidth = 0.01f;
                    slicelineRenderer.startColor = Color.red;
                    slicelineRenderer.endColor = Color.red;
                    //slicelineRenderer.positionCount = 2;
                    break;
                case sliceMode.VERT:
                    currentSliceMode = sliceMode.HORIZ;
                    break;
                case sliceMode.HORIZ:
                    currentSliceMode = sliceMode.NONE;
                    Destroy(slicelineRenderer);
                    break;
            }
        }

        Ray ray2;
        RaycastHit rayHit2;
        switch (currentSliceMode) {
            case sliceMode.NONE:
                break;
            case sliceMode.VERT:
                ray2 = Camera.main.ScreenPointToRay(Input.mousePosition);
                Physics.Raycast(ray2, out rayHit2);
                slicelineRenderer.SetPosition(0, new Vector3(rayHit2.point.x, 1, rayHit2.point.z));
                slicelineRenderer.SetPosition(1, new Vector3(rayHit2.point.x, -1, rayHit2.point.z));
                break;
            case sliceMode.HORIZ:
                ray2 = Camera.main.ScreenPointToRay(Input.mousePosition);
                Physics.Raycast(ray2, out rayHit2);
                slicelineRenderer.SetPosition(0, new Vector3(1, rayHit2.point.y, rayHit2.point.z));
                slicelineRenderer.SetPosition(1, new Vector3(-1, rayHit2.point.y, rayHit2.point.z));
                break;
        }


        if (shouldSnap && !isRotating) {
            gameObject.transform.rotation = Quaternion.Slerp(gameObject.transform.rotation, snappingRotation, Time.deltaTime * 4);
        }
        if (shouldReset) {
            gameObject.transform.position = Vector3.Slerp(gameObject.transform.position, lockPosition, Time.deltaTime * 4);
        }

        if (Input.GetKey("left ctrl")) {
            // Zoom instead of go through layers
            zoomIncrements += Input.mouseScrollDelta.y / 10;
        } else {
            // Make scrolling slower, but allow the partial ticks to accumulate
            sumDeltaScrollTicks += -Input.mouseScrollDelta.y / 2;
            if (Math.Abs(sumDeltaScrollTicks) >= 1) {
                int ticks = (int)sumDeltaScrollTicks;
                sumDeltaScrollTicks -= ticks;
                orthogonalScroll(ticks);
            }
        }

        Vector3 newCameraPos = Camera.main.transform.position;
        float newZ = baseCameraZ + zoomIncrements;
        if (newCameraPos.z != newZ) {
            newCameraPos.z = newZ;
            Camera.main.transform.position = newCameraPos;
        }

        updateRenderOrder();
    }

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
