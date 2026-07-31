using UnityEngine;

public class DomainWarping : MonoBehaviour
{
    public NoiseSettings noiseDomainX, noiseDomainY;
    public int amplitudeX =20, amplitudeY = 20;

    public float GenerateDomainNoise(int x, int z, NoiseSettings defaultNoiseSettings)
    {
        return GenerateDomainNoise(
            x,
            z,
            defaultNoiseSettings,
            defaultNoiseSettings.worldOffset);
    }

    public float GenerateDomainNoise(
        int x,
        int z,
        NoiseSettings defaultNoiseSettings,
        Vector2Int worldOffset)
    {
        Vector2 domainOffset = GenerateDomainOffset(x, z);
        return MyNoise.OctavePerlin(
            x + domainOffset.x,
            z + domainOffset.y,
            defaultNoiseSettings,
            worldOffset);
    }

    public Vector2 GenerateDomainOffset(int x, int z)
    {
        var noiseX = MyNoise.OctavePerlin(x, z, noiseDomainX) * amplitudeX;
        var noiseY = MyNoise.OctavePerlin(x, z, noiseDomainY) * amplitudeY;
        return new Vector2(noiseX, noiseY);
    }

    public Vector2Int GenerateDomainOffsetInt(int x, int z)
    {
        return Vector2Int.RoundToInt(GenerateDomainOffset(x, z));
    }
}
