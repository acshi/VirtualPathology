using UnityEngine;
using System.IO;
using System;

public class buildMesh : MonoBehaviour {
    public string ImageLayerDirectory = "human_kidney_png";

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
    //Vector3 positiveAxisZoom; // increase to view inner layers on the + side
    //Vector3 negativeAxisZoom; // for - side.

    // Total uncompressed dimensions xl*yl*zl, but x/y/zMinMax (between 0 and 1) specify the sub-part of that to use.
    void makeCube(float xl, float yl, float zl, float[] xMinMax, float[] yMinMax, float[] zMinMax, Mesh mesh) {
        mesh.Clear();
        mesh.subMeshCount = 6;

        Vector3[] vertices = new Vector3[24]; // three of each vertex for different textures
        Vector2[] uvs = new Vector2[24];

        int vertI = 0;
        // three sets of identical vertices, starting at 0 (x planes), 8 (y planes), 16 (z planes).
        // i&4 == 0 are -z, == 4 are +z
        // i&2 == 0 are -y, == 2 are +y
        // i&1 == 0 are -x, == 1 are +x
        for (int z = 0; z <= 1; z++) {
            for (int y = 0; y <= 1; y++) {
                for (int x = 0; x <= 1; x++) {
                    vertices[vertI] = new Vector3(xl * (xMinMax[x] - 0.5f), yl * (yMinMax[y] - 0.5f), zl * (zMinMax[z] - 0.5f));
                    vertices[vertI + 8] = vertices[vertI];
                    vertices[vertI + 16] = vertices[vertI];

                    uvs[vertI] = new Vector2(zMinMax[z], yMinMax[y]);
                    uvs[vertI + 8] = new Vector2(zMinMax[z], xMinMax[x]);
                    uvs[vertI + 16] = new Vector2(xMinMax[x], yMinMax[y]);
                    vertI++;
                }
            }
        }
        mesh.vertices = vertices;
        
        // loop x, y, z
        int meshI = 0;
        for (int i = 0; i < 3; i++) {
            int heldDimI = 1 << i;
            int dim1I = 1 << ((i + 1) % 3);
            int dim2I = 1 << ((i + 2) % 3);
            
            // - and + sides
            for (int j = 0; j < 2; j++) {
                int[] triangles = new int[6];

                int baseI = i * 8 + heldDimI * j;
                int vert0 = baseI;
                int vert1 = baseI + dim1I;
                int vert2 = baseI + dim2I;
                int vert3 = baseI + dim1I + dim2I;

                
                /*switch(i) {
                    case 0: // x
                        uvs[vert0] = new Vector2(zMinMax[0], yMinMax[0]);
                        uvs[vert1] = new Vector2(zMinMax[0], yMinMax[1]);
                        uvs[vert2] = new Vector2(zMinMax[1], yMinMax[0]);
                        uvs[vert3] = new Vector2(zMinMax[1], yMinMax[1]);
                        break;
                    case 1: // y
                        uvs[vert0] = new Vector2(zMinMax[0], xMinMax[1]);
                        uvs[vert1] = new Vector2(zMinMax[1], xMinMax[1]);
                        uvs[vert2] = new Vector2(zMinMax[0], xMinMax[0]);
                        uvs[vert3] = new Vector2(zMinMax[1], xMinMax[0]);
                        break;
                    case 2: // z
                        uvs[vert0] = new Vector2(xMinMax[1], yMinMax[0]);
                        uvs[vert1] = new Vector2(xMinMax[0], yMinMax[0]);
                        uvs[vert2] = new Vector2(xMinMax[1], yMinMax[1]);
                        uvs[vert3] = new Vector2(xMinMax[0], yMinMax[1]);
                        break;
                }*/

                // (-, -), (+, -), (+, +)
                triangles[0] = j == 0 ? vert3 : vert0;
                triangles[1] = vert1;
                triangles[2] = j == 0 ? vert0 : vert3;

                // (+, +), (-, +), (-, -)
                triangles[3] = j == 0 ? vert0 : vert3;
                triangles[4] = vert2;
                triangles[5] = j == 0 ? vert3 : vert0;
                
                mesh.SetTriangles(triangles, meshI);
                meshI++;
            }
        }
        mesh.uv = uvs;
    }

