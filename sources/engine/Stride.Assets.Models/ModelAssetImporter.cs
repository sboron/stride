// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System;
using System.Linq;
using System.Collections.Generic;
using Stride.Core;
using Stride.Core.Assets;
using Stride.Core.Assets.Analysis;
using Stride.Core.Diagnostics;
using Stride.Core.Extensions;
using Stride.Core.IO;
using Stride.Core.Serialization;
using Stride.Core.Serialization.Contents;
using Stride.Assets.Materials;
using Stride.Assets.Textures;
using Stride.Rendering;
using Stride.Importer.Common;
using Stride.Core.BuildEngine;

namespace Stride.Assets.Models
{
    public abstract class ModelAssetImporter : AssetImporterBase
    {
        public static readonly PropertyKey<bool> DeduplicateMaterialsKey = new PropertyKey<bool>("DeduplicateMaterials", typeof(ModelAssetImporter));
        public static readonly PropertyKey<int> LodLevelsKey = new PropertyKey<int>("LodLevels", typeof(ModelAssetImporter));
        public static readonly PropertyKey<bool> GenerateLodsKey = new PropertyKey<bool>("GenerateLods", typeof(ModelAssetImporter));

        public override IEnumerable<Type> RootAssetTypes
        {
            get
            {
                yield return typeof(ModelAsset);
                yield return typeof(AnimationAsset);
                yield return typeof(SkeletonAsset);
            }
        }

        public override IEnumerable<Type> AdditionalAssetTypes
        {
            get
            {
                yield return typeof(MaterialAsset);
                yield return typeof(TextureAsset);
                yield return typeof(ModelLodAsset);
            }
        }

        /// <summary>
        /// Get the entity information.
        /// </summary>
        /// <param name="localPath">The path of the asset.</param>
        /// <param name="logger">The logger to use to log import message.</param>
        /// <param name="importParameters">The import parameters.</param>
        /// <returns>The EntityInfo.</returns>
        public abstract EntityInfo GetEntityInfo(UFile localPath, Logger logger, AssetImporterParameters importParameters);

        /// <summary>
        /// Get the total animation clip duration.
        /// </summary>
        /// <param name="localPath">The path of the asset.</param>
        /// <param name="logger">The logger to use to log import message.</param>
        /// <param name="importParameters">The import parameters.</param>
        /// <param name="startTime">Returns the first (start) keyframe's time for the animation</param>
        /// <param name="endTime">Returns the last (end) keyframe's time for the animation</param>
        public abstract void GetAnimationDuration(UFile localPath, Logger logger, AssetImporterParameters importParameters, out TimeSpan startTime, out TimeSpan endTime);

        /// <summary>
        /// Imports the model.
        /// </summary>
        /// <param name="localPath">The path of the asset.</param>
        /// <param name="importParameters">The parameters used to import the model.</param>
        /// <returns>A collection of assets.</returns>
        public override IEnumerable<AssetItem> Import(UFile localPath, AssetImporterParameters importParameters)
        {
            var rawAssetReferences = new List<AssetItem>(); // the asset references without subdirectory path

            if (!importParameters.InputParameters.TryGet(LodLevelsKey, out var lodLevels))
                lodLevels = 0;

            if (!importParameters.InputParameters.TryGet(GenerateLodsKey, out var generateLods))
                generateLods = false;

            var entityInfo = GetEntityInfo(localPath, importParameters.Logger, importParameters);
            if (entityInfo == null)
                return rawAssetReferences;


            //var isImportingEntity = importParameters.IsTypeSelectedForOutput<PrefabAsset>();

            var isImportingModel = importParameters.IsTypeSelectedForOutput<ModelAsset>();
            var isImportingMaterial = importParameters.IsTypeSelectedForOutput<MaterialAsset>();
            var isImportingTexture = importParameters.IsTypeSelectedForOutput<TextureAsset>();

            // 1. Textures
            if (isImportingTexture)
            {
                ImportTextures(entityInfo.TextureDependencies, rawAssetReferences);
            }

            // 2. Skeleton
            AssetItem skeletonAsset = null;
            if (importParameters.IsTypeSelectedForOutput<SkeletonAsset>())
            {
                skeletonAsset = ImportSkeleton(rawAssetReferences, localPath, localPath, entityInfo);
            }

            // 3. Animation
            if (importParameters.IsTypeSelectedForOutput<AnimationAsset>())
            {
                TimeSpan startTime, endTime;
                GetAnimationDuration(localPath, importParameters.Logger, importParameters, out startTime, out endTime);

                ImportAnimation(rawAssetReferences, localPath, entityInfo.AnimationNodes, isImportingModel, skeletonAsset, startTime, endTime);
            }

            // 4. Materials
            if (isImportingMaterial)
            {
                ImportMaterials(rawAssetReferences, entityInfo.Materials);
            }

            // 5. Model
            if (isImportingModel)
            {
                ImportModel(importParameters.Logger, rawAssetReferences, localPath, localPath, entityInfo, false, skeletonAsset, generateLods ? lodLevels : 0 );
            }

            return rawAssetReferences;
        }

