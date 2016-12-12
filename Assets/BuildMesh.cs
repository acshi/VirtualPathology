using UnityEngine;
using System.IO;
using System;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class BuildMesh : MonoBehaviour {
    string datasetDirectory = "human_kidney_png";

    // Selects the layered view mode as opposed to the cube-based view mode
    public bool useDetailedViewMode = true;

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
    float transparencyScalar = 0.8f;
    float contrast = 1.0f;

    public Vector3 lockPosition = Vector3.zero;
    public GameObject mainCamera;

    public Canvas settingsCanvas;
    public bool dualControllerModeEnabled = false;

    void makeMeshCubes() {
        // Number of cubes to make in each dimension: x, y, z
        cubeCounts = new int[] { layerHeight / subcubeSize, (int)Math.Ceiling(yAspectRatio * layerNumber / subcubeSize), layerWidth / subcubeSize };
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
                    // Use localRotation instead of rotation (which can also be set, but will implicitly subtract the parent's rotation)
                    obj.transform.parent = gameObject.transform;
                    obj.transform.localRotation = Quaternion.identity;
                    obj.transform.localPosition = Vector3.zero;

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
                                    vertices[vertI] = new Vector3(meshSize[0] * (cubeXMinMax[x] - 0.5f),
                                                                  meshSize[1] * (cubeYMinMax[y] - 0.5f) * yAspectRatio,
                                                                  meshSize[2] * (cubeZMinMax[z] - 0.5f));
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
                    for (int i = 0; i < layerPixels[0]; i++) {
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
                    for (int i = 0; i < layerPixels[1]; i++) {
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
                    for (int i = 0; i < layerPixels[2]; i++) {
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
        if (detailsObjects == null) {
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
        } catch (IOException) {}

        Recreate(true);
    }

    void loadLayerFiles() {
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

        string[] files = Directory.GetFiles(path, "*.png");
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
        if (gameObjects == null || gameObjects.Length == 0) {
            return;
        }

        Material baseMaterial = baseRenderer.material;

        //cachedTexturePlanes.Clear();
        //cachedTextures.Clear();
        
        // Set the textures. We should only ever do this once, because it is so slow!
        // This mesh shouldn't ever really change so we only do this if absolutely necessary.
        if (forceReset || detailsMaterials[0][0].mainTexture == null) {
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

        // Only update when the textures should change due to a different detail level
        if (renderers[0].materials.Length != meshes[0].subMeshCount) {
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

            for (int meshI = 0; meshI < meshes.Length; meshI++) {
                MeshRenderer meshRend = renderers[meshI];

                int submeshI = 0;
                // loop x, y, z
                for (int axis = 0; axis < 3; axis++) {
                    Vector3 planeDirection = new Vector3(axis == 0 ? 1 : 0, axis == 1 ? 1 : 0, axis == 2 ? 1 : 0);

                    int count = cubeCounts[axis];
                    // Only pass through those index values for this specific set of z values
                    for (int i = (axis == 2) ? meshI * zPerMesh : 0; i < count && ((axis == 2) ? i < (meshI + 1) * zPerMesh : true); i++) {
                        meshRend.materials[submeshI].mainTexture = textureForPlane(new Plane(planeDirection, (float)i / (count - 1)));
                        submeshI++;
                    }
                }
            }
        }
    }

    void updateRenderOrder(bool force = false) {
        // do nothing if not yet loaded
        if (gameObjects == null || gameObjects.Length == 0) {
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

        if (useDetailedViewMode) {
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
        } else {
            for (int meshI = 0; meshI < meshes.Length; meshI++) {
                MeshRenderer meshRend = renderers[meshI];

                int submeshI = 0;
                // loop x, y, z
                for (int axis = 0; axis < 3; axis++) {
                    bool notMain = axis != mainAxis;

                    int count = cubeCounts[axis];
                    // Only pass through those index values for this specific set of z values
                    for (int i = (axis == 2) ? meshI * zPerMesh : 0; i < count && ((axis == 2) ? i < (meshI + 1) * zPerMesh : true); i++) {
                        // Some of these numbers are arbitrary, but the important thing is that the faces
                        // render back to front, and that the main axis faces also render before the other axes
                        int drawIndex = (int)(2000 * -cameraDirXyz[axis] * i / count * (axis == 0 ? -1 : 1)) + 2000;
                        meshRend.materials[submeshI].renderQueue = 3000 + drawIndex + (notMain ? 3000 : 0);
                        submeshI++;
                    }
                }
            }
        }
    }

    // Use this for initialization
    void Start() {
        baseRenderer = GetComponent<MeshRenderer>();
        gameObject.transform.position = lockPosition;
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
        makeMeshCubes();
        makeDetailedViewMeshes();

        setupTextures(forceResetTextures);

        // reset back to nothing removed, because the whole mesh has changed
        rmLayersXyz = new int[3, 2];

        updateRenderOrder(true);

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
        if (mouseOverUI()) {
            return;
        }
        triggerDown(Input.mousePosition);

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
        if (allTriangles == null || allTriangles[0] == null) {
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
        //snappingRotation = Quaternion.Euler(rot);
    }

    public void setTransferFunctionEnabled(bool enabled) {
        transferFunctionEnabled = enabled;
        updateShaderProperties();
    }

    public void toggleDualMode(bool enabled) {
        dualControllerModeEnabled = !dualControllerModeEnabled;
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
        if (gameObjects == null || gameObjects.Length == 0) {
            return;
        }

        for (int meshI = 0; meshI < meshes.Length; meshI++) {
            Material[] materials = renderers[meshI].materials;
            for (int matI = 0; matI < materials.Length; matI++) {
                materials[matI].SetInt("_UseTransferFunction", transferFunctionEnabled ? 1 : 0);
                materials[matI].SetFloat("_TransparencyScalar", transparencyScalar);
                materials[matI].SetFloat("_Contrast", contrast * 3);
            }
        }

        for (int axis = 0; axis < 3; axis++) {
            for (int matI = 0; matI < detailsMaterials.Length; matI++) {
                detailsMaterials[axis][matI].SetInt("_UseTransferFunction", transferFunctionEnabled ? 1 : 0);
                detailsMaterials[axis][matI].SetFloat("_TransparencyScalar", transparencyScalar);
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
        if (useDetailedViewMode) {
            updateDetailsMeshVertices();
        }

        // Derive the number of larger cube layers removed from the number of pixels removed
        int newRmLayers = (int)((float)newRmPixels * cubeCounts[axis] / layerPixels[axis]);
        // Keep the end number of layers such that the two ends of the cube do not pass each other, or go beyond the cube
        newRmLayers = (int)constrain(newRmLayers, 0, cubeCounts[axis] - 1 - rmLayersXyz[axis, (sign + 1) % 2]);
        // The number to actually modify now
        int layerCount = Math.Abs(newRmLayers - rmLayersXyz[axis, sign]);

        if (layerCount == 0) {
            return;
        }

        for (int l = 0; l < layerCount; l++) {
            // We subtract an extra layer here because you when layers is negative
            // we are actually adding the _next_ layer back, which is not visible
            // When removing layers, we remove the current visible layer, so the number isn't changed.
            int deltaL = pixels < 0 ? -l - 1 : l;
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
                        if (pixels > 0) {
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
    }

    // Update is called once per frame
    void Update() {
        // do nothing if not yet loaded
        if (gameObjects == null || gameObjects.Length == 0) {
            return;
        }

        // Manage switching between view modes
        if (useDetailedViewMode && renderers[0].enabled) {
            updateDetailsMeshVertices();
            // The correct detail renderer to enable will be done in this function
            updateRenderOrder(true);
        } else if (!useDetailedViewMode) {
            for (int axis = 0; axis < 3; axis++) {
                detailsRenderers[axis].enabled = false;
                detailsColliders[axis].enabled = false;
            }
        }

        bool shouldUpdateRenderOrder = false;
        for (int i = 0; i < gameObjects.Length; i++) {
            bool hasChange = renderers[i].enabled == useDetailedViewMode;
            if (hasChange) {
                shouldUpdateRenderOrder = true;
                renderers[i].enabled = !useDetailedViewMode;
                colliders[i].enabled = !useDetailedViewMode;
            }
        }
        if (shouldUpdateRenderOrder) {
            updateRenderOrder(true);
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
