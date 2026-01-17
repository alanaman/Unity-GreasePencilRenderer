# Unity Grease Pencil & Silhouette Renderer

This project provides tools for rendering non-photorealistic (NPR) effects in Unity. It features a system for importing and rendering Blender's Grease Pencil data, as well as a real-time silhouette edge detection and rendering system.

## Features

### Grease Pencil Support
- **Data Import & Storage**: Import and store Grease Pencil data from Blender into Unity using `GreasePencilSO` ScriptableObjects. The data structure supports layers, frames, strokes, points, and materials.
- **Rendering**: The `GreasePencilRenderer` component efficiently renders the stored Grease Pencil data using procedural geometry and Compute Buffers.
- **Animation**: Supports playback of Grease Pencil animations by iterating through frames.

### Silhouette Rendering
- **Real-time Edge Detection**: The `SilhouetteRenderer` and `SilhouetteEdgeCalculator` components work together to detect silhouette edges of 3D meshes in real-time.
- **GPU Acceleration**: Utilizes Compute Shaders for high-performance edge detection, adjacency calculation, and stroke linking (connecting edges into continuous strokes).
- **Stylized Strokes**: Renders detected silhouettes as stylized strokes, sharing the same rendering backend as the Grease Pencil system.

## Getting Started

### Grease Pencil
1. Create a `GreasePencilData` asset (or import one).
2. Create a GameObject and add the `GreasePencilRenderer` component.
3. Assign the `GreasePencilData` asset to the renderer.
4. Configure materials and playback settings.

### Silhouette
1. Create a GameObject with a MeshFilter and MeshRenderer.
2. Add the `SilhouetteEdgeCalculator` component to the GameObject.
3. Add the `SilhouetteRenderer` component to the GameObject.
4. Assign a material and configure stroke settings.
