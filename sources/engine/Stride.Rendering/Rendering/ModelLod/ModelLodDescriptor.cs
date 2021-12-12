// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;

using Stride.Core;
using Stride.Core.Annotations;
using Stride.Core.Serialization.Contents;
using Stride.Rendering.ProceduralModels;
using Stride.Core.IO;
using Stride.Core.Serialization;
using Stride.Graphics.Data;
using Stride.Core.Mathematics;
using Stride.Rendering.Rendering.MeshDataTool;
using Stride.Rendering.Rendering.MeshDecimator.Math;
using Stride.Core.Diagnostics;
using Stride.Rendering.MeshDecimator;
using System.Linq;
using Stride.Graphics;
using Buffer = Stride.Graphics.Buffer;
using System.Collections.Generic;

namespace Stride.Rendering.ModelLod
{
    /// <summary>
    /// A descriptor for a procedural geometry.
    /// </summary>
    [DataContract("ModelLodDescriptor")]
    [ContentSerializer(typeof(ModelLodDescriptorContentSerializer))]
    [ContentSerializer(typeof(DataContentSerializer<ModelLodDescriptor>))]    
    public class ModelLodDescriptor
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ModelLodDescriptor"/> class.
        /// </summary>
        public ModelLodDescriptor()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ModelLodDescriptor"/> class.
        /// </summary>
        /// <param name="type">The type.</param>
        public ModelLodDescriptor(int level, float quality, Model srcModel)
        {
            Level = level;
            Quality = quality;
            SrcModel = srcModel;
        }

        /// <summary>
        /// Gets or sets the type of geometric primitive.
        /// </summary>
        /// <value>The type of geometric primitive.</value>
        [DataMember(10)]
        [Display("Level")]
        public int Level { get; set; }

        /// <summary>
        /// Gets or sets the type of geometric primitive.
        /// </summary>
        /// <value>The type of geometric primitive.</value>
        [DataMember(15)]
        [Display("Quality")]
        public float Quality { get; set; }

        /// <summary>
        /// Gets or sets the type of geometric primitive.
        /// </summary>
        /// <value>The type of geometric primitive.</value>
        [DataMember(20)]
        [Display("SrcModel")]
        public Model SrcModel { get; set; }

        public Model GenerateModel(IServiceRegistry services)
        {
            var model = new Model();
            GenerateModel(services, model);
            return model;
        }

        public void GenerateModel(IServiceRegistry services, Model model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));

            if (SrcModel == null)
            {
                throw new InvalidOperationException("Invalid Lod. No given source model.");
            }

            var service = services.GetService<Stride.Core.IO.IDatabaseFileProviderService>();
            var assetManager = new ContentManager(service);

            var graphicsDevice = services?.GetSafeServiceAs<IGraphicsDeviceService>().GraphicsDevice;
            var convertedMeshes = new List<Mesh>();
            foreach (var mesh in SrcModel.Meshes)
            {
                var convertedMesh = new Mesh();

                var dt = new MeshDataToolGPU(mesh, assetManager);

                var totalIndicies = dt.getTotalIndicies();
                var totalVertices = dt.getTotalVerticies();
                int currentTriangleCount = totalIndicies / 3;

                float newQuality = MathHelper.Clamp01(this.Quality);
                int newTriangleCount = (int)MathF.Ceiling(currentTriangleCount * newQuality);

                var sourceMesh = new MeshDecimatorData(
                    dt.getPositions().Select(d => new Double3(d.X, d.Y, d.Z)).ToArray(), 
                    dt.getIndicies());

                sourceMesh.UV1 = dt.getUVs();
                sourceMesh.Tangents = dt.getTangents();
                sourceMesh.Normals = dt.getNormals();

                var n = sourceMesh.Normals.Length;

                var logger = GlobalLogger.GetLogger("Asset");
                logger.Info(string.Format("[LOD][{0}] Start generate  => Quality {1} => Triangles {2} to {3}", Level, newQuality, currentTriangleCount, newTriangleCount));
                var algorithm = MeshDecimator.MeshDecimator.CreateAlgorithm(Algorithm.FastQuadricMesh);
                var destMesh = MeshDecimator.MeshDecimator.DecimateMesh(algorithm, sourceMesh, newTriangleCount);
                logger.Info(string.Format("[LOD][{0}] Total Triangle Count", Level, (destMesh.Indices.Length / 3)));

                convertedMesh.Draw = new MeshDraw();
                convertedMesh.Draw.PrimitiveType = mesh.Draw.PrimitiveType;

                createIndexBuffer(convertedMesh.Draw, destMesh.Indices, graphicsDevice);

                VertexPositionNormalTexture[] vertices = new VertexPositionNormalTexture[destMesh.VertexCount];
                for(int idx = 0; idx < destMesh.VertexCount; idx++)
                {
                    vertices[idx] = new VertexPositionNormalTexture();
                    vertices[idx].Position = new Vector3(
                        Convert.ToSingle(sourceMesh.Vertices[idx].X), 
                        Convert.ToSingle(sourceMesh.Vertices[idx].Y), 
                        Convert.ToSingle(sourceMesh.Vertices[idx].Z));
                    vertices[idx].TextureCoordinate = sourceMesh.UV1[idx];
                    vertices[idx].Normal = sourceMesh.Normals[idx];
                }

                createVertexBuffer(convertedMesh.Draw, vertices, graphicsDevice);

                convertedMesh.Draw.DrawCount = destMesh.Indices.Length;
                convertedMesh.BoundingBox = mesh.BoundingBox;

                convertedMeshes.Add(convertedMesh);
            }

            model.Meshes = convertedMeshes;
            //model.Materials = SrcModel.Materials;
        }

        private void createVertexBuffer(MeshDraw meshDraw, VertexPositionNormalTexture[] vertices, GraphicsDevice graphicsDevice)
        {
            meshDraw.VertexBuffers = new[] { new VertexBufferBinding(Buffer.Vertex.New(graphicsDevice, vertices).RecreateWith(vertices), VertexPositionNormalTexture.Layout, vertices.Length) };
        }

        private void createIndexBuffer(MeshDraw meshDraw, int[] indices, GraphicsDevice graphicsDevice)
        {
            if (indices.Length < 0xFFFF)
            {
                var indicesShort = new ushort[indices.Length];
                for (int i = 0; i < indicesShort.Length; i++)
                {
                    indicesShort[i] = (ushort)indices[i];
                }
                meshDraw.IndexBuffer = new IndexBufferBinding(Buffer.Index.New(graphicsDevice, indicesShort).RecreateWith(indicesShort), false, indices.Length);
            }
            else
            {
                if (graphicsDevice.Features.CurrentProfile <= GraphicsProfile.Level_9_3)
                {
                    throw new InvalidOperationException("Cannot generate more than 65535 indices on feature level HW <= 9.3");
                }

                meshDraw.IndexBuffer = new IndexBufferBinding(Buffer.Index.New(graphicsDevice, indices).RecreateWith(indices), true, indices.Length);
            }
        }
    }
}