        private AssetItem ImportSkeleton(List<AssetItem> assetReferences, UFile assetSource, UFile localPath, EntityInfo entityInfo)
        {
            var asset = new SkeletonAsset { Source = assetSource };

            if (entityInfo.Nodes != null)
            {
                foreach (var node in entityInfo.Nodes)
                {
                    var nodeInfo = new NodeInformation(node.Name, node.Depth, node.Preserve);
                    asset.Nodes.Add(nodeInfo);
                }
            }

            if (entityInfo.AnimationNodes != null && entityInfo.AnimationNodes.Count > 0)
                asset.PreserveNodes(entityInfo.AnimationNodes);

            var skeletonUrl = new UFile(localPath.GetFileNameWithoutExtension() + " Skeleton");
            var assetItem = new AssetItem(skeletonUrl, asset);
            assetReferences.Add(assetItem);
            return assetItem;
        }

        private  void ImportAnimation(List<AssetItem> assetReferences, UFile localPath, List<string> animationNodes, bool shouldPostFixName, AssetItem skeletonAsset, TimeSpan animationStartTime, TimeSpan animationEndTime)
        {
            if (animationNodes != null && animationNodes.Count > 0)
            {
                var assetSource = localPath;

                var asset = new AnimationAsset { Source = assetSource, AnimationTimeMaximum = animationEndTime, AnimationTimeMinimum = animationStartTime };
                var animUrl = localPath.GetFileNameWithoutExtension() + (shouldPostFixName ? " Animation" : "");

                if (skeletonAsset != null)
                    asset.Skeleton = AttachedReferenceManager.CreateProxyObject<Skeleton>(skeletonAsset.Id, skeletonAsset.Location);

                assetReferences.Add(new AssetItem(animUrl, asset));
            }
        }

        private void ImportModel(Logger logger, List<AssetItem> assetReferences, UFile assetSource, UFile localPath, EntityInfo entityInfo, bool shouldPostFixName, AssetItem skeletonAsset, int lodLevels = 0)
        {
            var asset = new ModelAsset { Source = assetSource };
            if (entityInfo.Models != null)
            {
                var loadedMaterials = assetReferences.Where(x => x.Asset is MaterialAsset).ToList();
                foreach (var material in entityInfo.Materials)
                {
                    var modelMaterial = new ModelMaterial
                    {
                        Name = material.Key,
                        MaterialInstance = new MaterialInstance()
                    };
                    var foundMaterial = loadedMaterials.FirstOrDefault(x => x.Location == new UFile(material.Key));
                    if (foundMaterial != null)
                    {
                        var reference = AttachedReferenceManager.CreateProxyObject<Material>(foundMaterial.Id, foundMaterial.Location);
                        modelMaterial.MaterialInstance.Material = reference;
                    }
                    asset.Materials.Add(modelMaterial);
                }
                //handle the case where during import we imported no materials at all
                if (entityInfo.Materials.Count == 0)
                {
                    var modelMaterial = new ModelMaterial { Name = "Material", MaterialInstance = new MaterialInstance() };
                    asset.Materials.Add(modelMaterial);
                }
            }

            if (skeletonAsset != null)
                asset.Skeleton = AttachedReferenceManager.CreateProxyObject<Skeleton>(skeletonAsset.Id, skeletonAsset.Location);

            var modelUrl = new UFile(localPath.GetFileNameWithoutExtension() + (shouldPostFixName ? " Model" : ""));
            var assetItem = new AssetItem(modelUrl, asset);

            if(lodLevels > 0)
            {
                ImportLods(logger, lodLevels, assetReferences, modelUrl, assetSource, asset, assetItem);
            }

            assetReferences.Add(assetItem);
        }
   
        private void ImportLods(Logger logger, int levels, List<AssetItem> assetReferences,  UFile modelUrl, UFile assetSource, ModelAsset asset, AssetItem assetItem)
        {
            var lodModelSrc = AttachedReferenceManager.CreateProxyObject<Model>(assetItem.Id, assetItem.Location); ;

            for (int i = 1; i < levels + 1; i++)
            {
                var newLodAsset = new ModelLodAsset { SrcModel = lodModelSrc, Level = i };
                foreach(var mat in asset.Materials)
                {
                    newLodAsset.Materials.Add(mat);
                }

                var newUrl = new UFile(modelUrl.FullPath + "_LOD" + i);

                var lodAssetItem = new AssetItem(newUrl, newLodAsset);
                assetReferences.Add(lodAssetItem);
                
                var model = AttachedReferenceManager.CreateProxyObject<Model>(lodAssetItem.Id, lodAssetItem.Location);
                asset.Lods.Add( model);
            }
        }
   
