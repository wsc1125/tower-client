public static class EnemyAttackTimeline
{
    public static bool ShouldFire(int previousFrame, int currentFrame, bool alreadyFired)
    {
        return !alreadyFired && previousFrame < 4 && currentFrame >= 4;
    }
}
