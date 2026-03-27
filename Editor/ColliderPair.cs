using MagicaCloth2;
using PhysBone = VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone;
using PhysBoneCollider = VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider;

namespace Neigerium.PhysicsConverter.Editor
{
    public class ColliderPair
    {
        public PhysBoneCollider referencePhysboneCollider;
        public ColliderComponent targetMagicaclothCollider;
    }

}
