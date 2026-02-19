using UnityEngine;

[CreateAssetMenu(fileName = "noiseSettings", menuName = "Data/NoiseSettings")]

public class NoiseSettings : ScriptableObject
{

    public float noiseZoom;
    public int octaves;
    public float persistence;
    public Vector2Int offset;
    public Vector2Int worldOffset;
    public float redistributionModifier;
    public float exponent;
}
