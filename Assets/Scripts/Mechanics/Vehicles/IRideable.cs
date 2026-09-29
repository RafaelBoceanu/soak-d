using UnityEngine;

public interface IRideable
{
    Transform RiderAnchor { get; }
    bool FollowHeading { get; }
    void SetControl(bool active);
    void ReadInput(PlayerInputContext input);
    bool OwnsCollider(Collider col);
}
