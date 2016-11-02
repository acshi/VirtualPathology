using UnityEngine;
using System.IO;
using System;

public class buildMesh : MonoBehaviour {
    public string ImageLayerDirectory = "mouse_kidney_png";

    public int cubeCount = 4; // number of cube layers. 1 would be to have just the outermost layer
    public int cubeSeparation = 3; // number of layers between cube layers
    const float rotationSensitivity = 0.3f;

    Mesh proceduralMesh;
    MeshRenderer meshRend;
    MeshCollider meshCollider;
    Texture2D[] layers;
    Texture2D[] planeTextures;
    float scaleFactor;
    int layerWidth;
    int layerHeight;
    int layerNumber;
    
    bool isRotating = false;
    Vector3 dragStartPosition;

    float[] xLayersMinMax = new float[] { 0, 1 };
    float[] yLayersMinMax = new float[] { 0, 1 };
    float[] zLayersMinMax = new float[] { 0, 1 };
    float[] xBuiltMinMax = new float[2] { -1, -1 };
    float[] yBuiltMinMax = new float[2] { -1, -1 };
    float[] zBuiltMinMax = new float[2] { -1, -1 };
    //Vector3 positiveAxisZoom; // increase to view inner layers on the + side
    //Vector3 negativeAxisZoom; // for - side.

