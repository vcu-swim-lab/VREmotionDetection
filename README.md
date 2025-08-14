
# VREmotionDetection

**VREmotionDetection** is a Unity library for detecting emotions in VR using machine learning models.  

## 📋 Prerequisites

- Unity 6 or newer.
- Meta Quest Pro if you want to use use action units for prediction.


## 📦 Installation

1. In your Unity project, open **`Window > Package Manager`**.  
2. In the top-left corner, click **`+` > `Install package from git URL...`**  
3. Paste the following URL:  

   ```
   https://github.com/vcu-swim-lab/VREmotionDetection.git
   ```

4. Click **Install**.


## ⚙️ Configuration

### 1. Create a Predictor Config
1. In Unity, go to **`Assets`**.  
2. Right-click and choose **`Create > Scriptable Objects > PredictorConfig`**.

### 2. Set Up the Model
1. Select the newly created **PredictorConfig** asset.  
2. In the **`Au Model`** field, choose:  
   ```
   natural_trial_96.onnx
   ```
3. (Optional) Enable **`Poll By Default`** if you want the predictor to start reading data automatically on initialization.  
   - If disabled, you must manually start polling from your script (see [Usage](#-usage)).

### 3. Attach the Emotion Predictor
1. In the **Hierarchy**, select the GameObject you want to use for prediction.  
2. In the **Inspector**, click **`Add Component`** and choose **`Emotion Predictor`**.  
3. In the `Config` field of **Emotion Predictor**, select the **PredictorConfig** asset you created.


## ▶ Usage

1. Create a new **MonoBehaviour** script and attach it to the same GameObject as the Emotion Predictor.  
2. Open the script in your code editor.  
3. Copy and paste the sample code from:  
   [`Samples~/Basic/MyScript.cs`](./Samples~/BasicUsage/BasicPredictor.cs).  

This script will give you direct access to the predictions from the emotion detection model.


## 📝 License

This project is licensed under the [MIT License](./LICENSE).
