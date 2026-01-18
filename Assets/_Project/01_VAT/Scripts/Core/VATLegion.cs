using System;
using UnityEngine;

namespace Sandbox_002.VAT
{
    /// <summary>
    /// Renders massive numbers of VAT-animated instances using DrawMeshInstancedIndirect.
    /// RenderTexture-based approach following gpu-texture-data-containers.md:
    /// - RenderTexture as data container (not StructuredBuffer)
    /// - 1 pixel = 1 entity
    /// - CPU initializes once, GPU handles rendering
    /// </summary>
    public class VATLegion : MonoBehaviour, IDisposable
    {
        [Header("Mesh & Material")]
        [SerializeField] private Mesh _mesh;
        [SerializeField] private Material _material;

        [Header("Spawn Settings")]
        [SerializeField] private int _instanceCount = 1000;
        [SerializeField] private float _spawnRadius = 50f;
        [SerializeField] private float _minScale = 0.8f;
        [SerializeField] private float _maxScale = 1.2f;

        [Header("Animation")]
        [SerializeField] private float _animationDuration = 1f;
        [SerializeField] private bool _randomizeTimeOffset = true;

        /// <summary>
        /// RenderTexture storing pre-computed instance data.
        /// Layout: Width = InstanceCount, Height = 2
        /// Row 0: Position (RGB) + TimeOffset (A)
        /// Row 1: cosY (R) + sinY (G) + Scale (B) + Reserved (A)
        /// All trigonometry pre-computed on CPU at initialization.
        /// </summary>
        private RenderTexture _instanceDataTexture;
        private GraphicsBuffer _argsBuffer;
        private Bounds _renderBounds;
        private bool _isInitialized;
        private bool _isDisposed;

        private static readonly int InstanceDataTexID = Shader.PropertyToID("_InstanceDataTex");

        private void Start()
        {
            Initialize();
        }

        private void Initialize()
        {
            if (_mesh == null || _material == null)
            {
                Debug.LogError("VATLegion: Mesh or Material is not assigned.");
                return;
            }

            ReleaseResources();

            // Create RenderTexture as data container (gpu-texture-data-containers.md approach)
            _instanceDataTexture = new RenderTexture(_instanceCount, 2, 0, RenderTextureFormat.ARGBFloat)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            _instanceDataTexture.Create();

            // Create indirect arguments buffer
            _argsBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.IndirectArguments,
                1,
                sizeof(uint) * 5
            );

            // Initialize instance data (CPU → GPU, one-time)
            InitializeInstanceDataTexture();

            // Set indirect arguments
            uint indexCount = _mesh.GetIndexCount(0);
            _argsBuffer.SetData(new uint[] { indexCount, (uint)_instanceCount, 0, 0, 0 });

            // Calculate render bounds
            float maxBound = _spawnRadius + _mesh.bounds.extents.magnitude * _maxScale * 2f;
            _renderBounds = new Bounds(transform.position, Vector3.one * maxBound * 2f);

            _isInitialized = true;
            _isDisposed = false;
        }

        /// <summary>
        /// Initializes instance data in RenderTexture.
        /// Pre-computes all trigonometry on CPU, GPU only reads.
        /// </summary>
        private void InitializeInstanceDataTexture()
        {
            // Create temporary Texture2D to set initial data
            var initTexture = new Texture2D(_instanceCount, 2, TextureFormat.RGBAFloat, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color[_instanceCount * 2];
            Vector3 center = transform.position;

            for (int i = 0; i < _instanceCount; i++)
            {
                // Random position within spawn radius
                Vector2 randomCircle = UnityEngine.Random.insideUnitCircle * _spawnRadius;
                Vector3 position = center + new Vector3(randomCircle.x, 0, randomCircle.y);

                // Random rotation (Y-axis only, in radians)
                float rotY = UnityEngine.Random.Range(0f, Mathf.PI * 2f);

                // Pre-compute trigonometry on CPU (not GPU every frame)
                float cosY = Mathf.Cos(rotY);
                float sinY = Mathf.Sin(rotY);

                // Random scale (uniform)
                float scale = UnityEngine.Random.Range(_minScale, _maxScale);

                // Time offset for animation variation
                float timeOffset = _randomizeTimeOffset
                    ? UnityEngine.Random.Range(0f, _animationDuration)
                    : 0f;

                // Row 0: Position (RGB) + TimeOffset (A)
                pixels[i] = new Color(position.x, position.y, position.z, timeOffset);

                // Row 1: cosY (R) + sinY (G) + Scale (B) + Reserved (A)
                pixels[i + _instanceCount] = new Color(cosY, sinY, scale, 0);
            }

            initTexture.SetPixels(pixels);
            initTexture.Apply();

            // Copy to RenderTexture
            Graphics.Blit(initTexture, _instanceDataTexture);

            // Cleanup temporary texture
            DestroyImmediate(initTexture);

            // Set texture to material once (not every frame)
            _material.SetTexture(InstanceDataTexID, _instanceDataTexture);
        }

        private void Update()
        {
            if (!_isInitialized || _isDisposed) return;

            // Draw all instances in single call (texture already set during initialization)
            Graphics.DrawMeshInstancedIndirect(
                _mesh,
                0,
                _material,
                _renderBounds,
                _argsBuffer
            );
        }

        /// <summary>
        /// Reinitializes the legion with current settings.
        /// </summary>
        public void Reinitialize()
        {
            _isInitialized = false;
            Initialize();
        }

        /// <summary>
        /// Sets the instance count and reinitializes.
        /// </summary>
        public void SetInstanceCount(int count)
        {
            _instanceCount = Mathf.Max(1, count);
            Reinitialize();
        }

        private void ReleaseResources()
        {
            if (_instanceDataTexture != null)
            {
                _instanceDataTexture.Release();
                DestroyImmediate(_instanceDataTexture);
                _instanceDataTexture = null;
            }

            _argsBuffer?.Release();
            _argsBuffer = null;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            ReleaseResources();
        }

        private void OnDisable()
        {
            Dispose();
        }

        private void OnDestroy()
        {
            Dispose();
        }

        private void OnValidate()
        {
            _instanceCount = Mathf.Max(1, _instanceCount);
            _spawnRadius = Mathf.Max(0.1f, _spawnRadius);
            _minScale = Mathf.Max(0.01f, _minScale);
            _maxScale = Mathf.Max(_minScale, _maxScale);
            _animationDuration = Mathf.Max(0.01f, _animationDuration);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0, 1, 0, 0.3f);
            Gizmos.DrawWireSphere(transform.position, _spawnRadius);
        }
    }
}
