using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class DatasetChooser : MonoBehaviour {

    public BuildMesh buildMesh;
    public GameObject chooserPanel;

    public Text BaseDirText;
    public Button btn0;
    public Button btn1;
    public Button btn2;
    public Button btn3;

    public Button prevBtn;
    public Button nextBtn;

    public bool ShowChooser = true;
    public string BaseDatasetDirectory = "C:/VR_Datasets";

    // Increases when the next button is pressed, etc...
    int datasetIndex = 0;
    string[] datasetDirs = new string[0];

    float refreshDatasetsInterval = 3;
    float timeSinceLastRefresh = 4;

	// Use this for initialization
	void Start() {
        BaseDirText.text = "from " + BaseDatasetDirectory;
	}
	
    public void nextClick() {
        if (datasetDirs.Length > datasetIndex + 4) {
            datasetIndex += 4;
        }
    }

    public void prevClick() {
        if (datasetIndex >= 4) {
            datasetIndex -= 4;
        }
    }

    public void datasetClick(int index) {
        string dataset = datasetDirs[index + datasetIndex];
        buildMesh.LoadDataset(dataset);
    }

    // Update is called once per frame
    void Update() {
        chooserPanel.SetActive(ShowChooser);
        if (!ShowChooser) {
            return;
        }

        timeSinceLastRefresh += Time.deltaTime;
        if (timeSinceLastRefresh > refreshDatasetsInterval) {
            timeSinceLastRefresh = 0;

            if (Directory.Exists(BaseDatasetDirectory)) {
                datasetDirs = Directory.GetDirectories(BaseDatasetDirectory);
            } else {
                datasetDirs = new string[0];
            }
        }

        datasetIndex = Math.Min(datasetIndex, ((datasetDirs.Length - 1) / 4) * 4);

        Button[] buttons = new Button[] { btn0, btn1, btn2, btn3 };
        for (int i = 0; i < 4; i++) {
            Text btnText = buttons[i].GetComponentInChildren<Text>();
            if (i + datasetIndex < datasetDirs.Length) {
                buttons[i].interactable = true;
                string datasetName = datasetDirs[i + datasetIndex].Split('/', '\\').Last();
                btnText.text = datasetName;
            } else {
                buttons[i].interactable = false;
                btnText.text = "---";
            }
        }

        prevBtn.interactable = datasetIndex > 0;
        nextBtn.interactable = datasetDirs.Length > datasetIndex + 4;
    }
}