    // Total uncompressed dimensions xl*yl*zl, but x/y/zMinMax (between 0 and 1) specify the sub-part of that to use.
    void makeCubes(float xl, float yl, float zl, float[] xMinMax, float[] yMinMax, float[] zMinMax, Mesh mesh) {
        mesh.Clear();
        mesh.subMeshCount = 6 * cubeCount;

        Vector3[] vertices = new Vector3[24 * cubeCount]; // three of each vertex for different textures
        Vector2[] uvs = new Vector2[24 * cubeCount];
        
        for (int cubeI = 0; cubeI < cubeCount; cubeI++) {
            float layerOffset = cubeI * cubeSeparation;
            float[] cubeXMinMax = { 1 - xMinMax[0] - layerOffset / layerHeight, 1 - xMinMax[1] + layerOffset / layerHeight };
            float[] cubeYMinMax = { yMinMax[0] + layerOffset / layerNumber, yMinMax[1] - layerOffset / layerNumber };
            float[] cubeZMinMax = { zMinMax[0] + layerOffset / layerWidth, zMinMax[1] - layerOffset / layerWidth };

            int vertI = 24 * cubeI;
            // three sets of identical vertices, starting at 0 (x planes), 8 (y planes), 16 (z planes).
            // i&4 == 0 are -z, == 4 are +z
            // i&2 == 0 are -y, == 2 are +y
            // i&1 == 0 are -x, == 1 are +x
            for (int z = 0; z <= 1; z++) {
                for (int y = 0; y <= 1; y++) {
                    for (int x = 0; x <= 1; x++) {
                        vertices[vertI] = new Vector3(xl * (cubeXMinMax[x] - 0.5f), yl * (cubeYMinMax[y] - 0.5f), zl * (cubeZMinMax[z] - 0.5f));
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
        mesh.vertices = vertices;
        mesh.uv = uvs;

        int meshI = 0;
        for (int cubeI = 0; cubeI < cubeCount; cubeI++) {
            // loop x, y, z
            for (int i = 0; i < 3; i++) {
                int heldDimI = 1 << i;
                int dim1I = 1 << ((i + 1) % 3);
                int dim2I = 1 << ((i + 2) % 3);

                // - and + sides
                for (int j = 0; j < 2; j++) {
                    int[] triangles = new int[6];

                    int baseI = 24 * cubeI + i * 8 + heldDimI * j;
                    int vert0 = baseI;
                    int vert1 = baseI + dim1I;
                    int vert2 = baseI + dim2I;
                    int vert3 = baseI + dim1I + dim2I;

                    // (-, -), (+, -), (+, +)
                    triangles[0] = j == 0 ? vert0 : vert3;
                    triangles[1] = vert1;
                    triangles[2] = j == 0 ? vert3 : vert0;

                    // (+, +), (-, +), (-, -)
                    triangles[3] = j == 0 ? vert3 : vert0;
                    triangles[4] = vert2;
                    triangles[5] = j == 0 ? vert0 : vert3;

                    mesh.SetTriangles(triangles, meshI);
                    meshI++;
                }
            }
        }
    }

    void loadLayerFiles() {
        string path = Path.Combine(Application.streamingAssetsPath, ImageLayerDirectory);
        string[] files = Directory.GetFiles(path, "*.png");
        layers = new Texture2D[files.Length];

        //int transparency = (int)(transparencyCoef * 255);

        for (int i = 0; i < files.Length; i++) {
            Texture2D tex = new Texture2D(1, 1);
            tex.LoadImage(File.ReadAllBytes(files[i]));
            /*Color32[] pixels = tex.GetPixels32();
            // Add alpha to pixels
            for (int j = 0; j < pixels.Length; j++) {
                pixels[j].a = (byte)(Math.Min(255, (255 - transparency) + transparency * (pixels[j].r * 299 + pixels[j].g * 587 + pixels[j].b * 114) / 1000 / 255));
            }
            tex.SetPixels32(pixels);*/
            layers[i] = tex;
        }

        layerWidth = layers[0].width;
        layerHeight = layers[0].height;
        layerNumber = layers.Length;
    }

    // Distances should be from 0 to 1, from (-x/y/z to +x/y/z).
    void setPlaneTexture(Texture2D tex, Plane plane) {
        if (plane.normal.y == 1f) {
            // Special case for vertical slices
            Texture2D copyTexture = layers[(int)((1 - plane.distance) * (layerNumber - 1) + 0.5f)];
            tex.SetPixels32(copyTexture.GetPixels32());
        } else if (plane.normal.x == 1f) {
            int xLevel = (int)((1 - plane.distance) * (layerHeight - 1) + 0.5f);
            for (int i = 0; i < layerNumber; i++) {
                Color[] line = layers[i].GetPixels(0, xLevel, layerWidth, 1);
                tex.SetPixels(0, layerNumber - 1 - i, layerWidth, 1, line);
            }
        } else if (plane.normal.z == 1f) {
            int zLevel = (int)(plane.distance * (layerWidth - 1) + 0.5f);
            for (int i = 0; i < layerNumber; i++) {
                Color[] line = layers[i].GetPixels(zLevel, 0, 1, layerHeight);
                //Array.Reverse(line);
                tex.SetPixels(0, layerNumber - 1 - i, layerHeight, 1, line);
            }
        }
        tex.Apply();
    }

    void setupTextures() {
        Material baseMaterial = meshRend.material;
        meshRend.materials = new Material[6 * cubeCount];
        planeTextures = new Texture2D[6 * cubeCount];
        for (int cubeI = 0; cubeI < cubeCount; cubeI++) {
            planeTextures[6 * cubeI + 0] = new Texture2D(layerWidth, layerNumber, TextureFormat.RGBA32, false);
            planeTextures[6 * cubeI + 1] = new Texture2D(layerWidth, layerNumber, TextureFormat.RGBA32, false);
            planeTextures[6 * cubeI + 2] = new Texture2D(layerWidth, layerHeight, TextureFormat.RGBA32, false);
            planeTextures[6 * cubeI + 3] = new Texture2D(layerWidth, layerHeight, TextureFormat.RGBA32, false);
            planeTextures[6 * cubeI + 4] = new Texture2D(layerHeight, layerNumber, TextureFormat.RGBA32, false);
            planeTextures[6 * cubeI + 5] = new Texture2D(layerHeight, layerNumber, TextureFormat.RGBA32, false);
        }
        for (int i = 0; i < 6 * cubeCount; i++) {
            meshRend.materials[i] = new Material(baseMaterial);
            meshRend.materials[i].shader = baseMaterial.shader;
            meshRend.materials[i].mainTexture = planeTextures[i];
            meshRend.materials[i].SetOverrideTag("Queue", "Transparent+" + i);
        }
    }

    void setTextures(MeshRenderer rend) {
        for (int cubeI = 0; cubeI < cubeCount; cubeI++) {
            float layerOffset = cubeI * cubeSeparation;
            float[] cubeXMinMax = { xLayersMinMax[0] + layerOffset / layerHeight, xLayersMinMax[1] - layerOffset / layerHeight };
            float[] cubeYMinMax = { yLayersMinMax[0] + layerOffset / layerNumber, yLayersMinMax[1] - layerOffset / layerNumber };
            float[] cubeZMinMax = { zLayersMinMax[0] + layerOffset / layerWidth, zLayersMinMax[1] - layerOffset / layerWidth };
            // The planes are: -x, +x, -y, +y, -z, +z
            if (xLayersMinMax[0] != xBuiltMinMax[0]) {
                setPlaneTexture(planeTextures[6 * cubeI + 0], new Plane(new Vector3(1, 0, 0), cubeXMinMax[0]));
            }
            if (xLayersMinMax[1] != xBuiltMinMax[1]) {
                setPlaneTexture(planeTextures[6 * cubeI + 1], new Plane(new Vector3(1, 0, 0), cubeXMinMax[1]));
            }
            if (yLayersMinMax[0] != yBuiltMinMax[0]) {
                setPlaneTexture(planeTextures[6 * cubeI + 2], new Plane(new Vector3(0, 1, 0), cubeYMinMax[0])); // bottom
            }
            if (yLayersMinMax[1] != yBuiltMinMax[1]) {
                setPlaneTexture(planeTextures[6 * cubeI + 3], new Plane(new Vector3(0, 1, 0), cubeYMinMax[1])); // top
            }
            if (zLayersMinMax[0] != zBuiltMinMax[0]) {
                setPlaneTexture(planeTextures[6 * cubeI + 4], new Plane(new Vector3(0, 0, 1), cubeZMinMax[0]));
            }
            if (zLayersMinMax[1] != zBuiltMinMax[1]) {
                setPlaneTexture(planeTextures[6 * cubeI + 5], new Plane(new Vector3(0, 0, 1), cubeZMinMax[1]));
            }
        }
        for (int cubeI = 0; cubeI < cubeCount; cubeI++) {
            for (int i = 0; i < 6; i++) {
                //rend.materials[i].SetOverrideTag("Queue", "Transparent+" + (cubeCount - cubeI));
                rend.materials[cubeI * 6 + i].renderQueue = 3000 + (cubeCount - cubeI);
                //rend.materials[i].renderQueue = -;
            }
        }
        xBuiltMinMax[0] = xLayersMinMax[0];
        xBuiltMinMax[1] = xLayersMinMax[1];
        yBuiltMinMax[0] = yLayersMinMax[0];
        yBuiltMinMax[1] = yLayersMinMax[1];
        zBuiltMinMax[0] = zLayersMinMax[0];
        zBuiltMinMax[1] = zLayersMinMax[1];
    }

    // Use this for initialization
    void Start() {
        loadLayerFiles();

        MeshFilter mf = GetComponent<MeshFilter>();
        proceduralMesh = mf.mesh;
        scaleFactor = 2f / layerWidth;

        meshRend = GetComponent<MeshRenderer>();
        setupTextures();

        meshCollider = GetComponent<MeshCollider>();
        meshCollider.sharedMesh = proceduralMesh;

        Recreate();
	}

    float constrain(float val, float min, float max) {
        return Math.Min(max, Math.Max(min, val));
    }

    void Recreate() {
        //float[] xMinMax = new float[] { scaleFactor * layerWidth * (-1 + negativeAxisZoom.x) * 0.5f, scaleFactor * layerWidth * (1 - positiveAxisZoom.x) * 0.5f };
        //float[] yMinMax = new float[] { scaleFactor * layerNumber * (-1 + negativeAxisZoom.y) * 0.5f, scaleFactor * layerNumber * (1 - positiveAxisZoom.y) * 0.5f };
        //float[] zMinMax = new float[] { scaleFactor * layerHeight * (-1 + negativeAxisZoom.z) * 0.5f, scaleFactor * layerHeight * (1 - positiveAxisZoom.z) * 0.5f };

        makeCubes(scaleFactor * layerWidth, scaleFactor * layerNumber, scaleFactor * layerHeight, xLayersMinMax, yLayersMinMax, zLayersMinMax, proceduralMesh);
        setTextures(meshRend);
    }
	
    void OnMouseDown() {
        isRotating = true;
        dragStartPosition = Input.mousePosition;
    }

    void OnMouseDrag() {
        if (isRotating) {
            Vector3 change = (Input.mousePosition - dragStartPosition) * rotationSensitivity;
            transform.Rotate(change.y, -change.x, 0, Space.World);

            dragStartPosition = Input.mousePosition;
        }
    }

    void OnMouseUp() {
        isRotating = false;
    }

	// Update is called once per frame
	void Update () {
        Vector3 cameraDir = Camera.main.transform.forward;
        Vector3 relativeCameraDir = transform.rotation * cameraDir;

        float scrollTicks = -Input.mouseScrollDelta.y;
        if (Math.Abs(scrollTicks) > 0) {
            float absCamX = Math.Abs(relativeCameraDir.x);
            float absCamY = Math.Abs(relativeCameraDir.y);
            float absCamZ = Math.Abs(relativeCameraDir.z);
            if (absCamX > absCamY && absCamX > absCamZ) {
                if (relativeCameraDir.x < 0) {
                    xLayersMinMax[1] = constrain(xLayersMinMax[1] + scrollTicks / (layerHeight - 1), 0, 1);
                    xLayersMinMax[0] = Math.Min(xLayersMinMax[0], xLayersMinMax[1]); // Keep at least one layer visible
                } else {
                    xLayersMinMax[0] = constrain(xLayersMinMax[0] - scrollTicks / (layerHeight - 1), 0, 1);
                    xLayersMinMax[1] = Math.Max(xLayersMinMax[1], xLayersMinMax[0]);
                }
            } else if (absCamY > absCamX && absCamY > absCamZ) {
                if (relativeCameraDir.y > 0) {
                    yLayersMinMax[1] = constrain(yLayersMinMax[1] + scrollTicks / (layerNumber - 1), 0, 1);
                    yLayersMinMax[0] = Math.Min(yLayersMinMax[0], yLayersMinMax[1]);
                } else {
                    yLayersMinMax[0] = constrain(yLayersMinMax[0] - scrollTicks / (layerNumber - 1), 0, 1);
                    yLayersMinMax[1] = Math.Max(yLayersMinMax[1], yLayersMinMax[0]);
                }
            } else {
                if (relativeCameraDir.z < 0) {
                    zLayersMinMax[1] = constrain(zLayersMinMax[1] + scrollTicks / (layerWidth - 1), 0, 1);
                    zLayersMinMax[0] = Math.Min(zLayersMinMax[0], zLayersMinMax[1]);
                } else {
                    zLayersMinMax[0] = constrain(zLayersMinMax[0] - scrollTicks / (layerWidth - 1), 0, 1);
                    zLayersMinMax[1] = Math.Max(zLayersMinMax[1], zLayersMinMax[0]);
                }
            }

            Recreate();
        }
    }
}
