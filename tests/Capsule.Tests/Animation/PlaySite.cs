namespace Capsule.Tests.Animation;

/// <summary>Where an animator's <c>Play</c> is made from, which must not change what it shows.</summary>
public enum PlaySite
{
    EntityStart,
    EntityStep,
    ComponentAfterTheAnimator,
    BeforeJoiningMidStep,
}
