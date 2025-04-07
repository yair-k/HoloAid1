# Project HoloAid: An Integrated Augmented Reality System for Dual Sensory Impairments

## Introduction
In today's rapidly evolving digital landscape, accommodating individuals with sensory impairments remains an ongoing challenge, particularly when both vision and hearing impairments coexist. Project HoloAid presents an advanced yet practical Augmented Reality (AR) system using the Microsoft HoloLens platform to simplify navigation, object identification, and communication for users with dual sensory impairments.

Our solution combines:
- **Real-Time Spatial Mapping** for obstacle detection
- **Audio Sonification** to encode physical surroundings into auditory cues
- **Text Recognition and Text-to-Speech (TTS)** to assist with printed text and labels
- **Adaptive Contrast Enhancement (ACE)** to improve visual clarity of objects

By consolidating these features into a single wearable device, we aim to reduce complexity and improve quality of life for users with dual sensory impairments.

## System Overview and Client Requirements

### Client-Centric Requirements
Based on user research and accessibility best practices, we identified these core requirements:
- **Dual Sensory Support:** Provide assistance for both vision and hearing impairments in one solution
- **High Contrast Visualization:** Offer neon or black-and-white modes and magnification options 
- **Hands-Free Navigation:** Leverage voice commands and wearable technology
- **Enhanced Object Recognition:** Identify everyday objects like stovetops, utensils, and groceries
- **Efficient Audio Feedback:** Generate intelligible verbal cues over simple beeping sounds
- **Ease of Use:** Integrate with existing accessibility features and standard technologies

### Hardware and Software Architecture
The AR system is built around the Microsoft HoloLens, chosen for its:
1. **Depth-Sensing:** IR sensor array for accurate spatial mapping
2. **High-Resolution Optics:** Digital visual overlays on real-world scenes
3. **Integrated Microphone Array:** Voice command input and audio output options

Our software stack includes:
- **Unity and MRTK (Mixed Reality Toolkit)** for AR functionality
- **OpenCV** for image processing and contrast enhancement
- **TensorFlow** for object detection and classification
- **Tesseract OCR** for real-time text recognition
- **Google Text-to-Speech (TTS)** for audio feedback

## Technical Features

### Spatial Mapping and Obstacle Detection
The HoloLens constantly scans the environment to create a 3D mesh model representing surfaces and objects. We employ:
- Point cloud acquisition and Gaussian smoothing
- RANSAC algorithm for plane fitting
- Hough Transform for planar segmentation

### Audio Sonification for Enhanced Navigation
For users with hearing impairments, we transform spatial information into clear, intelligible cues:
- Audio intensity varies exponentially with obstacle distance
- Logarithmic scaling mimics natural human loudness perception
- Stereo positioning helps with directional awareness

### Text Recognition and Audio Feedback
Our text recognition module allows users to "hear" printed material:
1. Image capture from HoloLens camera
2. Preprocessing with OpenCV filters
3. Text extraction with Tesseract OCR
4. Voice output through Google TTS

### Adaptive Contrast Enhancement (ACE)
For users with partial blindness, our system actively adjusts the visual display:
- Global contrast adjustment to enhance overall scene visibility
- Local object highlighting using TensorFlow-based detection
- Neon edge overlays to make important objects visually stand out

## Software Integration
The system is implemented in Unity with modular components:
- **MixedRealityCameraParent:** Interfaces with HoloLens sensors
- **Script Manager:** Central controller for system tasks
- **OpenCV and TensorFlow Plugins:** Handle real-time image manipulation
- **Event-Driven Architecture:** Voice commands and gestures trigger specific functions

## Project Structure
```
HoloAid1/Assets/
├── Audio Samples
├── Prefabs
├── Resources
│   ├── HoloToolkit
│   │   ├── Boundary
│   │   ├── Input
│   │   ├── SpatialMapping
│   │   ├── SpatialSound
│   │   └── ...
│   ├── Icons
│   ├── MixedRealityToolkit
│   └── SimpleJSON-master
├── Scenes
│   └── Control Test.unity
└── Scripts
    ├── Audio
    │   ├── ObstacleAudio.cs
    │   └── ...
    ├── ControlScript.cs
    ├── FaceRecognition
    ├── MeshProcessing
    ├── Obstacle Recognition
    ├── Text Recognition
    └── ...
```

## Experimental Results
Initial testing has demonstrated promising results:
- Improved obstacle detection and avoidance
- Higher success rates in text recognition under various lighting conditions
- 30% improvement in object identification when using ACE
- Reduced reliance on multiple separate assistive devices

## Future Development
- Expand object recognition database for more household items
- Implement dynamic user preference learning
- Improve Bluetooth audio integration with hearing aids
- Enhance battery efficiency for extended use

---

*Project HoloAid represents an innovative approach to assistive technology, bringing together AR, computer vision, and audio processing to support users with dual sensory impairments in their daily lives.*
