using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace Sandbox_002.VAT.Editor
{
    /// <summary>
    /// VAT baking result containing all generated assets.
    /// </summary>
    public class VATBakeResult
    {
        public Texture2D positionTexture;
        public Texture2D normalTexture;
        public Mesh staticMesh;
        public VATData data;
        public Texture originalBaseMap;
        public Color originalBaseColor;
    }

    /// <summary>
    /// Core VAT baking logic.
    /// </summary>
    public static class VATBaker
    {
        /// <summary>
        /// Bakes animation into a position texture and extracts static mesh.
        /// </summary>
        /// <param name="skinnedMeshRenderer">Source skinned mesh</param>
        /// <param name="clip">Animation clip to bake</param>
        /// <param name="frameRate">Sampling frame rate</param>
        /// <param name="textureFormat">Texture format (RGBAHalf or RGBAFloat)</param>
        /// <returns>Bake result containing position texture, static mesh, and metadata</returns>
        public static VATBakeResult BakeAnimation(
            SkinnedMeshRenderer skinnedMeshRenderer,
            AnimationClip clip,
            int frameRate,
            TextureFormat textureFormat = TextureFormat.RGBAHalf)
        {
            var gameObject = skinnedMeshRenderer.gameObject;
            var rootObject = gameObject.transform.root.gameObject;

            int frameCount = Mathf.CeilToInt(clip.length * frameRate);
            int vertexCount = skinnedMeshRenderer.sharedMesh.vertexCount;

            var allPositions = new List<Vector3[]>();
            var allNormals = new List<Vector3[]>();
            var bakedMesh = new Mesh();

            // Sample animation at each frame
            for (int frame = 0; frame < frameCount; frame++)
            {
                float time = (float)frame / frameRate;
                clip.SampleAnimation(rootObject, time);

                skinnedMeshRenderer.BakeMesh(bakedMesh);
                var positions = bakedMesh.vertices;
                var normals = bakedMesh.normals;
                allPositions.Add((Vector3[])positions.Clone());
                allNormals.Add((Vector3[])normals.Clone());
            }

            // Calculate bounds for normalization
            Vector3 posMin = Vector3.positiveInfinity;
            Vector3 posMax = Vector3.negativeInfinity;

            foreach (var framePositions in allPositions)
            {
                foreach (var pos in framePositions)
                {
                    posMin = Vector3.Min(posMin, pos);
                    posMax = Vector3.Max(posMax, pos);
                }
            }

            // Create texture
            int textureWidth = NextPowerOfTwo(vertexCount);
            int textureHeight = frameCount;

            var positionTexture = new Texture2D(textureWidth, textureHeight, textureFormat, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            // Fill texture with normalized positions
            for (int frame = 0; frame < frameCount; frame++)
            {
                var framePositions = allPositions[frame];
                for (int vertex = 0; vertex < vertexCount; vertex++)
                {
                    Vector3 normalized = NormalizePosition(framePositions[vertex], posMin, posMax);
                    positionTexture.SetPixel(vertex, frame, new Color(normalized.x, normalized.y, normalized.z, 1f));
                }

                // Fill remaining pixels with black
                for (int x = vertexCount; x < textureWidth; x++)
                {
                    positionTexture.SetPixel(x, frame, Color.clear);
                }
            }

            positionTexture.Apply();

            // Create normal texture (Point filter - shader does lerp interpolation)
            var normalTexture = new Texture2D(textureWidth, textureHeight, textureFormat, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            // Fill texture with encoded normals ([-1,1] -> [0,1])
            for (int frame = 0; frame < frameCount; frame++)
            {
                var frameNormals = allNormals[frame];
                for (int vertex = 0; vertex < vertexCount; vertex++)
                {
                    Vector3 encoded = EncodeNormal(frameNormals[vertex]);
                    normalTexture.SetPixel(vertex, frame, new Color(encoded.x, encoded.y, encoded.z, 1f));
                }

                // Fill remaining pixels with default normal (0, 0, 1) encoded as (0.5, 0.5, 1)
                for (int x = vertexCount; x < textureWidth; x++)
                {
                    normalTexture.SetPixel(x, frame, new Color(0.5f, 0.5f, 1f, 1f));
                }
            }

            normalTexture.Apply();

            // Extract static mesh (preserves vertex order for VAT playback)
            Mesh staticMesh = ExtractStaticMesh(skinnedMeshRenderer);

            // Set bounds to encompass full animation range (critical for Frustum Culling)
            staticMesh.bounds = new Bounds(
                (posMin + posMax) / 2f,  // center
                posMax - posMin           // size
            );

            Object.DestroyImmediate(bakedMesh);

            var data = new VATData
            {
                vertexCount = vertexCount,
                frameCount = frameCount,
                duration = clip.length,
                positionMin = posMin,
                positionMax = posMax
            };

            return new VATBakeResult
            {
                positionTexture = positionTexture,
                normalTexture = normalTexture,
                staticMesh = staticMesh,
                data = data
            };
        }

        /// <summary>
        /// Extracts a static mesh from a SkinnedMeshRenderer.
        /// The extracted mesh has the same vertex order as the source, which is critical for VAT playback.
        /// </summary>
        private static Mesh ExtractStaticMesh(SkinnedMeshRenderer skinnedMeshRenderer)
        {
            Mesh sourceMesh = skinnedMeshRenderer.sharedMesh;
            Mesh staticMesh = new Mesh();
            staticMesh.name = sourceMesh.name + "_VAT_Static";

            // Copy all mesh data to preserve vertex order
            staticMesh.vertices = sourceMesh.vertices;
            staticMesh.normals = sourceMesh.normals;
            staticMesh.tangents = sourceMesh.tangents;
            staticMesh.uv = sourceMesh.uv;
            staticMesh.uv2 = sourceMesh.uv2;
            staticMesh.colors = sourceMesh.colors;
            staticMesh.triangles = sourceMesh.triangles;

            // Copy submeshes
            staticMesh.subMeshCount = sourceMesh.subMeshCount;
            for (int i = 0; i < sourceMesh.subMeshCount; i++)
            {
                staticMesh.SetSubMesh(i, sourceMesh.GetSubMesh(i));
            }

            // Note: Bounds are set later in BakeAnimation using animation posMin/posMax

            return staticMesh;
        }

        /// <summary>
        /// Normalizes a position to 0-1 range based on bounding box.
        /// </summary>
        private static Vector3 NormalizePosition(Vector3 position, Vector3 min, Vector3 max)
        {
            Vector3 range = max - min;
            return new Vector3(
                range.x > 0 ? (position.x - min.x) / range.x : 0.5f,
                range.y > 0 ? (position.y - min.y) / range.y : 0.5f,
                range.z > 0 ? (position.z - min.z) / range.z : 0.5f
            );
        }

        /// <summary>
        /// Encodes a normal from [-1,1] to [0,1] range for texture storage.
        /// </summary>
        private static Vector3 EncodeNormal(Vector3 normal)
        {
            return normal * 0.5f + new Vector3(0.5f, 0.5f, 0.5f);
        }

        /// <summary>
        /// Returns the next power of two greater than or equal to the input.
        /// </summary>
        private static int NextPowerOfTwo(int value)
        {
            int power = 1;
            while (power < value)
            {
                power *= 2;
            }
            return power;
        }
    }
}
