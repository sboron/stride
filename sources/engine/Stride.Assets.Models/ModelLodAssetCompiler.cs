// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using Stride.Core.Assets;
using Stride.Core.Assets.Analysis;
using Stride.Core.Assets.Compiler;
using Stride.Core.IO;
using Stride.Core.Serialization.Contents;
using Stride.Assets.Materials;
using Stride.Graphics;
using Stride.Rendering.ModelLod;
using Stride.Core.BuildEngine;
using System.Threading.Tasks;

namespace Stride.Assets.Models
{
    [AssetCompiler(typeof(ModelLodAsset), typeof(AssetCompilationContext))]
    public class ModelLodAssetCompiler : AssetCompilerBase
    {
        public override IEnumerable<BuildDependencyInfo> GetInputTypes(AssetItem assetItem)
        {
            yield return new BuildDependencyInfo(typeof(MaterialAsset), typeof(AssetCompilationContext), BuildDependencyType.Runtime | BuildDependencyType.CompileAsset);
        }

        protected override void Prepare(AssetCompilerContext context, AssetItem assetItem, string targetUrlInStorage, AssetCompilerResult result)
        {
            var asset = (ModelLodAsset)assetItem.Asset;
            result.BuildSteps = new AssetBuildStep(assetItem);
            result.BuildSteps.Add(new ModelLodAssetCompileCommand(targetUrlInStorage, asset, assetItem.Package));
        }

        private class ModelLodAssetCompileCommand : AssetCommand<ModelLodAsset>
        {
            public ModelLodAssetCompileCommand(string url, ModelLodAsset parameters, IAssetFinder assetFinder)
                : base(url, parameters, assetFinder)
            {
            }

            protected override Task<ResultStatus> DoCommandOverride(ICommandContext commandContext)
            {
                var assetManager = new ContentManager(MicrothreadLocalDatabases.ProviderService);
                assetManager.Save(Url, new ModelLodDescriptor(Parameters.Level, Parameters.Quality, Parameters.SrcModel));

                return Task.FromResult(ResultStatus.Successful);
            }
        }
    }
}
