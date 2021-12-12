// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;

using Stride.Core;
using Stride.Core.Serialization;
using Stride.Core.Serialization.Contents;
using Stride.Graphics;

namespace Stride.Rendering.ModelLod
{
    internal class ModelLodDescriptorContentSerializer : ContentSerializerBase<Model>
    {
        private static readonly DataContentSerializerHelper<ModelLodDescriptor> DataSerializerHelper = new DataContentSerializerHelper<ModelLodDescriptor>();

        public override Type SerializationType
        {
            get { return typeof(ModelLodDescriptor); }
        }

        public override void Serialize(ContentSerializerContext context, SerializationStream stream, Model model)
        {
            var lodModel = new ModelLodDescriptor();
            DataSerializerHelper.Serialize(context, stream, lodModel);

            var services = stream.Context.Tags.Get(ServiceRegistry.ServiceRegistryKey);

            lodModel.GenerateModel(services, model);
        }
    }
}
