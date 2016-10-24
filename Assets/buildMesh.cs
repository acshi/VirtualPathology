using UnityEngine;
using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;

public class buildMesh : MonoBehaviour {
    public string ImageLayerDirectory = "human_kidney_png";

    public float yAspectRatio = 2.8f;
    public int layerSeparation = 3; // basically the pixels between layers
    const float rotationSensitivity = 0.3f;

    Mesh proceduralMesh;
    MeshRenderer meshRend;
    MeshCollider meshCollider;
    Texture2D[] layers;
    //Texture2D[] planeTextures;
    float scaleFactor;
    int layerWidth;
    int layerHeight;
    int layerNumber;

    List<Texture2D> cachedTextures = new List<Texture2D>();
    List<Plane> cachedTexturePlanes = new List<Plane>();
    
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
    void makeMeshLayers(float xl, float yl, float zl, float[] xMinMax, float[] yMinMax, float[] zMinMax, Mesh mesh) {
        mesh.Clear();

        Vector3 viewNormal = getViewNormal();
        int layerCount = 
        mesh.subMeshCount = 6 * layerCount;

        Vector3[] vertices = new Vector3[24 * layerCount]; // three of each vertex for different textures
        Vector2[] uvs = new Vector2[24 * layerCount];

        for (int cubeI = 0; cubeI < layerCount; cubeI++) {
            float layerOffset = cubeI * layerSeparation;
            float[] cubeXMinMax = { 1 - xMinMax[0] - layerOffset / (layerHeight - 1), 1 - xMinMax[1] + layerOffset / (layerHeight - 1) };
            float[] cubeYMinMax = { yMinMax[0] + layerOffset / (layerNumber - 1), yMinMax[1] - layerOffset / (layerNumber - 1) };
            float[] cubeZMinMax = { zMinMax[0] + layerOffset / (layerWidth - 1), zMinMax[1] - layerOffset / (layerWidth - 1) };

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
        mesh.vertices = vertices;
        mesh.uv = uvs;

        int meshI = 0;
        for (int cubeI = 0; cubeI < layerCount; cubeI++) {
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
        meshRend.materials = new Material[6 * layerCount];
        for (int i = 0; i < 6 * layerCount; i++) {
            meshRend.materials[i] = new Material(baseMaterial);
            meshRend.materials[i].shader = baseMaterial.shader;
            //meshRend.materials[i].mainTexture = planeTextures[i];
            // Have the cubes render from the inside out
            meshRend.materials[i].SetOverrideTag("Queue", "Transparent+" + i);
        }
    }

    void setTextures(MeshRenderer rend) {
        for (int cubeI = 0; cubeI < layerCount; cubeI++) {
            float layerOffset = cubeI * layerSeparation;
            float[] cubeXMinMax = { xLayersMinMax[0] + layerOffset / (layerHeight - 1), xLayersMinMax[1] - layerOffset / (layerHeight - 1) };
            float[] cubeYMinMax = { yLayersMinMax[0] + layerOffset / (layerNumber - 1), yLayersMinMax[1] - layerOffset / (layerNumber - 1) };
            float[] cubeZMinMax = { zLayersMinMax[0] + layerOffset / (layerWidth - 1), zLayersMinMax[1] - layerOffset / (layerWidth - 1) };
            // The planes are: -x, +x, -y, +y, -z, +z
            if (xLayersMinMax[0] != xBuiltMinMax[0]) {
                rend.materials[6 * cubeI + 0].mainTexture = textureForPlane(new Plane(new Vector3(1, 0, 0), cubeXMinMax[0]));
            }
            if (xLayersMinMax[1] != xBuiltMinMax[1]) {
                rend.materials[6 * cubeI + 1].mainTexture = textureForPlane(new Plane(new Vector3(1, 0, 0), cubeXMinMax[1]));
            }
            if (yLayersMinMax[0] != yBuiltMinMax[0]) {
                rend.materials[6 * cubeI + 2].mainTexture = textureForPlane(new Plane(new Vector3(0, 1, 0), cubeYMinMax[0])); // bottom
            }
            if (yLayersMinMax[1] != yBuiltMinMax[1]) {
                rend.materials[6 * cubeI + 3].mainTexture = textureForPlane(new Plane(new Vector3(0, 1, 0), cubeYMinMax[1])); // top
            }
            if (zLayersMinMax[0] != zBuiltMinMax[0]) {
                rend.materials[6 * cubeI + 4].mainTexture = textureForPlane(new Plane(new Vector3(0, 0, 1), cubeZMinMax[0]));
            }
            if (zLayersMinMax[1] != zBuiltMinMax[1]) {
                rend.materials[6 * cubeI + 5].mainTexture = textureForPlane(new Plane(new Vector3(0, 0, 1), cubeZMinMax[1]));
            }
        }
        for (int cubeI = 0; cubeI < layerCount; cubeI++) {
            for (int i = 0; i < 6; i++) {
                //rend.materials[i].SetOverrideTag("Queue", "Transparent+" + (cubeCount - cubeI));
                rend.materials[cubeI * 6 + i].renderQueue = 3000 + (layerCount - cubeI);
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

    bool floatEquals(float a, float b) {
        float epsilon = 1e-5f;
        return Math.Abs(a - b) < epsilon;
    }

    Vector3 getViewNormal() {
        // Detect which face of the object is in front of the camera, return that normal
        RaycastHit rayHit;
        Physics.Raycast(Camera.main.transform.position, Camera.main.transform.forward, out rayHit);
        Vector3 hitSurfaceNormal = rayHit.transform.InverseTransformDirection(rayHit.normal);
        return hitSurfaceNormal;
    }

    enum MeshView: int {
        X = 0,
        Y,
        Z,
        XZ,
        XNZ, // N means X and Z have different signs
        XY,
        XNY,
        YZ,
        YNZ
    }

    // Which mesh should we be using right now
    MeshView getCurrentMeshView() {
        // Detect which point of the object is in front of the camera
        RaycastHit rayHit;
        Physics.Raycast(Camera.main.transform.position, Camera.main.transform.forward, out rayHit);
        Vector3 p = rayHit.transform.InverseTransformDirection(rayHit.point);

        float absX = Math.Abs(p.x);
        float absY = Math.Abs(p.y);
        float absZ = Math.Abs(p.z);

        if (absX >= absY && absX >= absZ) {
            if (absY >= absZ && absY >= 0.5f) {
                return (p.x * p.y > 0) ? MeshView.XY : MeshView.XNY;
            } else if (absZ >= 0.5f) {
                return (p.x * p.z > 0) ? MeshView.XZ : MeshView.XNZ;
            } else {
                return MeshView.X;
            }
        } else if (Math.Abs(p.y) >= Math.Abs(p.z)) {
            if (absX >= absZ && absX >= 0.5f) {
                return (p.y * p.x > 0) ? MeshView.XY : MeshView.XNY;
            } else if (absZ >= 0.5f) {
                return (p.y * p.z > 0) ? MeshView.YZ : MeshView.YNZ;
            } else {
                return MeshView.Y;
            }
        } else {
            if (absX >= absY && absX >= 0.5f) {
                return (p.z * p.x > 0) ? MeshView.XZ : MeshView.XNZ;
            } else if (absY >= 0.5f) {
                return (p.z * p.y > 0) ? MeshView.YZ : MeshView.YNZ;
            } else {
                return MeshView.Z;
            }
        }
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
