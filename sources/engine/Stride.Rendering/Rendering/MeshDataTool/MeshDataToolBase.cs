using Stride.Core.Mathematics;

namespace Stride.Rendering.Rendering.MeshDataTool
{
    public abstract class MeshDataToolBase
    {
        protected Mesh mesh { get; set; }
        public MeshDataToolBase(Mesh srcMesh)
        {
            mesh = srcMesh;
        }
        public abstract int getTotalVerticies();
        public abstract int getTotalIndicies();
        public abstract int[] getIndicies();

        public abstract Vector3[] getPositions();
        public abstract Vector2[] getUVs();
        public abstract Vector3[] getNormals();
        public abstract Vector4[] getTangents();

        public abstract Vector3 getPosition(int index);
        public abstract Vector2 getUV(int index);
        public abstract Vector3 getNormal(int index);
        public abstract Vector4 getTangent(int index);

    }
}
