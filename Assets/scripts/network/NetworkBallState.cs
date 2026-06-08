using Unity.Netcode;
using UnityEngine;

public struct NetworkBallState : INetworkSerializable
{
    public int Type;
    public int Number;
    public bool IsActive;
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 LinearVelocity;
    public Vector3 AngularVelocity;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Type);
        serializer.SerializeValue(ref Number);
        serializer.SerializeValue(ref IsActive);
        serializer.SerializeValue(ref Position);
        serializer.SerializeValue(ref Rotation);
        serializer.SerializeValue(ref LinearVelocity);
        serializer.SerializeValue(ref AngularVelocity);
    }
}
