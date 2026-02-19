using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using System;

public static class MyNoise
{
    public static float RemapValue(float value, float initialMin, float initialMax, float outputMin, float outputMax)
    {
        return outputMin + (value - initialMin) * (outputMax - outputMin) / (initialMax - initialMin);
    }

    public static float RemapValue01(float value, float outputMin, float outputMax)
    {
        return outputMin + (value - 0) * (outputMax - outputMin) / (1 - 0);
    }

    public static float RemapValue01ToInt(float value, float outputMin, float outputMax)
    {
        return (int)RemapValue01(value, outputMin, outputMax);
    }

    public static float Redistribution(float noise, NoiseSettings noiseSettings)
    {
        float redistributed = Mathf.Pow(noise * noiseSettings.redistributionModifier, noiseSettings.exponent);
        return Mathf.Clamp01(redistributed);
    }

    public static float OctavePerlin(float x, float z, NoiseSettings noiseSettings)
    {
        x *= noiseSettings.noiseZoom;
        z *= noiseSettings.noiseZoom;
        x += noiseSettings.noiseZoom;
        z += noiseSettings.noiseZoom;

        float total = 0;
        float frequency = 1;
        float amplitude = 1;
        float amplitudeSum = 0;

        for (int i = 0; i < noiseSettings.octaves; i++)
        {
            total += Mathf.PerlinNoise((noiseSettings.offset.x + noiseSettings.worldOffset.x + x) * frequency,
             (noiseSettings.offset.y + noiseSettings.worldOffset.y + z) * frequency) * amplitude;

             amplitudeSum += amplitude;

             amplitude *= noiseSettings.persistence;
             frequency *= 2;
        }

        return total / amplitudeSum;
    }
}
