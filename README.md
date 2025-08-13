
# VREmotionDetection

A library for detecting emotions in VR.

## Installation

1. Open your Unity game, go to `Window > Package Manager`.
2. Top-left corner, click on `+ > Install package from git URL..`, paste [https://github.com/vcu-swim-lab/VREmotionDetection.git]() and then click `Install`.

## Configuration

1. Go to `Assets`, then right click and `Create > Scriptable Objects > PredictorConfig`.
2. Click the created `PredictorConfig`, and then `Au Model`. From there pick `natural_trial_96.onnx`. Also enable `Poll By Default` if you want the predictor to start reading data starting from initialization, otherwise you have to manually enable polling later from your script, as shown in the example below.
3. Now go to Unity's `Hierarchy` tab and pick one game object that you want to use as a predictor. From there on `Inspector > Add Component > Emotion Predictor`. Then click on the `Config` field of `Emotion Predictor` and select the `PredictorConfig` asset you created earlier.

## Usage

Create a new `MonoBehavior` script to use the predictions from the model and attach it to the same game object. Open the script in your editor of choice and copy paste the [script](./Samples~/Basic/MyScript.cs) found in the samples folder. With this you can 
