# Unity Grease Pencil & Silhouette Renderer (WIP)

This is a work-in-progress project exploring NPR (Non-Photorealistic Rendering) techniques in Unity. It currently consists of a renderer for Blender's Grease Pencil data and a system for real-time silhouette edge detection.

## Features

### Grease Pencil Support
- **Data Structure**: `GreasePencilSO` ScriptableObjects are used to hold Grease Pencil data, including layers, frames, strokes, and materials.
  - *Note: The importer script to bring data from Blender into Unity is not currently included in this repository.*
- **Rendering**: The `GreasePencilRenderer` component handles the rendering of the stored stroke data using procedural geometry and Compute Buffers.
- **Animation**: Basic support for frame-by-frame animation playback.

### Silhouette Rendering
- **Edge Detection**: The `SilhouetteEdgeCalculator` component detects silhouette edges on 3D meshes in real-time.
- **Compute Shaders**: Uses Compute Shaders for finding edges, calculating adjacency, and linking edges into strokes.
- **Stroke Rendering**: The `SilhouetteRenderer` renders the detected edges as strokes, utilizing the same rendering logic as the Grease Pencil system.

## Getting Started

### Grease Pencil
1. Create or obtain a `GreasePencilData` asset (requires external data).
2. Create a GameObject and add the `GreasePencilRenderer` component.
3. Assign the `GreasePencilData` asset to the renderer.
4. Configure material settings.

### Silhouette
1. Create a GameObject with a MeshFilter and MeshRenderer.
2. Add the `SilhouetteEdgeCalculator` component.
3. Add the `SilhouetteRenderer` component.
4. Assign a material.
