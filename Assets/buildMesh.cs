using UnityEngine;
using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;

public class buildMesh : MonoBehaviour {
    public string ImageLayerDirectory = "human_kidney_png";

    public float yAspectRatio = 2.8f;
    public int subcubeSize = 64; // each cube has x, y, and z of this dimension.
    const float rotationSensitivity = 0.3f;

    Mesh proceduralMesh;
    MeshRenderer meshRend;
    MeshCollider meshCollider;
    Texture2D[] layers;
    //Texture2D[] planeTextures;
    float scaleFactor;
    int layerWidth; // Z
    int layerHeight; // X
    int layerNumber; // Y

    int[] cubeCounts;

    List<Texture2D> cachedTextures = new List<Texture2D>();
    List<Plane> cachedTexturePlanes = new List<Plane>();
    
    bool isRotating = false;
    Vector3 dragStartPosition;

    float[] xLayersMinMax = new float[] { 0, 1 };
    float[] yLayersMinMax = new float[] { 0, 1 };
    float[] zLayersMinMax = new float[] { 0, 1 };
    //Vector3 positiveAxisZoom; // increase to view inner layers on the + side
    //Vector3 negativeAxisZoom; // for - side.

    void makeMeshCubes(float xl, float yl, float zl, Mesh mesh) {
        mesh.Clear();

        // Number of cubes to make in each dimension: x, y, z
        cubeCounts = new int[] { layerHeight / subcubeSize, (int)Math.Ceiling(yAspectRatio * layerNumber / subcubeSize), layerWidth / subcubeSize };

        int totalTextureCount = cubeCounts[0] + cubeCounts[1] + cubeCounts[2];
        int totalCubeCount = cubeCounts[0] * cubeCounts[1] * cubeCounts[2];

        mesh.subMeshCount = totalTextureCount;

        Vector3[] vertices = new Vector3[totalCubeCount * 24]; // three of each vertex for different textures
        Vector2[] uvs = new Vector2[totalCubeCount * 24];
        
        // Cube index is cubeZI * (cubeCountY * cubeCountX) + cubeYI * cubeCountX + cubeCountX
        int cubeI = 0;
        for (int cubeZI = 0; cubeZI < cubeCounts[2]; cubeZI++) {
            // These are from 0 (min) to 1 (max) of the cube
            float[] cubeZMinMax = { (float)cubeZI / cubeCounts[2], (float)(cubeZI + 1) / cubeCounts[2] };
            for (int cubeYI = 0; cubeYI < cubeCounts[1]; cubeYI++) {
                float[] cubeYMinMax = { (float)cubeYI / cubeCounts[1], (float)(cubeYI + 1) / cubeCounts[1] };
                for (int cubeXI = 0; cubeXI < cubeCounts[0]; cubeXI++, cubeI++) {
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
                }
            }
        }
        mesh.vertices = vertices;
        mesh.uv = uvs;

        int meshI = 0;
        // loop x, y, z
        int[] dimIs = new int[3];
        for (int i = 0; i < 3; i++) {
            // 1, 2, 4 are the index distances for x, y, and z in the vertices array.
            // These numbers can be added or not to the baseI below to get the desired vertex index.
            // Adding 1 or not would be the difference between a vertex with x = 1 or x = -1, and so forth.
            int heldDimI = 1 << i;
            int dim1I = 1 << ((i + 1) % 3);
            int dim2I = 1 << ((i + 2) % 3);

            int mainCount = cubeCounts[i];
            int otherCount1 = cubeCounts[(i + 1) % 3];
            int otherCount2 = cubeCounts[(i + 2) % 3];

            for (dimIs[0] = 0; dimIs[0] < mainCount; dimIs[0]++) {
                int[] triangles = new int[otherCount1 * otherCount2 * 12];

                int triangleI = 0;
                for (dimIs[1] = 0; dimIs[1] < otherCount1; dimIs[1]++) {
                    for (dimIs[2] = 0; dimIs[2] < otherCount2; dimIs[2]++) {
                        int cubeXI = dimIs[(3 - i) % 3];
                        int cubeYI = dimIs[(4 - i) % 3];
                        int cubeZI = dimIs[2 - i];
                        int cubeIndex = cubeZI * (cubeCounts[1] * cubeCounts[0]) + cubeYI * cubeCounts[0] + cubeXI;

                        // - and + sides
                        for (int j = 0; j < 2; j++) {
                            int baseI = 24 * cubeIndex + i * 8 + heldDimI * j;
                            int vert0 = baseI;
                            int vert1 = baseI + dim1I;
                            int vert2 = baseI + dim2I;
                            int vert3 = baseI + dim1I + dim2I;
                            
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

                mesh.SetTriangles(triangles, meshI);
                meshI++;
            }
        }
    }

    void loadLayerFiles() {
        // The files in the designated folder have the source y-layers
        string path = Path.Combine(Application.streamingAssetsPath, ImageLayerDirectory);
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
        Material baseMaterial = meshRend.material;
        meshRend.materials = new Material[proceduralMesh.subMeshCount];
        for (int i = 0; i < meshRend.materials.Length; i++) {
            meshRend.materials[i] = new Material(baseMaterial);
            meshRend.materials[i].shader = baseMaterial.shader;
            meshRend.materials[i].hideFlags = HideFlags.HideAndDontSave | HideFlags.HideInInspector;
            //meshRend.materials[i].mainTexture = planeTextures[i];
            // Have the cubes render from the inside out
            //meshRend.materials[i].SetOverrideTag("Queue", "Transparent+" + i);
        }
    }

    /*void setTextures(MeshRenderer rend) {
        int meshI = 0;
        // loop x, y, z
        for (int i = 0; i < 3; i++) {
            Vector3 planeDirection = new Vector3(i == 0 ? 1 : 0, i == 1 ? 1 : 0, i == 2 ? 1 : 0);
            for (int j = 0; j < cubeCounts[i]; j++) {
                rend.materials[meshI].mainTexture = textureForPlane(new Plane(planeDirection, (float)j / (cubeCounts[i] - 1)));
                meshI++;
            }
        }
    }*/

    void updateMaterials() {
        Vector3 cameraDirection = transform.InverseTransformDirection(Camera.main.transform.forward);

        int meshI = 0;
        // loop x, y, z
        float[] cameraDirXyz = new float[3] {cameraDirection.x, cameraDirection.y, cameraDirection.z };
        for (int i = 0; i < 3; i++) {
            Vector3 planeDirection = new Vector3(i == 0 ? 1 : 0, i == 1 ? 1 : 0, i == 2 ? 1 : 0);
            
            int count = cubeCounts[i];
            for (int j = 0; j < count; j++) {
                int drawIndex = (int)(2000 * cameraDirXyz[i] * j / count * (i == 1 || i == 2 ? -1 : 1)) + 2000;
                meshRend.materials[meshI].renderQueue = 3000 + drawIndex;
                meshRend.materials[meshI].mainTexture = textureForPlane(new Plane(planeDirection, (float)j / (count - 1)));
                meshI++;
            }
        }
    }

    // Use this for initialization
    void Start() {
        loadLayerFiles();

        MeshFilter mf = GetComponent<MeshFilter>();
        proceduralMesh = mf.mesh;
        scaleFactor = 2f / layerWidth;

        meshRend = GetComponent<MeshRenderer>();
        
        meshCollider = GetComponent<MeshCollider>();
        meshCollider.sharedMesh = proceduralMesh;

        Recreate();
	}

    float constrain(float val, float min, float max) {
        return Math.Min(max, Math.Max(min, val));
    }

    void Recreate() {
        cachedTextures.Clear();
        cachedTexturePlanes.Clear();

        makeMeshCubes(scaleFactor * layerWidth, scaleFactor * layerNumber, scaleFactor * layerHeight, proceduralMesh);
        setupTextures();
        updateMaterials();
    }
	
    void OnMouseDown() {
        isRotating = true;
        dragStartPosition = Input.mousePosition;
    }

    void OnMouseDrag() {
        if (isRotating) {
            Vector3 change = (Input.mousePosition - dragStartPosition) * rotationSensitivity;
            transform.Rotate(change.y, -change.x, 0, Space.World);
            updateMaterials();

            dragStartPosition = Input.mousePosition;
        }
    }

    void OnMouseUp() {
        isRotating = false;
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

	// Update is called once per frame
	void Update () {
        
        float scrollTicks = -Input.mouseScrollDelta.y;
        if (Math.Abs(scrollTicks) > 0) {
            Vector3 viewNormal = getViewNormal();

            if (floatEquals(viewNormal.x, -1)) {
                xLayersMinMax[1] = constrain(xLayersMinMax[1] + scrollTicks / (layerHeight - 1), 0, 1);
                xLayersMinMax[0] = Math.Min(xLayersMinMax[0], xLayersMinMax[1]); // Keep at least one layer visible
            } else if (floatEquals(viewNormal.x, 1)) {
                xLayersMinMax[0] = constrain(xLayersMinMax[0] - scrollTicks / (layerHeight - 1), 0, 1);
                xLayersMinMax[1] = Math.Max(xLayersMinMax[1], xLayersMinMax[0]);
            } else if (floatEquals(viewNormal.y, 1)) {
                yLayersMinMax[1] = constrain(yLayersMinMax[1] + scrollTicks / (layerNumber - 1), 0, 1);
                yLayersMinMax[0] = Math.Min(yLayersMinMax[0], yLayersMinMax[1]);
            } else if (floatEquals(viewNormal.y, -1)) {
                yLayersMinMax[0] = constrain(yLayersMinMax[0] - scrollTicks / (layerNumber - 1), 0, 1);
                yLayersMinMax[1] = Math.Max(yLayersMinMax[1], yLayersMinMax[0]);
            } else if (floatEquals(viewNormal.z, 1)) {
                zLayersMinMax[1] = constrain(zLayersMinMax[1] + scrollTicks / (layerWidth - 1), 0, 1);
                zLayersMinMax[0] = Math.Min(zLayersMinMax[0], zLayersMinMax[1]);
            } else if (floatEquals(viewNormal.z, -1)) {
                zLayersMinMax[0] = constrain(zLayersMinMax[0] - scrollTicks / (layerWidth - 1), 0, 1);
                zLayersMinMax[1] = Math.Max(zLayersMinMax[1], zLayersMinMax[0]);
            }

            Recreate();
        }
    }
}