    void loadLayerFiles() {
        string path = Path.Combine(Application.streamingAssetsPath, ImageLayerDirectory);
        string[] files = Directory.GetFiles(path, "*.png");
        layers = new Texture2D[files.Length];

        for (int i = 0; i < files.Length; i++) {
            Texture2D tex = new Texture2D(1, 1);
            tex.LoadImage(File.ReadAllBytes(files[i]));
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
        planeTextures = new Texture2D[6];
        planeTextures[0] = new Texture2D(layerWidth, layerNumber, TextureFormat.RGBA32, false);
        planeTextures[1] = new Texture2D(layerWidth, layerNumber, TextureFormat.RGBA32, false);
        planeTextures[2] = new Texture2D(layerWidth, layerHeight, TextureFormat.RGBA32, false);
        planeTextures[3] = new Texture2D(layerWidth, layerHeight, TextureFormat.RGBA32, false);
        planeTextures[4] = new Texture2D(layerHeight, layerNumber, TextureFormat.RGBA32, false);
        planeTextures[5] = new Texture2D(layerHeight, layerNumber, TextureFormat.RGBA32, false);
        meshRend.materials[0].mainTexture = planeTextures[0];
        meshRend.materials[1].mainTexture = planeTextures[1];
        meshRend.materials[2].mainTexture = planeTextures[2];
        meshRend.materials[3].mainTexture = planeTextures[3];
        meshRend.materials[4].mainTexture = planeTextures[4];
        meshRend.materials[5].mainTexture = planeTextures[5];
    }

    void setTextures(MeshRenderer rend) {
        // The planes are: -x, +x, -y, +y, -z, +z
        // Not sure why we need to do this inversion for x... but it comes out backward if we don't
        setPlaneTexture(planeTextures[0], new Plane(new Vector3(1, 0, 0), 1 - xLayersMinMax[0]));
        setPlaneTexture(planeTextures[1], new Plane(new Vector3(1, 0, 0), 1 - xLayersMinMax[1]));
        setPlaneTexture(planeTextures[2], new Plane(new Vector3(0, 1, 0), yLayersMinMax[0])); // bottom
        setPlaneTexture(planeTextures[3], new Plane(new Vector3(0, 1, 0), yLayersMinMax[1])); // top
        setPlaneTexture(planeTextures[4], new Plane(new Vector3(0, 0, 1), zLayersMinMax[0]));
        setPlaneTexture(planeTextures[5], new Plane(new Vector3(0, 0, 1), zLayersMinMax[1]));
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

        makeCube(scaleFactor * layerWidth, scaleFactor * layerNumber, scaleFactor * layerHeight, xLayersMinMax, yLayersMinMax, zLayersMinMax, proceduralMesh);
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

        float scrollTicks = Input.mouseScrollDelta.y;
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
                if (relativeCameraDir.y < 0) {
                    yLayersMinMax[1] = constrain(yLayersMinMax[1] + scrollTicks / (layerHeight - 1), 0, 1);
                    yLayersMinMax[0] = Math.Min(yLayersMinMax[0], yLayersMinMax[1]);
                } else {
                    yLayersMinMax[0] = constrain(yLayersMinMax[0] - scrollTicks / (layerHeight - 1), 0, 1);
                    yLayersMinMax[1] = Math.Max(yLayersMinMax[1], yLayersMinMax[0]);
                }
            } else {
                if (relativeCameraDir.z < 0) {
                    zLayersMinMax[1] = constrain(zLayersMinMax[1] + scrollTicks / (layerHeight - 1), 0, 1);
                    zLayersMinMax[0] = Math.Min(zLayersMinMax[0], zLayersMinMax[1]);
                } else {
                    zLayersMinMax[0] = constrain(zLayersMinMax[0] - scrollTicks / (layerHeight - 1), 0, 1);
                    zLayersMinMax[1] = Math.Max(zLayersMinMax[1], zLayersMinMax[0]);
                }
            }

            Recreate();
        }
    }
}
