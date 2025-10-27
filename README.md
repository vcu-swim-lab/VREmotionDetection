
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

1. In Unity, select the object that you want to add the predictor model from the **Hierarchy** tab.
2. In the **Inspector** tab, click **Add Component**, then **Face Au Model**.
3. In the **Natural Model** field, pick `natural_trial_96.onnx` and for the `Acted Model` field pick `act_trial_92.onnx`.

## ▶ Usage

1. Create a new **MonoBehaviour** script and attach it to the same GameObject as the Emotion Predictor.  
2. Open the script in your code editor.  
3. Copy and paste the sample code from:  
   [`Samples~/Basic/MyScript.cs`](./Samples~/BasicUsage/BasicPredictor.cs).  

This script will give you direct access to the predictions from the emotion detection model.


## 📝 License

This project is licensed under the [MIT License](./LICENSE).