        private static void ImportMaterials(List<AssetItem> assetReferences, Dictionary<string, MaterialAsset> materials)
        {
            if (materials != null)
            {
                var loadedTextures = assetReferences.Where(x => x.Asset is TextureAsset).ToList();

                foreach (var materialKeyValue in materials)
                {
                    AdjustForTransparency(materialKeyValue.Value);
                    var material = materialKeyValue.Value;

                    // patch texture name and ids
                    var materialAssetReferences = AssetReferenceAnalysis.Visit(material);
                    foreach (var materialAssetReferenceLink in materialAssetReferences)
                    {
                        var materialAssetReference = materialAssetReferenceLink.Reference as IReference;
                        if (materialAssetReference == null)
                            continue;

                        // texture location is #nameOfTheModel_#nameOfTheTexture at this point in the material
                        var foundTexture = loadedTextures.FirstOrDefault(x => x.Location == materialAssetReference.Location);
                        if (foundTexture != null)
                        {
                            materialAssetReferenceLink.UpdateReference(foundTexture.Id, foundTexture.Location);
                        }
                    }

                    var assetReference = new AssetItem(materialKeyValue.Key, material);
                    assetReferences.Add(assetReference);
                }
            }
        }

        /// <summary>
        /// Modify the material to comply with its transparency parameters.
        /// </summary>
        /// <param name="material">The material/</param>
        private static void AdjustForTransparency(MaterialAsset material)
        {
            //// Note: at this point, there is no other nodes than diffuse, specular, transparent, normal and displacement
            //if (material.ColorNodes.ContainsKey(MaterialParameters.AlbedoDiffuse))
            //{
            //    var diffuseNode = material.GetMaterialNode(MaterialParameters.AlbedoDiffuse);
            //    if (material.ColorNodes.ContainsKey(MaterialParameters.TransparencyMap))
            //    {
            //        var diffuseNodeName = material.ColorNodes[MaterialParameters.AlbedoDiffuse];
            //        var transparentNodeName = material.ColorNodes[MaterialParameters.TransparencyMap];

            //        var transparentNode = material.GetMaterialNode(MaterialParameters.TransparencyMap);

            //        if (diffuseNode == null || transparentNode == null)
            //            return;

            //        var foundTextureDiffuse = FindTextureNode(material, diffuseNodeName);
            //        var foundTextureTransparent = FindTextureNode(material, transparentNodeName);

            //        if (foundTextureDiffuse != null && foundTextureTransparent != null)
            //        {
            //            if (foundTextureDiffuse != foundTextureTransparent)
            //            {
            //                var alphaMixNode = new MaterialBinaryComputeNode(diffuseNode, transparentNode, BinaryOperator.SubstituteAlpha);
            //                material.AddColorNode(MaterialParameters.AlbedoDiffuse, "sd_diffuseWithAlpha", alphaMixNode);
            //            }
            //        }

            //        // set the key if it was missing
            //        material.Parameters.Set(MaterialParameters.HasTransparency, true);
            //    }
            //    else
            //    {
            //        // NOTE: MaterialParameters.HasTransparency is mostly runtime
            //        var isTransparent = false;
            //        if (material.Parameters.ContainsKey(MaterialParameters.HasTransparency))
            //            isTransparent = (bool)material.Parameters[MaterialParameters.HasTransparency];
                    
            //        if (!isTransparent)
            //        {
            //            // remove the diffuse node
            //            var diffuseName = material.ColorNodes[MaterialParameters.AlbedoDiffuse];
            //            material.Nodes.Remove(diffuseName);

            //            // add the new one
            //            var opaqueNode = new MaterialBinaryComputeNode(diffuseNode, null, BinaryOperator.Opaque);
            //            material.AddColorNode(MaterialParameters.AlbedoDiffuse, "sd_diffuseOpaque", opaqueNode);
            //        }
            //    }
            //}
        }

        private static void ImportTextures(IEnumerable<string> textureDependencies, List<AssetItem> assetReferences)
        {
            if (textureDependencies == null)
                return;

            foreach (var textureFullPath in textureDependencies.Distinct(x => x))
            {
                var texturePath = new UFile(textureFullPath);

                var source = texturePath;
                var texture = new TextureAsset { Source = source, Type = new ColorTextureType { PremultiplyAlpha = false } };

                // Create asset reference
                assetReferences.Add(new AssetItem(texturePath.GetFileNameWithoutExtension(), texture));
            }
        }
    }
}
