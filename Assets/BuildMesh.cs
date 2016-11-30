using UnityEngine;
using System.IO;
using System;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class BuildMesh : MonoBehaviour {
    public string ImageLayerDirectory = "human_kidney_png";
    public bool loadFromStreamingAssets = true;

    // Selects the layered view mode as opposed to the cube-based view mode
    public bool useDetailedViewMode = true;

    public float yAspectRatio = 2.8f;
    public int subcubeSize = 64; // each cube has x, y, and z of this dimension.
    const float rotationSensitivity = 0.3f;
    const float triggerRotationSensitivity = 300f;
    const float baseCameraZ = -2;

    Texture2D[] layers; // original image files -- the y axis
    float scaleFactor;
    int layerWidth; // Z
    int layerHeight; // X
    int layerNumber; // Y
    int[] layerPixels;

    // Number of cubes in each of x, y, and z axes
    int[] cubeCounts;

    // Each mesh will be a cuboid with full x and y dimensions, but will take a section of the total z
    // This way each mesh can have less than 65536 vertices.
    int zPerMesh;

    // Used as a reference only to set up the materials of the meshes
    MeshRenderer baseRenderer;
    LineRenderer slicelineRenderer;
    GameObject[] gameObjects;
    Mesh[] meshes;
    MeshRenderer[] renderers;
    MeshCollider[] colliders;
    Vector3[][] allVertices; // [meshIndex][vertexIndex]
    Vector3[][] allOriginalVertices;
    int[][][] allTriangles; // [meshIndex][subMeshIndex][triangleIndex]

    // For detailed view mode
    GameObject detailsObject;
    Mesh detailsMesh;
    MeshRenderer detailsRenderer;
    MeshCollider detailsCollider;

    List<Texture2D> cachedTextures = new List<Texture2D>();
    List<Plane> cachedTexturePlanes = new List<Plane>();

    // To minimize changes in updateMaterials
    int materialsMainAxis = -1; // xyz: 0, 1, 2
    int materialsAxisSign = -1; // +-: 0, 1

    bool isRotating = false;
    enum sliceMode { NONE, VERT, HORIZ };
    sliceMode currentSliceMode;
    Vector3 dragStartPosition;

    // How much to add to the local scale of the object
    // We use scale to effect zoom in a camera-independant manner
    public float zoomIncrements = 0;

    public bool shouldSnap = false;
    public bool shouldReset = false;
    Quaternion snappingRotation = new Quaternion();
    float snappingZoomIncrement = 0;

    int[,] rmLayersXyz = new int[3, 2]; // [xyz index, minMax index]
    //Vector3 positiveAxisZoom; // increase to view inner layers on the + side
    //Vector3 negativeAxisZoom; // for - side.
    float sumDeltaScrollTicks = 0;

    // UI Elements
    public Toggle transferFunctionToggle;
    public Text transparencyText;
    public Slider transparencyScalarSlider;
    public Text contrastText;
    public Slider contrastSlider;
    public Text qualityText;
    public Slider qualitySlider;

    // Shader properties
    bool transferFunctionEnabled = true;
    float transparencyScalar = 0.8f;
    float contrast = 1.0f;
    int quality = 2;

    void makeMeshCubes(float xl, float yl, float zl) {
        // Number of cubes to make in each dimension: x, y, z
        cubeCounts = new int[] { layerHeight / subcubeSize, (int)Math.Ceiling(yAspectRatio * layerNumber / subcubeSize), layerWidth / subcubeSize };

        Debug.Log(cubeCounts[0] + " " + cubeCounts[1] + " " + cubeCounts[2]);
        int totalCubeCount = cubeCounts[0] * cubeCounts[1] * cubeCounts[2];

        // Each mesh is limited to 65536 vertices. How many meshes are needed to cover all vertices?
        float meshesNeeded = (float)totalCubeCount * 24 / 65536;
        zPerMesh = (int)Math.Floor(cubeCounts[2] / meshesNeeded);
        int meshCount = (int)Math.Ceiling((float)cubeCounts[2] / zPerMesh);

        if (allTriangles == null || allTriangles.Length != meshCount) {
            allTriangles = new int[meshCount][][];
            allVertices = new Vector3[meshCount][];
            allOriginalVertices = new Vector3[meshCount][];
            gameObjects = new GameObject[meshCount];
            meshes = new Mesh[meshCount];
            renderers = new MeshRenderer[meshCount];
            colliders = new MeshCollider[meshCount];
        }

        for (int meshI = 0; meshI < meshCount; meshI++) {
            int zInMesh = Math.Min(zPerMesh, cubeCounts[2] - meshI * zPerMesh); // so last mesh takes remainder
            int meshTextureCount = cubeCounts[0] + cubeCounts[1] + zInMesh;
            int meshCubeCount = cubeCounts[0] * cubeCounts[1] * zInMesh;

            int[] meshCubeCounts = new int[3] { cubeCounts[0], cubeCounts[1], zInMesh };

            Mesh mesh;
            MeshCollider collider;
            if (gameObjects[meshI] == null) {
                GameObject obj = GameObject.Find("mesh" + meshI);
                if (obj) {
                    gameObjects[meshI] = obj;
                    mesh = obj.GetComponent<MeshFilter>().mesh;
                    meshes[meshI] = mesh;

                    renderers[meshI] = obj.GetComponent<MeshRenderer>();
                    collider = obj.GetComponent<MeshCollider>();
                    colliders[meshI] = collider;
                } else {
                    obj = new GameObject("mesh" + meshI);
                    // The child transform will implicitly take the parent one into account
                    // So for the child to have "zero" rotation, it should be set to the parent rotation
                    obj.transform.parent = gameObject.transform;
                    obj.transform.rotation = gameObject.transform.rotation;

                    gameObjects[meshI] = obj;
                    SubmeshEvents submeshEvents = obj.AddComponent<SubmeshEvents>();
                    submeshEvents.buildMesh = this;

                    MeshFilter filter = obj.AddComponent<MeshFilter>();
                    mesh = filter.mesh;
                    meshes[meshI] = mesh;

                    renderers[meshI] = obj.AddComponent<MeshRenderer>();

                    Rigidbody body = obj.AddComponent<Rigidbody>();
                    body.useGravity = false;
                    body.isKinematic = true;

                    collider = obj.AddComponent<MeshCollider>();
                    colliders[meshI] = collider;
                }
            } else {
                mesh = meshes[meshI];
                collider = colliders[meshI];
            }

            mesh.subMeshCount = meshTextureCount;
            if (allTriangles[meshI] == null || allTriangles[meshI].Length != mesh.subMeshCount) {
                allTriangles[meshI] = new int[mesh.subMeshCount][];
            }

            Vector3[] vertices = new Vector3[meshCubeCount * 24]; // three of each vertex for different textures
            Vector2[] uvs = new Vector2[meshCubeCount * 24];

            // Cube index is (cubeZI - meshI * zPerMesh) * (cubeCountY * cubeCountX) + cubeYI * cubeCountX + cubeCountX
            int cubeI = 0;
            for (int cubeZI = meshI * zPerMesh; cubeZI < (meshI + 1) * zPerMesh && cubeZI < cubeCounts[2]; cubeZI++) {
                // These are from 0 (min) to 1 (max) of the cube
                float[] cubeZMinMax = { (float)cubeZI / cubeCounts[2], (float)(cubeZI + 1) / cubeCounts[2] };
                for (int cubeYI = 0; cubeYI < cubeCounts[1]; cubeYI++) {
                    float[] cubeYMinMax = { (float)cubeYI / cubeCounts[1], (float)(cubeYI + 1) / cubeCounts[1] };
                    for (int cubeXI = 0; cubeXI < cubeCounts[0]; cubeXI++) {
                        float[] cubeXMinMax = { 1 - (float)cubeXI / cubeCounts[0], 1 - (float)(cubeXI + 1) / cubeCounts[0] };

                        int vertI = 24 * cubeI;
                        // three sets of identical vertices, starting at 0 (x planes), 8 (y planes), 16 (z planes).
                        // i&4 == 0 are -z, == 4 are +z
                        // i&2 == 0 are -y, == 2 are +y
                        // i&1 == 0 are -x, == 1 are +x
                        for (int z = 0; z <= 1; z++) {
                            for (int y = 0; y <= 1; y++) {
                                for (int x = 0; x <= 1; x++) {
                                    vertices[vertI] = new Vector3(xl * (cubeXMinMax[x] - 0.5f), yl * (cubeYMinMax[y] - 0.5f) * yAspectRatio, zl * (cubeZMinMax[z] - 0.5f));
                                    vertices[vertI + 8] = vertices[vertI];
                                    vertices[vertI + 16] = vertices[vertI];

                                    uvs[vertI] = new Vector2(cubeZMinMax[z], cubeYMinMax[y]);
                                    uvs[vertI + 8] = new Vector2(cubeZMinMax[z], cubeXMinMax[x]);
                                    uvs[vertI + 16] = new Vector2(cubeXMinMax[x], cubeYMinMax[y]);
                                    vertI++;
                                }
                            }
                        }
                        cubeI++;
                    }
                }
            }

            // remove old triangles before changing vertices
            for (int i = 0; i < mesh.subMeshCount; i++) {
                mesh.SetTriangles(new int[0], i);
            }

            Debug.Log("CubeI: " + cubeI);
            allVertices[meshI] = vertices;
            allOriginalVertices[meshI] = (Vector3[])vertices.Clone();
            mesh.vertices = vertices;
            mesh.uv = uvs;

            int submeshI = 0;
            // loop x, y, z
            int[] dimIs = new int[3];
            for (int i = 0; i < 3; i++) {
                // 1, 2, 4 are the index distances for x, y, and z in the vertices array.
                // These numbers can be added or not to the baseI below to get the desired vertex index.
                // Adding 1 or not would be the difference between a vertex with x = 1 or x = -1, and so forth.
                int heldDimI = 1 << i;
                int dim1I = 1 << ((i + 1) % 3);
                int dim2I = 1 << ((i + 2) % 3);

                int mainCount = meshCubeCounts[i];
                int otherCount1 = meshCubeCounts[(i + 1) % 3];
                int otherCount2 = meshCubeCounts[(i + 2) % 3];

                for (dimIs[0] = 0; dimIs[0] < mainCount; dimIs[0]++) {
                    int[] triangles = allTriangles[meshI][submeshI];
                    // E.g. If we are making faces pointing in the x-direction (i = 0)
                    // Then we will have yCubeCount*zCubeCount cubes to make
                    // But we are only making the 2 squares in the x-direction for each 
                    // That makes 4 triangles per cube, each with 3 vertices, so the array is
                    // yCubeCount * zCubeCount * 4 * 3 in length
                    if (triangles == null || triangles.Length != otherCount1 * otherCount2 * 12) {
                        triangles = new int[otherCount1 * otherCount2 * 12];
                        allTriangles[meshI][submeshI] = triangles;
                    }

                    int triangleI = 0;
                    // Only pass through those index values for this specific set of z values
                    for (dimIs[1] = 0; dimIs[1] < otherCount1; dimIs[1]++) {
                        for (dimIs[2] = 0; dimIs[2] < otherCount2; dimIs[2]++) {
                            int cubeXI = dimIs[(3 - i) % 3];
                            int cubeYI = dimIs[(4 - i) % 3];
                            int relativeCubeZI = dimIs[2 - i]; // equal to cubeZI - meshI * zPerMesh
                            int cubeIndex = relativeCubeZI * (cubeCounts[1] * cubeCounts[0]) + cubeYI * cubeCounts[0] + cubeXI;

                            // - (j=0) and + (j=1) sides
                            for (int j = 0; j < 2; j++) {
                                // i * 8 gives us a unique copy of vertices with uv values for our plane direction
                                int baseI = 24 * cubeIndex + i * 8 + heldDimI * j;
                                int vert0 = baseI;
                                int vert1 = baseI + dim1I;
                                int vert2 = baseI + dim2I;
                                int vert3 = baseI + dim1I + dim2I;

                                // Swapping the order of the indices gives the triangle a normal facing
                                // in the opposite direction. This is needed so that both cube planes face outward
                                triangles[triangleI + 0] = j == 0 ? vert0 : vert3;
                                triangles[triangleI + 1] = vert1;
                                triangles[triangleI + 2] = j == 0 ? vert3 : vert0;

                                triangles[triangleI + 3] = j == 0 ? vert3 : vert0;
                                triangles[triangleI + 4] = vert2;
                                triangles[triangleI + 5] = j == 0 ? vert0 : vert3;

                                triangleI += 6;
                            }
                        }
                    }

                    mesh.SetTriangles(triangles, submeshI);
                    submeshI++;
                }
            }
            collider.sharedMesh = mesh;
        }

        // remove excess mesh objects that might still exist
        // extra brackets because scoping already has these variable names
        {
            GameObject obj;
            int i = meshCount;
            while ((obj = GameObject.Find("mesh" + i))) {
                Destroy(obj);
                i++;
            }
        }
    }

    void makeDetailedViewMesh(float xl, float yl, float zl) {
        Mesh mesh;
        MeshCollider collider;
        if (detailsObject == null) {
            GameObject obj = GameObject.Find("detailsMesh");
            if (obj) {
                detailsObject = obj;
                mesh = obj.GetComponent<MeshFilter>().mesh;
                detailsMesh = mesh;

                detailsRenderer = obj.GetComponent<MeshRenderer>();
                collider = obj.GetComponent<MeshCollider>();
                detailsCollider = collider;
            } else {
                obj = new GameObject("detailsMesh");
                obj.SetActive(false);
                // The child transform will implicitly take the parent one into account
                // So for the child to have "zero" rotation, it should be set to the parent rotation
                obj.transform.parent = gameObject.transform;
                obj.transform.rotation = gameObject.transform.rotation;

                detailsObject = obj;
                SubmeshEvents submeshEvents = obj.AddComponent<SubmeshEvents>();
                submeshEvents.buildMesh = this;

                MeshFilter filter = obj.AddComponent<MeshFilter>();
                mesh = filter.mesh;
                detailsMesh = mesh;

                detailsRenderer = obj.AddComponent<MeshRenderer>();

                Rigidbody body = obj.AddComponent<Rigidbody>();
                body.useGravity = false;
                body.isKinematic = true;

                collider = obj.AddComponent<MeshCollider>();
                detailsCollider = collider;
            }
        } else {
            mesh = detailsMesh;
            collider = detailsCollider;
        }

        int meshTextureCount = layerPixels[0] + layerPixels[1] + layerPixels[2];
        mesh.subMeshCount = meshTextureCount;

        Vector3[] vertices = new Vector3[meshTextureCount * 4]; // four vertices for a plane.
        Vector2[] uvs = new Vector2[meshTextureCount * 4];

        int planeI = 0;
        float[] xyzMins = { -xl / 2, -yl / 2 * yAspectRatio, -zl / 2 };
        float[] xyzMaxes = { xl / 2, yl / 2 * yAspectRatio, zl / 2 };
        for (int i = 0; i < layerPixels[0]; i++) {
            int vertI = planeI * 4;
            float xVal = ((float)i / layerPixels[0] - 0.5f) * xl;
            vertices[vertI] = new Vector3(xVal, xyzMins[1], xyzMins[2]);
            vertices[vertI + 1] = new Vector3(xVal, xyzMaxes[1], xyzMins[2]);
            vertices[vertI + 2] = new Vector3(xVal, xyzMins[1], xyzMaxes[2]);
            vertices[vertI + 3] = new Vector3(xVal, xyzMaxes[1], xyzMaxes[2]);
            planeI++;
        }
        for (int i = 0; i < layerPixels[1]; i++) {
            int vertI = planeI * 4;
            float yVal = ((float)i / layerPixels[1] - 0.5f) * yl * yAspectRatio;
            vertices[vertI] = new Vector3(xyzMins[0], yVal, xyzMins[2]);
            vertices[vertI + 1] = new Vector3(xyzMaxes[0], yVal, xyzMins[2]);
            vertices[vertI + 2] = new Vector3(xyzMins[0], yVal, xyzMaxes[2]);
            vertices[vertI + 3] = new Vector3(xyzMaxes[0], yVal, xyzMaxes[2]);
            planeI++;
        }
        for (int i = 0; i < layerPixels[2]; i++) {
            int vertI = planeI * 4;
            float zVal = ((float)i / layerPixels[2] - 0.5f) * xl;
            vertices[vertI] = new Vector3(xyzMins[0], xyzMins[1], zVal);
            vertices[vertI + 1] = new Vector3(xyzMaxes[0], xyzMins[1], zVal);
            vertices[vertI + 2] = new Vector3(xyzMins[0], xyzMaxes[1], zVal);
            vertices[vertI + 3] = new Vector3(xyzMaxes[0], xyzMaxes[1], zVal);
            planeI++;
        }

        for (planeI = 0; planeI < meshTextureCount; planeI++) {
            int vertI = planeI * 4;
            uvs[vertI] = new Vector2(0, 0);
            uvs[vertI + 1] = new Vector2(1, 0);
            uvs[vertI + 2] = new Vector2(0, 1);
            uvs[vertI + 3] = new Vector2(1, 1);
        }

        // remove old triangles before changing vertices
        for (int i = 0; i < mesh.subMeshCount; i++) {
            mesh.SetTriangles(new int[0], i);
        }

        mesh.vertices = vertices;
        mesh.uv = uvs;

        int submeshI = 0;
        for (int axis = 0; axis < 3; axis++) {
            for (int i = 0; i < layerPixels[axis]; i++) {
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
                submeshI++;
            }
        }

        collider.sharedMesh = mesh;
    }

    void loadLayerFiles() {
        // The files in the designated folder have the source y-layers
        string path;
        if (loadFromStreamingAssets) {
            path = Path.Combine(Application.streamingAssetsPath, ImageLayerDirectory);
        } else {
            path = ImageLayerDirectory;
        }

        if (!Directory.Exists(path)) {
            Debug.Log("Directory does not exist");
            return;
        }

        string[] files = Directory.GetFiles(path, "*.png");
        layers = new Texture2D[files.Length];

        for (int i = 0; i < files.Length; i++) {
            Texture2D tex = new Texture2D(1, 1);
            tex.LoadImage(File.ReadAllBytes(files[i]));
            layers[i] = tex;

            cachedTextures.Add(tex);
            cachedTexturePlanes.Add(new Plane(new Vector3(0, 1, 0), 1f - (float)i / (files.Length - 1)));
        }

        layerWidth = layers[0].width;
        layerHeight = layers[0].height;
        layerNumber = layers.Length;

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

    void setupTextures() {
        Material baseMaterial = baseRenderer.material;

        for (int meshI = 0; meshI < meshes.Length; meshI++) {
            Mesh mesh = meshes[meshI];
            MeshRenderer meshRend = renderers[meshI];

            meshRend.materials = new Material[mesh.subMeshCount];
            for (int i = 0; i < meshRend.materials.Length; i++) {
                meshRend.materials[i] = new Material(baseMaterial);
                meshRend.materials[i].shader = baseMaterial.shader;
                meshRend.materials[i].hideFlags = HideFlags.HideAndDontSave | HideFlags.HideInInspector;
            }
        }
    }

    void updateMaterials() {
        // Camera direction in the local space of the mesh
        Vector3 cameraDirection = gameObject.transform.InverseTransformDirection(Camera.main.transform.forward);
        float[] cameraDirXyz = new float[3] { cameraDirection.x, cameraDirection.y, cameraDirection.z };

        int mainAxis;
        int mainAxisSign;
        getMainAxisAndSign(cameraDirection, out mainAxis, out mainAxisSign);

        if (materialsMainAxis == mainAxis && materialsAxisSign == mainAxisSign) {
            return;
        }
        materialsMainAxis = mainAxis;
        materialsAxisSign = mainAxisSign;

        if (useDetailedViewMode) {
            int submeshI = 0;
            for (int axis = 0; axis < 3; axis++) {
                Vector3 planeDirection = new Vector3(axis == 0 ? 1 : 0, axis == 1 ? 1 : 0, axis == 2 ? 1 : 0);
                bool notMain = axis != mainAxis;

                int count = layerPixels[axis];
                for (int i = 0; i < layerPixels[i]; i++) {
                    int drawIndex = (int)(2000 * -cameraDirXyz[axis] * i / count * (axis == 0 ? -1 : 1)) + 2000;
                    detailsRenderer.materials[submeshI].renderQueue = 3000 + drawIndex + (notMain ? 3000 : 0);
                    detailsRenderer.materials[submeshI].mainTexture = textureForPlane(new Plane(planeDirection, (float)i / (count - 1)));
                    submeshI++;
                }
            }
        } else {
            for (int meshI = 0; meshI < meshes.Length; meshI++) {
                MeshRenderer meshRend = renderers[meshI];

                int submeshI = 0;
                // loop x, y, z
                for (int axis = 0; axis < 3; axis++) {
                    Vector3 planeDirection = new Vector3(axis == 0 ? 1 : 0, axis == 1 ? 1 : 0, axis == 2 ? 1 : 0);
                    bool notMain = axis != mainAxis;

                    int count = cubeCounts[axis];
                    // Only pass through those index values for this specific set of z values
                    for (int i = (axis == 2) ? meshI * zPerMesh : 0; i < count && ((axis == 2) ? i < (meshI + 1) * zPerMesh : true); i++) {
                        // Some of these numbers are arbitrary, but the important thing is that the faces
                        // render back to front, and that the main axis faces also render before the other axes
                        int drawIndex = (int)(2000 * -cameraDirXyz[axis] * i / count * (axis == 0 ? -1 : 1)) + 2000;
                        meshRend.materials[submeshI].renderQueue = 3000 + drawIndex + (notMain ? 3000 : 0);
                        meshRend.materials[submeshI].mainTexture = textureForPlane(new Plane(planeDirection, (float)i / (count - 1)));
                        submeshI++;
                    }
                }
            }
        }
    }

    // Use this for initialization
    void Start() {
        loadLayerFiles();
        scaleFactor = 2f / layerWidth;

        baseRenderer = GetComponent<MeshRenderer>();

        Recreate();
    }

    float constrain(float val, float min, float max) {
        return Math.Min(max, Math.Max(min, val));
    }

    void Recreate() {
        cachedTextures.Clear();
        cachedTexturePlanes.Clear();

        makeMeshCubes(scaleFactor * layerWidth, scaleFactor * layerNumber, scaleFactor * layerHeight);
        makeDetailedViewMesh(scaleFactor * layerWidth, scaleFactor * layerNumber, scaleFactor * layerHeight);

        setupTextures();

        // reset back to nothing removed, because the whole mesh has changed
        rmLayersXyz = new int[3, 2];

        // Force updateMaterials to reset everything
        materialsMainAxis = -1;
        materialsAxisSign = -1;
        updateMaterials();

        updateShaderProperties();
    }

    void getMeshISubmeshITriangleI(RaycastHit rayHit, out int meshI, out int submeshI, out int triangleI) {
        int globalTriangleI = rayHit.triangleIndex * 3;
        int previousIndexSum = 0;
        Debug.Log("rayHit: " + rayHit);
        Debug.Log("rayHit.collider: " + rayHit.collider);
        Debug.Log(rayHit.collider.name);
        int meshNum = int.Parse(rayHit.collider.name.Substring("mesh".Length));
        for (int i = 0; i < allTriangles[meshNum].Length; i++) {
            int relativeI = globalTriangleI - previousIndexSum;
            if (relativeI < allTriangles[meshNum][i].Length) {
                meshI = meshNum;
                submeshI = i;
                triangleI = relativeI;
                return;
            }
            previousIndexSum += allTriangles[meshNum][i].Length;
        }
        meshI = -1;
        submeshI = -1;
        triangleI = -1;
    }

    public void xSlice(int y, int direction) {
        if (direction < 0) {
            for (int z_index = 0; z_index < cubeCounts[2]; z_index++) {
                for (int y_index = 0; y_index < y; y_index++) {
                    for (int x_index = 0; x_index < cubeCounts[0]; x_index++) {
                        int cubeIndex = z_index * cubeCounts[0] * cubeCounts[1] + y_index * cubeCounts[0] + x_index;
                        int meshI = cubeIndex / (cubeCounts[0] * cubeCounts[1] * zPerMesh);
                        cubeIndex = cubeIndex % (cubeCounts[0] * cubeCounts[1] * zPerMesh);
                        for (int i = 24 * cubeIndex; i < 24 * (cubeIndex + 1); i++) {
                            allVertices[meshI][i] = Vector3.zero;
                        }
                    }
                }
            }
        } else {
            for (int z_index = 0; z_index < cubeCounts[2]; z_index++) {
                for (int y_index = cubeCounts[1] - 1; y_index > y; y_index--) {
                    for (int x_index = 0; x_index < cubeCounts[0]; x_index++) {
                        int cubeIndex = z_index * cubeCounts[0] * cubeCounts[1] + y_index * cubeCounts[0] + x_index;
                        int meshI = cubeIndex / (cubeCounts[0] * cubeCounts[1] * zPerMesh);
                        cubeIndex = cubeIndex % (cubeCounts[0] * cubeCounts[1] * zPerMesh);
                        for (int i = 24 * cubeIndex; i < 24 * (cubeIndex + 1); i++) {
                            allVertices[meshI][i] = Vector3.zero;
                        }
                    }
                }
            }
        }
        for (int index = 0; index < 3; index++) {
            meshes[index].vertices = allVertices[index];
        }
    }

    public void ySlice(int x, int direction) {
        if (direction < 0) {
            for (int z_index = 0; z_index < cubeCounts[2]; z_index++) {
                for (int y_index = 0; y_index < cubeCounts[1]; y_index++) {
                    for (int x_index = 0; x_index < x; x_index++) {
                        int cubeIndex = z_index * cubeCounts[0] * cubeCounts[1] + y_index * cubeCounts[0] + x_index;
                        int meshI = cubeIndex / (cubeCounts[0] * cubeCounts[1] * zPerMesh);
                        cubeIndex = cubeIndex % (cubeCounts[0] * cubeCounts[1] * zPerMesh);
                        for (int i = 24 * cubeIndex; i < 24 * (cubeIndex + 1); i++) {
                            allVertices[meshI][i] = Vector3.zero;
                        }
                    }
                }
            }
        } else {
            for (int z_index = 0; z_index < cubeCounts[2]; z_index++) {
                for (int y_index = 0; y_index < cubeCounts[1]; y_index++) {
                    for (int x_index = cubeCounts[0] - 1; x_index > x; x_index--) {
                        int cubeIndex = z_index * cubeCounts[0] * cubeCounts[1] + y_index * cubeCounts[0] + x_index;
                        int meshI = cubeIndex / (cubeCounts[0] * cubeCounts[1] * zPerMesh);
                        cubeIndex = cubeIndex % (cubeCounts[0] * cubeCounts[1] * zPerMesh);
                        for (int i = 24 * cubeIndex; i < 24 * (cubeIndex + 1); i++) {
                            allVertices[meshI][i] = Vector3.zero;
                        }
                    }
                }
            }
        }
        for (int index = 0; index < 3; index++) {
            meshes[index].vertices = allVertices[index];
        }
    }

    public void zSlice(int z, int direction) {
        if (direction < 0) {
            for (int z_index = 0; z_index < z; z_index++) {
                for (int y_index = 0; y_index < cubeCounts[1]; y_index++) {
                    for (int x_index = 0; x_index < cubeCounts[0]; x_index++) {
                        int cubeIndex = z_index * cubeCounts[0] * cubeCounts[1] + y_index * cubeCounts[0] + x_index;
                        int meshI = cubeIndex / (cubeCounts[0] * cubeCounts[1] * zPerMesh);
                        cubeIndex = cubeIndex % (cubeCounts[0] * cubeCounts[1] * zPerMesh);
                        for (int i = 24 * cubeIndex; i < 24 * (cubeIndex + 1); i++) {
                            allVertices[meshI][i] = Vector3.zero;
                        }
                    }
                }
            }
        } else {
            for (int z_index = cubeCounts[2] - 1; z_index > z; z_index--) {
                for (int y_index = 0; y_index < cubeCounts[1]; y_index++) {
                    for (int x_index = 0; x_index < cubeCounts[0]; x_index++) {
                        int cubeIndex = z_index * cubeCounts[0] * cubeCounts[1] + y_index * cubeCounts[0] + x_index;
                        int meshI = cubeIndex / (cubeCounts[0] * cubeCounts[1] * zPerMesh);
                        cubeIndex = cubeIndex % (cubeCounts[0] * cubeCounts[1] * zPerMesh);
                        for (int i = 24 * cubeIndex; i < 24 * (cubeIndex + 1); i++) {
                            allVertices[meshI][i] = Vector3.zero;
                        }
                    }
                }
            }
        }
        for (int index = 0; index < 3; index++) {
            meshes[index].vertices = allVertices[index];
        }
    }

    public void removeCubeFromRay(RaycastHit rayHit) {
        if (rayHit.triangleIndex == -1 || rayHit.collider == null) {
            Debug.Log("BuildMesh.cs:removeCubeFromRay() triangleIndex is -1, or rayHit.collider is null!");
            return;
        }
        int meshI;
        int submeshI;
        int triangleI;
        getMeshISubmeshITriangleI(rayHit, out meshI, out submeshI, out triangleI);

        int vert1 = allTriangles[meshI][submeshI][triangleI + 0];
        int vert2 = allTriangles[meshI][submeshI][triangleI + 1];
        int vert3 = allTriangles[meshI][submeshI][triangleI + 2];
        int cubeIndex = Math.Min(vert1, Math.Min(vert2, vert3)) / 24;
        Debug.Log(new Vector3(cubeCounts[0], cubeCounts[1], cubeCounts[2]));
        Debug.Log(cubeIndex);
        Debug.Log(meshI);
        Debug.Log(gameObject.transform.rotation.eulerAngles);
        for (int i = 24 * cubeIndex; i < 24 * (cubeIndex + 1); i++) {
            allVertices[meshI][i] = Vector3.zero;
        }

        meshes[meshI].vertices = allVertices[meshI];

        meshes[meshI].SetTriangles(allTriangles[meshI][submeshI], submeshI);
        colliders[meshI].sharedMesh = null;
        colliders[meshI].sharedMesh = meshes[meshI];
    }

    bool mouseOverUI() {
        List<RaycastResult> raycastResults = new List<RaycastResult>();
        PointerEventData eventData = new PointerEventData(EventSystem.current);
        eventData.position = Input.mousePosition;
        EventSystem.current.RaycastAll(eventData, raycastResults);
        return raycastResults.Count > 0;
    }

    public void OnMouseDown() {
        // If the allTriangles array has been mangled by a code reload, recreate all the missing arrays
        if (allTriangles == null || allTriangles[0] == null) {
            Recreate();
        }

        if (mouseOverUI()) {
            return;
        }

        isRotating = true;
        dragStartPosition = Input.mousePosition;

        if (Input.GetKey("left ctrl")) {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit rayHit;
            if (Physics.Raycast(ray, out rayHit) && rayHit.triangleIndex != -1) {
                removeCubeFromRay(rayHit);
            }
        }
        Ray ray2;
        RaycastHit rayHit2;
        Vector3 axes;
        int maxIndex;
        int sign;
        switch (currentSliceMode) {
            case sliceMode.NONE:
                break;

            case sliceMode.VERT:
                ray2 = Camera.main.ScreenPointToRay(Input.mousePosition);
                Physics.Raycast(ray2, out rayHit2);
                //Debug.Log ("VERT QUATERNION");
                axes = Quaternion.Inverse(gameObject.transform.rotation) * Vector3.right;
                maxIndex = getMaxIndex(axes);
                sign = maxIndex > 0 ? 1 : -1;
                maxIndex = Mathf.Abs(maxIndex);
                float cubeX;
                if (maxIndex != 1) {
                    sign *= -1;
                }
                if (sign == -1) {
                    cubeX = (float)(rayHit2.point.x + 1.0) / 2.0f;
                } else {
                    cubeX = 1.0f - ((float)(rayHit2.point.x + 1.0) / 2.0f);
                }
                switch (maxIndex) {
                    case 2:
                        xSlice((int)((cubeX) * cubeCounts[1]), -1 * sign);
                        break;
                    case 1:
                        ySlice((int)(cubeX * cubeCounts[0]), -1 * sign);
                        break;
                    case 3:
                        zSlice((int)((cubeX) * cubeCounts[2]), -1 * sign);
                        break;
                }
                //Debug.Log (axes);
                break;
            case sliceMode.HORIZ:
                ray2 = Camera.main.ScreenPointToRay(Input.mousePosition);
                Physics.Raycast(ray2, out rayHit2);
                float cubeY;
                //Debug.Log ("HORIZ QUATERNION");
                axes = Quaternion.Inverse(gameObject.transform.rotation) * Vector3.up;
                maxIndex = getMaxIndex(axes);
                sign = maxIndex > 0 ? 1 : -1;
                maxIndex = Mathf.Abs(maxIndex);
                if (maxIndex != 2) {
                    sign *= -1;
                }
                if (sign == 1) {
                    cubeY = (float)(rayHit2.point.y + 1.0) / 2.0f;
                } else {
                    cubeY = 1.0f - ((float)(rayHit2.point.y + 1.0) / 2.0f);
                }
                switch (maxIndex) {
                    case 2:
                        xSlice((int)(cubeY * cubeCounts[1]), -1 * sign);
                        break;
                    case 1:
                        ySlice((int)(cubeY * cubeCounts[0]), -1 * sign);
                        break;
                    case 3:
                        zSlice((int)((1.0f - cubeY) * cubeCounts[2]), sign);
                        break;
                }
                //Debug.Log (axes);
                break;
        }
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
        if (isRotating) {
            Vector3 change = (Input.mousePosition - dragStartPosition) * rotationSensitivity;
            gameObject.transform.Rotate(change.y, -change.x, 0, Space.World);
            updateMaterials();

            updateSnapScale();

            dragStartPosition = Input.mousePosition;
        }
    }

    public void OnMouseUp() {
        isRotating = false;

        // Find rotation to snap to
        Vector3 rot = gameObject.transform.rotation.eulerAngles;
        rot.x = (float)Math.Round(rot.x / 90f) * 90;
        rot.y = (float)Math.Round(rot.y / 90f) * 90;
        rot.z = (float)Math.Round(rot.z / 90f) * 90;
        snappingRotation = Quaternion.Euler(rot);
    }

    //TODO: write some nice, modular code here instead of copy pasting
    public void triggerDown(Vector3 initialPosition) {
        // If the allTriangles array has been mangled by a code reload, recreate all the missing arrays
        if (allTriangles == null || allTriangles[0] == null) {
            Recreate();
        }

        //if (mouseOverUI()) {
        //	return;
        //}

        isRotating = true;
        dragStartPosition = initialPosition;
        Debug.Log("initialPosition" + initialPosition);
    }

    public void triggerHeld(Vector3 currentPosition) {

        if (isRotating) {
            //Debug.Log ("triggerHeld: isRotating");
            Vector3 change = (currentPosition - dragStartPosition) * triggerRotationSensitivity;
            //Debug.Log ("change: " + change);
            gameObject.transform.Rotate(change.y, -change.x, 0, Space.World);
            //gameObject.transform.rotation = Quaternion.FromToRotation((dragStartPosition - gameObject.transform.position), (currentPosition - gameObject.transform.position));
            updateMaterials();

            updateSnapScale();

            dragStartPosition = currentPosition;
        }
    }

    public void triggerUp() {
        isRotating = false;

        // Find rotation to snap to
        Vector3 rot = gameObject.transform.rotation.eulerAngles;
        Debug.Log("triggerUp, rot: " + rot);
        rot.x = (float)Math.Round(rot.x / 90f) * 90;
        rot.y = (float)Math.Round(rot.y / 90f) * 90;
        rot.z = (float)Math.Round(rot.z / 90f) * 90;
        snappingRotation = Quaternion.Euler(rot);
    }


    void updateSnapScale() {
        // Camera direction in the local space of the mesh
        Vector3 cameraDirection = gameObject.transform.InverseTransformDirection(Camera.main.transform.forward);

        int mainAxis;
        int mainAxisSign;
        getMainAxisAndSign(cameraDirection, out mainAxis, out mainAxisSign);

        int removedLayers = rmLayersXyz[mainAxis, mainAxisSign];
        int totalLayers = cubeCounts[mainAxis];
        float totalHeight = layerPixels[mainAxis] * scaleFactor;
        if (mainAxis == 1) {
            totalHeight *= yAspectRatio;
        }

        snappingZoomIncrement = totalHeight / totalLayers * removedLayers;
    }

    public void setTransferFunctionEnabled(bool enabled) {
        transferFunctionEnabled = enabled;
        updateShaderProperties();
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

    public void setQuality(float q) {
        quality = (int)q;
        qualityText.text = "Quality: " + quality;
        subcubeSize = new int[] { 70, 50, 30, 20 }[quality - 1];
        Recreate();
    }

    public void resetAll() {
        gameObject.transform.rotation = new Quaternion();
        snappingRotation = new Quaternion();

        transferFunctionToggle.isOn = true;
        transparencyScalarSlider.value = 0.5f;
        contrastSlider.value = 1.0f;
        qualitySlider.value = 2;

        updateMaterials();
    }

    public void updateShaderProperties() {
        for (int meshI = 0; meshI < meshes.Length; meshI++) {
            Material[] materials = renderers[meshI].materials;
            for (int matI = 0; matI < materials.Length; matI++) {
                materials[matI].SetInt("_UseTransferFunction", transferFunctionEnabled ? 1 : 0);
                materials[matI].SetFloat("_TransparencyScalar", transparencyScalar);
                materials[matI].SetFloat("_Contrast", contrast * 3);
            }
        }

        Material[] detailsMaterials = detailsRenderer.materials;
        for (int matI = 0; matI < detailsMaterials.Length; matI++) {
            detailsMaterials[matI].SetInt("_UseTransferFunction", transferFunctionEnabled ? 1 : 0);
            detailsMaterials[matI].SetFloat("_TransparencyScalar", transparencyScalar);
            detailsMaterials[matI].SetFloat("_Contrast", contrast * 3);
        }
    }

    public void zoomIn(float increment) {
        zoomIncrements += increment;
    }

    bool floatEquals(float a, float b) {
        float epsilon = 1e-5f;
        return Math.Abs(a - b) < epsilon;
    }

    Vector3 getViewNormal() {
        // Detect which face of the object is in front of the camera, return that normal
        RaycastHit rayHit;
        Physics.Raycast(Camera.main.transform.position, Camera.main.transform.forward, out rayHit);
        if (rayHit.transform == null) {
            return Vector3.zero;
        }
        Vector3 hitSurfaceNormal = rayHit.transform.InverseTransformDirection(rayHit.normal);
        return hitSurfaceNormal;
    }

    void getMainAxisAndSign(Vector3 direction, out int axis, out int sign) {
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

    public void orthogonalScroll(int layers) {
        Debug.Log("scroll");
        // Camera direction in the local space of the mesh
        Vector3 cameraDirection = gameObject.transform.InverseTransformDirection(Camera.main.transform.forward);

        int axis;
        int sign;
        getMainAxisAndSign(cameraDirection, out axis, out sign);

        // Keep the end number of layers such that the two ends of the cube do not pass each other, or go beyond the cube
        int newRmLayers = (int)constrain(rmLayersXyz[axis, sign] + layers, 0, cubeCounts[axis] - 1 - rmLayersXyz[axis, (sign + 1) % 2]);
        // The number to actually modify now
        int layerCount = Math.Abs(newRmLayers - rmLayersXyz[axis, sign]);

        if (layerCount == 0) {
            return;
        }

        for (int l = 0; l < layerCount; l++) {
            // We subtract an extra layer here because you when layers is negative
            // we are actually adding the _next_ layer back, which is not visible
            // When removing layers, we remove the current visible layer, so the number isn't changed.
            int deltaL = layers < 0 ? -l - 1 : l;
            // Cube index is (cubeZI - meshI * zPerMesh) * (cubeCountY * cubeCountX) + cubeYI * cubeCountX + cubeCountX
            int axis2 = (axis + 1) % 3;
            int axis3 = (axis + 2) % 3;

            int[] dimIs = new int[3] { 0, 0, 0 };
            // The dimension index of the layer we are removing
            bool countFromZero = sign == 0;
            countFromZero = axis == 0 ? !countFromZero : countFromZero;
            dimIs[axis] = countFromZero ? (rmLayersXyz[axis, sign] + deltaL) : (cubeCounts[axis] - 1 - rmLayersXyz[axis, sign] - deltaL);

            int dim2Min = rmLayersXyz[axis2, axis2 == 0 ? 1 : 0];
            int dim2Max = cubeCounts[axis2] - rmLayersXyz[axis2, axis2 == 0 ? 0 : 1];
            for (dimIs[axis2] = dim2Min; dimIs[axis2] < dim2Max; dimIs[axis2]++) {
                int dim3Min = rmLayersXyz[axis3, axis3 == 0 ? 1 : 0];
                int dim3Max = cubeCounts[axis3] - rmLayersXyz[axis3, axis3 == 0 ? 0 : 1];
                for (dimIs[axis3] = dim3Min; dimIs[axis3] < dim3Max; dimIs[axis3]++) {
                    int cubeXI = dimIs[0];
                    int cubeYI = dimIs[1];
                    int cubeZI = dimIs[2];
                    int meshI = cubeZI / zPerMesh;
                    int relativeCubeZI = cubeZI - meshI * zPerMesh;
                    int cubeIndex = relativeCubeZI * (cubeCounts[1] * cubeCounts[0]) + cubeYI * cubeCounts[0] + cubeXI;

                    for (int i = 24 * cubeIndex; i < 24 * (cubeIndex + 1); i++) {
                        if (layers > 0) {
                            allVertices[meshI][i] = Vector3.zero;
                        } else {
                            allVertices[meshI][i] = allOriginalVertices[meshI][i];
                        }
                    }
                }
            }
        }

        for (int meshI = 0; meshI < meshes.Length; meshI++) {
            meshes[meshI].vertices = allVertices[meshI];
        }

        rmLayersXyz[axis, sign] = newRmLayers;
        updateSnapScale();
    }

    // Update is called once per frame
    void Update() {
        // Manage switching between view modes
        if (useDetailedViewMode) {
            if (!detailsObject.activeSelf) {
                for (int i = 0; i < gameObjects.Length; i++) {
                    gameObjects[i].SetActive(false);
                }
                detailsObject.SetActive(true);
            }
        }

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
                    slicelineRenderer.numPositions = 2;
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
            gameObject.transform.position = Vector3.Slerp (gameObject.transform.position, Vector3.zero, Time.deltaTime * 4);
        }

        Vector3 newCameraPos = Camera.main.transform.position;
        float newZ = baseCameraZ + zoomIncrements;
        if (newCameraPos.z != newZ) {
            newCameraPos.z = newZ;
            Camera.main.transform.position = newCameraPos;
        }

        // Make scrolling slower, but allow the partial ticks to accumulate
        sumDeltaScrollTicks += -Input.mouseScrollDelta.y / 2;
        if (Math.Abs(sumDeltaScrollTicks) >= 1) {
            int ticks = (int)sumDeltaScrollTicks;
            sumDeltaScrollTicks -= ticks;
            orthogonalScroll(ticks);
        }
        updateMaterials();
    }
}
